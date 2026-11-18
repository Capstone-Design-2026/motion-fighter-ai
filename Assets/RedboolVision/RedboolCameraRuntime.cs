using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

/// <summary>Owns only the bundled local camera child; game input keeps its existing UDP lease.</summary>
public sealed class RedboolCameraRuntime : MonoBehaviour
{
    [Serializable] public sealed class Framing { public string code,message; public string[] outside_edges; }
    [Serializable] public sealed class RecordingStatus
    {
        public string state,error; public long queue_dropped_rows,budget_omitted_rows;
    }
    [Serializable] public sealed class CameraStatus
    {
        public int schema,control_count; public bool camera_connected,calibrated,synthetic,ready;
        public float calibration_progress,pose_ms,capture_fps;
        public string phase,calibration_hint,calibration_reason,tracking,error,stream_id,last_command;
        public string inference_mode,inference_warning;
        public bool camera_slow;
        public float acquisition_fps;
        public string camera_profile;
        public long preview_sequence,frame_id; public Framing framing;
        public RecordingStatus recording;
    }
    [Serializable] sealed class Command { public string command,request_id; }
    public static RedboolCameraRuntime Instance { get; private set; }
    public static bool Packaged => File.Exists(BackendPath);
    public static string BackendPath => Path.Combine(Application.streamingAssetsPath,"RedboolCamera",
        Application.platform==RuntimePlatform.WindowsPlayer ? "REDBOOLCamera.exe" : "REDBOOLCamera");
    public CameraStatus Status { get; private set; }
    public Texture2D Preview { get; private set; }
    public string Error { get; private set; }
    public string InputConnectionError { get; private set; }
    public string LastInputConnectionError { get; private set; }
    public bool InputConnectionBlocked { get; private set; }
    public bool Running => child!=null && !Exited(child);
    public bool DiagnosticsPending => child!=null || stoppingChild!=null;
    public int ChildId => Running ? child.Id : 0;
    public bool Fresh => Status!=null && Time.realtimeSinceStartupAsDouble-lastStatus<1.5;
    public bool PreviewFresh => Preview!=null && Time.realtimeSinceStartupAsDouble-lastPreview<1;
    public const int PreviewTargetFps=15;
    public int CurrentPreviewTargetFps { get; private set; } = PreviewTargetFps;
    public long PreviewSequence => previewSequence;
    public long PreviewDecodedFrames { get; private set; }
    public bool ShowPanel { get; private set; }
    public bool PreviewVisible { get; set; } = true;
    public bool Synthetic { get; private set; }
    public bool InferencePressure => Status!=null && Status.inference_warning=="AUXILIARY_DISABLED_FOR_LATENCY";
    readonly RedboolRenderPressure renderPressure = new RedboolRenderPressure();
    public bool JointRenderPressure => Running && renderPressure.Latched;
    Process child,stoppingChild; StreamWriter childLog;
    Coroutine statusRoutine,previewRoutine;
    string role,address="127.0.0.1",token,url; int generation,port;
    int inputPort;
    bool onlineInput;
    bool restartRequested,quitting,commandPending;
    double lastStatus=-100,lastPreview=-100;
    long previewSequence=-1; readonly object logLock=new object();
    long logBytes; int previewSamples,previewRequests,frameSamples,healthyPreviewWindows;
    double decodeTotal,decodeMax,requestTotal,requestMax,metricStarted,metricAt;
    double lastFrameAt=-1,frameElapsed,nextReceiverRetry;
    long lastCameraFrameId=-1;
    double lastCameraFrameAt=-100,renderFpsTotal,renderFpsSeconds;
    bool renderWindowEligible=true,severeRenderWindowEligible=true;
    int renderSceneHandle=-1;
    GUIStyle connectionNoticeStyle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatic() { Instance=null; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        // Existing development/editor launches stay manual. Explicit native
        // synthetic tests still need the actual bundled backend.
        var args=Environment.GetCommandLineArgs();
        bool synthetic=RedboolCameraCheckMode.Authorize(args,Environment.GetEnvironmentVariable(RedboolCameraCheckMode.OutputVariable),
            Application.isEditor,Application.isBatchMode,out string checkError);
        if(RedboolCameraCheckMode.Requested(args)&&!synthetic)
        {
            Debug.LogError("[REDBOOL OFFLINE CHECK] "+checkError+" No camera runtime created.");
            if(!Application.isEditor)Application.Quit(2);
            return;
        }
        if(Application.isEditor || Application.isBatchMode || !Packaged || Instance!=null) return;
        if(Array.IndexOf(args,"--redbool-external-camera")>=0) return;
        var go=new GameObject("REDBOOL Integrated Camera");DontDestroyOnLoad(go);
        Instance=go.AddComponent<RedboolCameraRuntime>();
        Instance.Synthetic=synthetic;
        go.AddComponent<RedboolCameraPanel>();
    }
    void Start() { RedboolDemoSettings.CameraReconnectRequested += Reconnect; StartCoroutine(Supervise()); }
    string DesiredRole()
    {
        if(RedboolPvp.Active) return RedboolPvp.IsClient ? "P2" : "P1";
        if(SceneManager.GetActiveScene().name==SceneRoutes.Start) return null;
        if(role=="DEV01") return role; // Retain one calibration through story/map transitions.
        if(FindAnyObjectByType<CaveTutorial>()!=null || FindAnyObjectByType<RedboolBattlePreparation>()!=null) return "DEV01";
        return null;
    }
    static int DesiredInputPort(string wanted) => wanted=="P2" && RedboolPvp.Online
        ? RedboolOnlineInputBridge.LocalPort : wanted=="P2"?5066:5065;
    bool TransportMatches(string wanted) => inputPort==DesiredInputPort(wanted)
        && onlineInput==(wanted=="P2" && RedboolPvp.Online);
    bool LocalReceiversReady(string wanted,bool retainStoryCamera,out string issue,out bool fault)
    {
        issue=null;fault=false;
        if(wanted=="P2" && RedboolPvp.Online)
        {
            bool ready=RedboolOnlineInputBridge.LocalPort>0;
            if(!ready) {issue="온라인 입력 연결을 준비하고 있어요 · 계속되면 메뉴에서 다시 참가해주세요";fault=true;}
            return ready;
        }
        if(wanted==null||wanted=="P2")return true; // The guest sends to its chosen remote host.
        var scene=SceneManager.GetActiveScene();
        var receivers=FindObjectsByType<RedboolDemoReceiver>(FindObjectsInactive.Include,FindObjectsSortMode.None);
        int count=wanted=="P1"?2:1;
        for(int slot=0;slot<count;slot++)
        {
            int expectedPort=5065+slot;string actor=wanted=="P1"?"P"+(slot+1):"DEV01";
            RedboolDemoReceiver failed=null;bool bound=false;
            foreach(var receiver in receivers)
            {
                if(!receiver.gameObject.activeInHierarchy||receiver.gameObject.scene!=scene||receiver.port!=expectedPort||receiver.expectedActorId!=actor)continue;
                if(receiver.Bound){bound=true;break;}
                if(!string.IsNullOrEmpty(receiver.BindError))failed=receiver;
            }
            if(bound)continue;
            if(failed!=null)
            {
                fault=true;
                issue=failed.PortConflict ? "다른 게임과 연결이 겹쳤어요\n이전 게임을 닫으면 재연결돼요"
                    : "게임 입력 연결을 열지 못했어요\n잠시 후 자동으로 다시 연결해요";
                return false;
            }
            // Keep one calibration across ordinary story/map transitions. A
            // failed receiver is never treated as this harmless scene gap.
            if(wanted=="DEV01"&&retainStoryCamera&&FindAnyObjectByType<CaveTutorial>()==null&&FindAnyObjectByType<RedboolBattlePreparation>()==null)continue;
            issue="게임 입력 연결을 준비하고 있어요";return false;
        }
        return true;
    }
    bool CheckReceiverGate(string wanted,bool retainStoryCamera)
    {
        bool ready=LocalReceiversReady(wanted,retainStoryCamera,out string issue,out bool fault);
        InputConnectionError=issue;
        if(fault)
        {
            if(LastInputConnectionError!=issue)Debug.LogWarning("[REDBOOL CAMERA] local receiver unavailable; own camera withheld. role="+wanted);
            LastInputConnectionError=issue;
        }
        else if(ready&&wanted!=null)LastInputConnectionError=null;
        return ready;
    }
    void RetryLocalReceivers(string wanted)
    {
        if(wanted==null||wanted=="P2"||Time.realtimeSinceStartupAsDouble<nextReceiverRetry)return;
        nextReceiverRetry=Time.realtimeSinceStartupAsDouble+1;
        var scene=SceneManager.GetActiveScene();
        foreach(var receiver in FindObjectsByType<RedboolDemoReceiver>(FindObjectsInactive.Include,FindObjectsSortMode.None))
        {
            bool owns=wanted=="P1"?receiver.PvpOwned&&(receiver.port==5065||receiver.port==5066)
                :receiver.expectedActorId=="DEV01"&&receiver.port==5065;
            if(owns&&receiver.gameObject.activeInHierarchy&&receiver.gameObject.scene==scene&&!receiver.Bound&&!string.IsNullOrEmpty(receiver.BindError))receiver.RetryBinding();
        }
    }
    IEnumerator Supervise()
    {
        while(!quitting)
        {
            string wanted=DesiredRole();string host=wanted=="P2"?RedboolPvp.HostAddress:"127.0.0.1";
            ShowPanel=wanted!=null && (RedboolPvp.Active || FindAnyObjectByType<CaveTutorial>()!=null || FindAnyObjectByType<RedboolBattlePreparation>()!=null);
            if(wanted!=role || host!=address || !TransportMatches(wanted) || restartRequested)
            {
                restartRequested=false;yield return StopOwned();role=wanted;address=host;
                inputPort=DesiredInputPort(wanted);onlineInput=wanted=="P2" && RedboolPvp.Online;
                InputConnectionBlocked=false;InputConnectionError=null;
                if(role!=null) yield return StartAuthorized();
            }
            if(role!=null)
            {
                if(!CheckReceiverGate(role,Running))
                {
                    InputConnectionBlocked=true;
                    if(child!=null)yield return StopOwned();
                    RetryLocalReceivers(role);
                }
                else if(InputConnectionBlocked)
                {
                    InputConnectionBlocked=false;
                    if(child==null)yield return StartAuthorized();
                }
            }
            if(child!=null && Exited(child))
            {
                Error="카메라 연결이 종료됐어요. 다시 연결을 눌러주세요.";
                yield return StopOwned();
            }
            yield return new WaitForSecondsRealtime(.2f);
        }
    }
    void OnGUI()
    {
        if(SceneManager.GetActiveScene().name!=SceneRoutes.Start||string.IsNullOrEmpty(LastInputConnectionError)||RedboolPauseMenu.IsOpen)return;
        if(connectionNoticeStyle==null)connectionNoticeStyle=new GUIStyle(GUI.skin.box){font=WuxiaTheme.Font,fontSize=18,wordWrap=true,alignment=TextAnchor.MiddleLeft};
        GUI.Box(new Rect(20,Screen.height-116,Mathf.Min(620,Screen.width-40),90),
            LastInputConnectionError+"\n이전에 켠 REDBOOL·Unity가 있다면 종료 후 다시 들어가세요.",connectionNoticeStyle);
    }
    bool CheckLaunchMode()
    {
        var args=Environment.GetCommandLineArgs();
        bool authorized=RedboolCameraCheckMode.Authorize(args,Environment.GetEnvironmentVariable(RedboolCameraCheckMode.OutputVariable),
            Application.isEditor,Application.isBatchMode,out string modeError);
        if((RedboolCameraCheckMode.Requested(args)||Synthetic)&&!authorized)
        {
            Error=modeError??"Offline fixture authorization disappeared; camera launch blocked.";
            Debug.LogError("[REDBOOL OFFLINE CHECK] "+Error);return false;
        }
        Synthetic=authorized;return true;
    }
    IEnumerator StartAuthorized()
    {
        // Re-check before any OS permission request, including role changes,
        // reconnects and components created outside the usual Install path.
        if(!CheckLaunchMode())yield break;
        string initialRole=DesiredRole();
        string initialAddress=initialRole=="P2"?RedboolPvp.HostAddress:"127.0.0.1";
        if(quitting || restartRequested || role!=initialRole || address!=initialAddress || !TransportMatches(initialRole))yield break;
        if(!CheckReceiverGate(role,false)){InputConnectionBlocked=true;yield break;}
        // macOS support was added in Unity2023.1.0a19 (official release notes);
        // this Unity6 main-thread preflight avoids OpenCV's first-use permission
        // request from the capture worker. Synthetic checks never request access.
        if(!Synthetic && !Application.isEditor && Application.platform==RuntimePlatform.OSXPlayer
            && !Application.HasUserAuthorization(UserAuthorization.WebCam))
        {
            Error="카메라 권한 허용 창을 확인해주세요.";
            yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
            if(quitting)yield break;
            if(!Application.HasUserAuthorization(UserAuthorization.WebCam))
            {
                Error="카메라 권한이 필요해요. 시스템 설정의 카메라에서 허용 후 다시 연결을 누르세요.";
                // role remains selected: no child or automatic prompt/retry loop.
                // Reconnect is the explicit retry after changing OS permission.
                yield break;
            }
        }
        string wanted=DesiredRole();
        string wantedAddress=wanted=="P2"?RedboolPvp.HostAddress:"127.0.0.1";
        if(quitting || restartRequested || role!=wanted || address!=wantedAddress || !TransportMatches(wanted))yield break;
        if(!CheckReceiverGate(role,false)){InputConnectionBlocked=true;yield break;}
        InputConnectionBlocked=false;StartOwned();
    }
    void Update()
    {
        // KeyDown must be read every frame, including while the game is paused.
        if(ShowPanel && Running && !RedboolPauseMenu.BlocksStoryInput && Input.GetKeyDown(KeyCode.C)) Calibrate();
        // The backend warning stays latched until this camera session ends.
        // Recalibration must not raise preview work while inference is overloaded.
        ApplyInferencePressure();
        double now=Time.realtimeSinceStartupAsDouble;
        if(lastFrameAt>=0) {
            double interval=now-lastFrameAt;
            frameElapsed+=interval;frameSamples++;
            // Status HTTP freshness alone is insufficient: the camera's actual
            // processed frame must also keep advancing. Do not count sleep gaps.
            float fps=Status!=null?Status.capture_fps:0;
            bool active=Running && ShowPanel && Fresh && !Synthetic && Status.camera_connected
                && now-lastCameraFrameAt<1.5 && Application.isFocused && !RedboolPauseMenu.BlocksStoryInput
                && Status.phase!="paused" && interval>0 && interval<1
                && fps>0 && !float.IsNaN(fps) && !float.IsInfinity(fps);
            renderWindowEligible &= active;
            if(active) {renderFpsTotal+=fps*interval;renderFpsSeconds+=interval;}
            int sceneHandle=SceneManager.GetActiveScene().handle;
            severeRenderWindowEligible &= sceneHandle==renderSceneHandle
                && RedboolRenderPressure.IsStableGameFrame(Running,ShowPanel,Application.isFocused,
                    RedboolPauseMenu.BlocksStoryInput,Synthetic,Status!=null && Status.phase=="paused",interval);
            renderSceneHandle=sceneHandle;
        }
        lastFrameAt=now;
        if(now>=metricAt)
        {
            double elapsed=now-metricStarted;
            double meanFrame=frameSamples>0?frameElapsed/frameSamples*1000:0;
            double meanDecode=previewSamples>0?decodeTotal/previewSamples:0;
            double meanRequest=previewRequests>0?requestTotal/previewRequests:0;
            int windowTarget=CurrentPreviewTargetFps;
            bool wasPressure=renderPressure.Latched;
            double cameraFps=renderFpsSeconds>0?renderFpsTotal/renderFpsSeconds:0;
            renderPressure.Observe(generation,Running,renderWindowEligible && frameSamples>0,
                elapsed,cameraFps,meanFrame,severeRenderWindowEligible && frameSamples>0);
            if(!wasPressure && renderPressure.Latched)
                Debug.Log(FormattableString.Invariant($"[REDBOOL JOINT RENDER PRESSURE] generation={generation} camera_fps={cameraFps:F2} game_mean_frame_ms={meanFrame:F3} windows={renderPressure.BadWindows} severe_windows={renderPressure.SevereGameWindows} sustained_windows={renderPressure.SustainedGameWindows} reason={renderPressure.Trigger} target=1280x720 session_latched=true backend_unchanged=true"));
            if(Running && frameSamples>0)
            {
                if(InferencePressure || meanFrame>22 || meanDecode>2 || meanRequest>80)
                {
                    CurrentPreviewTargetFps=8;healthyPreviewWindows=0;
                }
                else if(CurrentPreviewTargetFps<PreviewTargetFps && ++healthyPreviewWindows>=2)
                {
                    CurrentPreviewTargetFps=PreviewTargetFps;healthyPreviewWindows=0;
                }
            }
            if(elapsed>0 && previewRequests>0)
                Debug.Log(FormattableString.Invariant($"[REDBOOL CAMERA PREVIEW] samples={previewSamples} elapsed_s={elapsed:F3} delivered_fps={previewSamples/elapsed:F2} target_fps={windowTarget} next_target_fps={CurrentPreviewTargetFps} game_mean_frame_ms={meanFrame:F3} requests={previewRequests} request_mean_ms={meanRequest:F3} request_max_ms={requestMax:F3} decode_mean_ms={meanDecode:F3} decode_max_ms={decodeMax:F3} sequence={previewSequence} local_only=true"));
            previewSamples=previewRequests=0;decodeTotal=decodeMax=requestTotal=requestMax=0;
            frameSamples=0;frameElapsed=0;
            renderFpsTotal=renderFpsSeconds=0;renderWindowEligible=severeRenderWindowEligible=true;
            metricStarted=now;metricAt=now+5;
        }
    }
    void ApplyInferencePressure()
    {
        if(Running && InferencePressure) {CurrentPreviewTargetFps=8;healthyPreviewWindows=0;}
    }
    static bool Exited(Process process) { try { return process.HasExited; } catch(InvalidOperationException) { return true; } }
    static string Quote(string value)
    {
        // ProcessStartInfo does not invoke a shell. Escape Windows/Mono's quoted argument syntax.
        var result=new StringBuilder("\"");int slashes=0;
        foreach(char c in value)
        {
            if(c=='\\') { slashes++;continue; }
            if(c=='\"') { result.Append('\\',slashes*2+1);result.Append(c);slashes=0;continue; }
            result.Append('\\',slashes);slashes=0;result.Append(c);
        }
        result.Append('\\',slashes*2);return result.Append('"').ToString();
    }
    void StartOwned()
    {
        // Defence in depth: no partial test configuration may start a real child.
        if(!CheckLaunchMode())return;
        Error=null;Status=null;lastStatus=lastPreview=-100;previewSequence=-1;PreviewDecodedFrames=0;generation++;
        renderPressure.Reset(generation);lastCameraFrameId=-1;lastCameraFrameAt=-100;
        renderFpsTotal=renderFpsSeconds=0;renderWindowEligible=severeRenderWindowEligible=true;
        renderSceneHandle=SceneManager.GetActiveScene().handle;
        previewSamples=previewRequests=0;decodeTotal=decodeMax=requestTotal=requestMax=0;
        metricStarted=Time.realtimeSinceStartupAsDouble;metricAt=metricStarted+5;
        lastFrameAt=metricStarted;frameSamples=healthyPreviewWindows=0;frameElapsed=0;
        CurrentPreviewTargetFps=PreviewTargetFps;
        try
        {
        var reservation=new TcpListener(IPAddress.Loopback,0);
        try { RedboolSocketOwnership.PreventInheritance(reservation.Server,"TCP camera bridge reservation 127.0.0.1:0");
            reservation.Start();port=((IPEndPoint)reservation.LocalEndpoint).Port; }
        finally { reservation.Stop(); }
        token=Guid.NewGuid().ToString("N")+Guid.NewGuid().ToString("N");url="http://127.0.0.1:"+port;
        string diagnosticRoot=Path.Combine(Application.persistentDataPath,"CameraDiagnostics");
        string fixtureRoot=Environment.GetEnvironmentVariable("REDBOOL_CAMERA_CHECK_OUTPUT");
        if(Synthetic&&!string.IsNullOrEmpty(fixtureRoot))diagnosticRoot=Path.Combine(fixtureRoot,"children");
        string output=Path.Combine(diagnosticRoot,DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));
        Directory.CreateDirectory(output);
        var arguments=new List<string> {"--headless","--bridge-port",port.ToString(),"--bridge-token",token,
            "--parent-pid",Process.GetCurrentProcess().Id.ToString(),"--actor",role,"--host",address,
            "--port",inputPort.ToString(),"--personal-calibration","--movement","--mirror",
            "--camera",RedboolDemoSettings.CameraIndex.ToString(),"--cross-refine-pose","bundled",
            "--preview-fps",PreviewTargetFps.ToString(),"--recording","diagnostics","--seconds","43200","--output",output};
        if(Synthetic) arguments.Add("--synthetic-bridge-check");
        if(onlineInput) {arguments.Add("--network-profile");arguments.Add("online");}
        var escaped=new List<string>();foreach(string arg in arguments) escaped.Add(Quote(arg));
        var info=new ProcessStartInfo(BackendPath,string.Join(" ",escaped)) { UseShellExecute=false,CreateNoWindow=true,
            WorkingDirectory=Path.GetDirectoryName(BackendPath),RedirectStandardOutput=true,RedirectStandardError=true,
            StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8 };
        info.EnvironmentVariables["PYTHONUNBUFFERED"]="1";
        info.EnvironmentVariables["REDBOOL_CAMERA_CACHE_DIR"]=Synthetic
            ? Path.Combine(Environment.GetEnvironmentVariable(RedboolCameraCheckMode.OutputVariable),"cache")
            : Path.Combine(Application.persistentDataPath,"CameraCache");
            lock(logLock) { childLog=new StreamWriter(Path.Combine(output,"backend.log"),false,Encoding.UTF8);childLog.AutoFlush=true;logBytes=0; }
            child=new Process { StartInfo=info };
            child.OutputDataReceived+=CaptureLog;child.ErrorDataReceived+=CaptureLog;
            if(!child.Start()) throw new IOException("Backend did not start");
            child.BeginOutputReadLine();child.BeginErrorReadLine();
            statusRoutine=StartCoroutine(PollStatus(generation));previewRoutine=StartCoroutine(PollPreview(generation));
            Debug.Log($"[REDBOOL CAMERA] child_started role={role} synthetic={Synthetic} diagnostics={output}");
        }
        catch(Exception error)
        {
            Error="카메라를 시작하지 못했어요. 다시 연결을 눌러주세요.";
            Debug.LogError("[REDBOOL CAMERA] start failed: "+error.Message);
            if(child!=null) { TerminateOwned(child);child.Dispose();child=null; } CloseLog();
        }
    }
    void CaptureLog(object sender,DataReceivedEventArgs e)
    {
        if(e.Data==null)return;
        lock(logLock)
        {
            if(childLog==null || logBytes>2*1024*1024 || (!ReferenceEquals(sender,child)&&!ReferenceEquals(sender,stoppingChild)))return;
            string line=e.Data.Length>4096?e.Data.Substring(0,4096):e.Data;
            try { childLog.WriteLine(line);logBytes+=Encoding.UTF8.GetByteCount(line)+1; }
            catch(IOException) { logBytes=3*1024*1024; } // A full disk must not terminate camera supervision.
        }
    }
    void CloseLog()
    {
        lock(logLock)
        {
            try { childLog?.Dispose(); }
            catch(IOException) { }
            finally { childLog=null; }
        }
    }
    UnityWebRequest Get(string path)
    {
        var request=UnityWebRequest.Get(url+path);request.SetRequestHeader("X-REDBOOL-Token",token);request.timeout=2;return request;
    }
    IEnumerator PollStatus(int ownedGeneration)
    {
        while(ownedGeneration==generation && Running)
        {
            using(var request=Get("/status"))
            {
                yield return request.SendWebRequest();
                if(ownedGeneration!=generation)yield break;
                if(request.result==UnityWebRequest.Result.Success && request.downloadHandler.data!=null && request.downloadHandler.data.Length<=16384)
                {
                    CameraStatus next=null;try { next=JsonUtility.FromJson<CameraStatus>(request.downloadHandler.text); } catch(ArgumentException) { }
                    if(next!=null && next.schema==1) {
                        Status=next;lastStatus=Time.realtimeSinceStartupAsDouble;
                        if(next.camera_connected && next.frame_id>lastCameraFrameId)
                        {
                            lastCameraFrameId=next.frame_id;lastCameraFrameAt=lastStatus;
                            // Calibration/selection also consumes live camera frames. Keep the
                            // Mac event loop responsive before valid gesture packets exist.
                            // Only advancing frames renew the bounded OS activity lease; this
                            // never grants tracking/readiness or extends network input validity.
                            RedboolFrameMetrics.NoteLiveInput();
                        }
                    }
                }
            }
            yield return new WaitForSecondsRealtime(.2f);
        }
    }
    IEnumerator PollPreview(int ownedGeneration)
    {
        double nextRequestAt=Time.realtimeSinceStartupAsDouble;
        while(ownedGeneration==generation && Running)
        {
            if(!PreviewVisible || !ShowPanel)
            {
                // Folding the panel stops HTTP polling; recognition stays full-rate.
                nextRequestAt=Time.realtimeSinceStartupAsDouble;
                yield return new WaitForSecondsRealtime(.125f);
                continue;
            }
            double now=Time.realtimeSinceStartupAsDouble;
            if(now<nextRequestAt) { yield return null;continue; }
            double interval=1.0/CurrentPreviewTargetFps;
            nextRequestAt+=interval;
            if(nextRequestAt<=now)nextRequestAt=now+interval;
            using(var request=Get("/preview.jpg"))
            {
                double requestedAt=Time.realtimeSinceStartupAsDouble;
                yield return request.SendWebRequest();
                if(ownedGeneration!=generation)yield break;
                double requestMs=(Time.realtimeSinceStartupAsDouble-requestedAt)*1000;
                previewRequests++;requestTotal+=requestMs;requestMax=Math.Max(requestMax,requestMs);
                byte[] bytes=request.downloadHandler.data;
                if(request.result==UnityWebRequest.Result.Success && request.responseCode==200 && bytes!=null && bytes.Length<=256*1024
                    && long.TryParse(request.GetResponseHeader("X-REDBOOL-Sequence"),out long sequence) && sequence>previewSequence)
                {
                    if(Preview==null)Preview=new Texture2D(320,240,TextureFormat.RGB24,false);
                    var watch=Stopwatch.StartNew();bool loaded=Preview.LoadImage(bytes,false);watch.Stop();
                    if(loaded) {
                        previewSequence=sequence;lastPreview=Time.realtimeSinceStartupAsDouble;previewSamples++;PreviewDecodedFrames++;decodeTotal+=watch.Elapsed.TotalMilliseconds;decodeMax=Math.Max(decodeMax,watch.Elapsed.TotalMilliseconds);
                        // Fresh JPEGs are live work even in the uncalibrated waiting room.
                        // Duplicate/stale responses do not get here and cannot retain activity.
                        RedboolFrameMetrics.NoteLiveInput();
                    }
                }
            }
            // HTTP and decoding consume this deadline's budget. Never append a
            // full interval after every response, or issue catch-up requests.
            double completedAt=Time.realtimeSinceStartupAsDouble;
            if(completedAt>=nextRequestAt)nextRequestAt=completedAt+interval;
            yield return null;
        }
    }
    public void Calibrate()
    {
        if(Running&&Fresh&&!commandPending&&(Synthetic||Status.camera_connected))StartCoroutine(SendCommand("calibrate"));
    }
    public void Reconnect() { restartRequested=true;nextReceiverRetry=0; }
    IEnumerator SendCommand(string action)
    {
        commandPending=true;int ownedGeneration=generation;
        byte[] bytes=Encoding.UTF8.GetBytes(JsonUtility.ToJson(new Command {command=action,request_id=Guid.NewGuid().ToString("N")}));
        using(var request=new UnityWebRequest(url+"/control","POST"))
        {
            request.uploadHandler=new UploadHandlerRaw(bytes);request.downloadHandler=new DownloadHandlerBuffer();request.timeout=2;
            request.SetRequestHeader("Content-Type","application/json");request.SetRequestHeader("X-REDBOOL-Token",token);
            yield return request.SendWebRequest();
            if(ownedGeneration==generation && request.result!=UnityWebRequest.Result.Success) Error="보정 요청을 보내지 못했어요. 연결 상태를 확인해주세요.";
            else if(ownedGeneration==generation) Error=null;
        }
        if(ownedGeneration==generation)commandPending=false;
    }
    IEnumerator StopOwned()
    {
        generation++;if(statusRoutine!=null)StopCoroutine(statusRoutine);if(previewRoutine!=null)StopCoroutine(previewRoutine);
        renderPressure.Reset(generation);
        statusRoutine=previewRoutine=null;var owned=child;stoppingChild=owned;child=null;
        if(owned!=null && !Exited(owned))
        {
            byte[] bytes=Encoding.UTF8.GetBytes("{\"command\":\"stop\",\"request_id\":\""+Guid.NewGuid().ToString("N")+"\"}");
            using(var request=new UnityWebRequest(url+"/control","POST"))
            {
                request.uploadHandler=new UploadHandlerRaw(bytes);request.downloadHandler=new DownloadHandlerBuffer();request.timeout=1;
                request.SetRequestHeader("Content-Type","application/json");request.SetRequestHeader("X-REDBOOL-Token",token);
                yield return request.SendWebRequest();
            }
            // Landmark chunks drain on their own worker. Keep the menu responsive
            // while allowing final checksums/index/summary to finish before export.
            double end=Time.realtimeSinceStartupAsDouble+5;
            while(!Exited(owned)&&Time.realtimeSinceStartupAsDouble<end)yield return null;
            TerminateOwned(owned);
        }
        owned?.Dispose();if(ReferenceEquals(stoppingChild,owned))stoppingChild=null;
        CloseLog();Status=null;lastStatus=lastPreview=-100;commandPending=false;
    }
    void OnApplicationQuit()
    {
        quitting=true;generation++;var owned=child;child=null;
        var pending=stoppingChild;stoppingChild=null;
        if(pending!=null && !ReferenceEquals(pending,owned)) { TerminateOwned(pending);pending.Dispose(); }
        // Parent-watchdog is the crash fallback. Only this process's own child
        // is terminated here, never another user's camera/developer session.
        if(owned!=null)
        {
            try
            {
                if(!Exited(owned))
                {
                    try
                    {
                        var request=(HttpWebRequest)WebRequest.Create(url+"/control");request.Proxy=null;
                        request.Method="POST";request.Timeout=400;request.ReadWriteTimeout=400;
                        request.ContentType="application/json";request.Headers["X-REDBOOL-Token"]=token;
                        byte[] body=Encoding.UTF8.GetBytes("{\"command\":\"stop\",\"request_id\":\"quit-"+Guid.NewGuid().ToString("N")+"\"}");
                        request.ContentLength=body.Length;using(var stream=request.GetRequestStream())stream.Write(body,0,body.Length);
                        using(var response=request.GetResponse()) { }
                    }
                    catch(WebException) { }
                    if(!owned.WaitForExit(1500))owned.Kill();
                }
            }
            catch(InvalidOperationException) { }
            catch(System.ComponentModel.Win32Exception) { }
            catch(IOException) { }
            owned.Dispose();
        }
        CloseLog();
    }
    static void TerminateOwned(Process owned)
    {
        try { if(!Exited(owned))owned.Kill(); }
        catch(InvalidOperationException) { }
        catch(System.ComponentModel.Win32Exception) { }
    }
    void OnDestroy()
    {
        RedboolDemoSettings.CameraReconnectRequested -= Reconnect;
        // Scene teardown or removal is distinct from application exit: the
        // parent PID remains alive, so its watchdog cannot clean up this child.
        if(child!=null||stoppingChild!=null)OnApplicationQuit();
        if(Preview!=null)Destroy(Preview);if(Instance==this)Instance=null;
    }
}
