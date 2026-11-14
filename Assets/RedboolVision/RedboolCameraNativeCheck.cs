using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug=UnityEngine.Debug;

/// <summary>Explicit development-only packaged lifecycle fixture. Synthetic camera only.</summary>
public sealed class RedboolCameraNativeCheck : MonoBehaviour
{
    [Serializable] sealed class Report {public bool passed;public string error;public List<string> checks;public List<int> children;public double preview_fps;public float render_scale;public int width,height;}
    readonly List<string> checks=new List<string>();
    readonly List<int> children=new List<int>();
    string directory;bool finished;
    double measuredPreviewFps;
    float measuredRenderScale;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        var args=Environment.GetCommandLineArgs();
        if(Array.IndexOf(args,"--redbool-camera-check")<0)return;
        if(!Debug.isDebugBuild||Application.isEditor||Application.isBatchMode||Array.IndexOf(args,"--redbool-camera-synthetic")<0)
        {Debug.LogError("Camera check requires a non-batch development player and --redbool-camera-synthetic; no camera started.");Application.Quit(2);return;}
        var root=new GameObject("REDBOOL Synthetic Camera Check");DontDestroyOnLoad(root);root.AddComponent<RedboolCameraNativeCheck>();
    }
    IEnumerator Start()
    {
        directory=Environment.GetEnvironmentVariable("REDBOOL_CAMERA_CHECK_OUTPUT");
        if(string.IsNullOrEmpty(directory))directory=Path.Combine(Application.persistentDataPath,"SyntheticCameraCheck");
        Directory.CreateDirectory(directory);
        var flow=Flow();
        while(true)
        {
            bool more=false;Exception failure=null;
            try {more=flow.MoveNext();}catch(Exception error){failure=error;}
            if(failure!=null){Finish(false,failure.ToString());yield break;}
            if(!more){Finish(true,null);yield break;}
            yield return flow.Current;
        }
    }
    void Check(bool value,string label) {if(!value)throw new Exception(label);checks.Add(label);Debug.Log("[CAMERA CHECK] "+label);}
    static void Deadline(double end,string label) {if(Time.realtimeSinceStartupAsDouble>end)throw new Exception("Timed out: "+label);}
    static Button ButtonNamed(string name)
    {
        foreach(var b in FindObjectsByType<Button>(FindObjectsInactive.Include,FindObjectsSortMode.None))if(b.name==name)return b;
        return null;
    }
    static bool Alive(int id)
    {
        try {using(var p=Process.GetProcessById(id))return !p.HasExited;}
        catch(ArgumentException){return false;}catch(InvalidOperationException){return false;}
    }
    IEnumerator Flow()
    {
        string savedRun=PlayerPrefs.GetString("VisionStriker.Run", "");
        var pipeline=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        float originalRenderScale=pipeline!=null?pipeline.renderScale:1;
        double end=Time.realtimeSinceStartupAsDouble+30;
        while(ButtonNamed("PvpButton")==null||RedboolCameraRuntime.Instance==null)
        {Deadline(end,"menu and integrated runtime");yield return null;}
        var runtime=RedboolCameraRuntime.Instance;
        Check(runtime.Synthetic&&RedboolCameraRuntime.Packaged,"Bundled runtime explicitly synthetic");
        Check(!runtime.Running,"Menu does not open camera child");
        var menu=FindAnyObjectByType<StartSceneUI>();var lobby=menu.transform.Find("StartCanvas/TwoScreenMenu");
        Check(lobby!=null&&!lobby.gameObject.activeSelf,"Lobby hidden by default");
        ButtonNamed("PvpButton").onClick.Invoke();yield return null;
        Check(lobby.gameObject.activeSelf&&!RedboolPvp.Active,"PvP menu opens without pairing");
        yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(directory,"01_pvp_lobby.png"));
        yield return new WaitForSecondsRealtime(.3f);
        ButtonNamed("Host").onClick.Invoke();
        end=Time.realtimeSinceStartupAsDouble+60;
        while(!runtime.Fresh||runtime.Status==null||runtime.Status.phase!="test"||!runtime.PreviewFresh)
        {Deadline(end,"host synthetic status and preview");yield return null;}
        Check(RedboolPvp.Active&&!RedboolPvp.IsClient&&RedboolPvp.RequireView,"HostDual starts same-arena two-screen mode");
        Check(SceneManager.GetActiveScene().name=="UndergroundPit","PvP opens the new dedicated arena");
        Check(runtime.Status.synthetic&&!runtime.Status.camera_connected,"Synthetic backend reports physical camera closed");
        int first=runtime.ChildId;children.Add(first);Check(first>0,"Host owns one child PID");
        Check((string)typeof(RedboolCameraRuntime).GetField("role",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(runtime)=="P1","Host camera uses P1 role");
        long sequence=runtime.Status.preview_sequence;
        int beforeControls=runtime.Status.control_count;
        runtime.Calibrate();end=Time.realtimeSinceStartupAsDouble+15;
        while(runtime.Status==null||runtime.Status.last_command!="calibrate"||runtime.Status.control_count<=beforeControls)
        {Deadline(end,"calibration control round trip");yield return null;}
        Check(runtime.Status.last_command=="calibrate"&&runtime.Status.control_count==beforeControls+1,"Local calibration command received once");
        end=Time.realtimeSinceStartupAsDouble+10;
        while(runtime.Status.preview_sequence<=sequence)
        {Deadline(end,"continuing local preview");yield return null;}
        Check(runtime.PreviewFresh,"Preview continues after calibration request");
        // Exercise the real localhost JPEG path and game scheduling. This is
        // synthetic visual input; it does not claim camera/recognition accuracy.
        end=Time.realtimeSinceStartupAsDouble+20;
        while(runtime.CurrentPreviewTargetFps!=15) {Deadline(end,"healthy preview rate");yield return null;}
        yield return new WaitForSecondsRealtime(1);
        long decodedAt=runtime.PreviewDecodedFrames;
        double measuredAt=Time.realtimeSinceStartupAsDouble;
        yield return new WaitForSecondsRealtime(5);
        measuredPreviewFps=(runtime.PreviewDecodedFrames-decodedAt)/(Time.realtimeSinceStartupAsDouble-measuredAt);
        Check(measuredPreviewFps>=10&&measuredPreviewFps<=16,"Healthy local preview exceeds old 8fps cap without exceeding 15fps target");
        var budget=FindAnyObjectByType<RedboolCameraRenderBudget>();
        Check(budget!=null&&pipeline!=null,"Standalone camera render budget installed on real URP pipeline");
        budget.Refresh();measuredRenderScale=pipeline.renderScale;
        Check(Mathf.Approximately(pipeline.renderScale,RedboolCameraRenderBudget.ScaleFor(Screen.width,Screen.height,originalRenderScale)),"High-DPI scene rendering respects camera budget");
        Check(Mathf.Approximately(RedboolCameraRenderBudget.ScaleFor(1280,720,1),1)&&Mathf.Approximately(RedboolCameraRenderBudget.ScaleFor(1280,720,.7f),.7f),"Normal resolution and preexisting lower quality remain unchanged");
        float retina=RedboolCameraRenderBudget.ScaleFor(3420,2146,1);
        Check(3420*retina<=1920.1f&&2146*retina<=1200.1f,"Recorded Retina resolution stays within 3D pixel budget");
        pipeline.renderScale=.3f;budget.Refresh();
        Check(Mathf.Approximately(pipeline.renderScale,.3f),"Camera budget respects an external lower render setting");
        pipeline.renderScale=originalRenderScale;budget.Refresh();
        Check(Mathf.Approximately(pipeline.renderScale,RedboolCameraRenderBudget.ScaleFor(Screen.width,Screen.height,originalRenderScale)),"External quality restoration cannot bypass active Retina budget");
        var image=ButtonNamed("FoldCamera");Check(image!=null,"Preview fold control exists");
        image.onClick.Invoke();yield return new WaitForSecondsRealtime(.4f);
        decodedAt=runtime.PreviewDecodedFrames;yield return new WaitForSecondsRealtime(1);
        Check(runtime.PreviewDecodedFrames==decodedAt&&runtime.Fresh,"Folded preview stops image work while control channel remains live");
        image.onClick.Invoke();
        Application.targetFrameRate=30;end=Time.realtimeSinceStartupAsDouble+12;
        while(runtime.CurrentPreviewTargetFps!=8) {Deadline(end,"preview sheds load on slow game");yield return null;}
        Check(runtime.Fresh,"Preview falls back to 8fps during slow game frames without losing control status");
        Application.targetFrameRate=60;end=Time.realtimeSinceStartupAsDouble+20;
        while(runtime.CurrentPreviewTargetFps!=15) {Deadline(end,"preview recovers after healthy windows");yield return null;}
        Check(runtime.Fresh,"Preview restores higher rate after sustained healthy frames");
        yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(directory,"02_pvp_camera.png"));
        yield return new WaitForSecondsRealtime(.3f);
        RedboolPvp.Instance.Leave(true);end=Time.realtimeSinceStartupAsDouble+15;
        while(SceneManager.GetActiveScene().name!=SceneRoutes.Start||runtime.DiagnosticsPending||Alive(first)||ButtonNamed("DuelTestButton")==null)
        {Deadline(end,"return to menu stops owned child");yield return null;}
        Check(!runtime.Running&&!Alive(first),"Returning to menu stops old child PID");
        Check(!runtime.DiagnosticsPending,"Diagnostics export waits until the owned camera finishes");
        budget.Refresh();Check(Mathf.Approximately(pipeline.renderScale,originalRenderScale),"Leaving camera gameplay restores authored render scale");
        ButtonNamed("DuelTestButton").onClick.Invoke();end=Time.realtimeSinceStartupAsDouble+60;
        while(!runtime.Running||runtime.ChildId==first||!runtime.Fresh||runtime.Status==null||runtime.Status.phase!="test"||!runtime.PreviewFresh)
        {Deadline(end,"story duel synthetic status and preview");yield return null;}
        int second=runtime.ChildId;children.Add(second);
        Check(second>0&&second!=first&&!RedboolPvp.Active,"Story duel starts independent owned child");
        Check(SceneManager.GetActiveScene().name=="ScoutPass","Solo duel keeps its story arena");
        Check((string)typeof(RedboolCameraRuntime).GetField("role",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(runtime)=="DEV01","Story camera uses DEV01 role");
        Check(runtime.Synthetic&&runtime.Status.synthetic&&!runtime.Status.camera_connected,"Story path remains synthetic without physical camera");
        end=Time.realtimeSinceStartupAsDouble+10;
        while(!runtime.PreviewFresh) {Deadline(end,"first story preview JPEG");yield return null;}
        Check(runtime.PreviewFresh,"Story preview arrives from the new child");
        // A Windows child must not retain a scene receiver's socket. Keep the
        // same DEV01 child alive while the old scene closes and reopens 5065.
        var beforeReceiver=FindAnyObjectByType<RedboolDemoReceiver>();
        Check(beforeReceiver!=null&&beforeReceiver.isActiveAndEnabled,"Story input receiver opens before scene transition");
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        yield return null;
        end=Time.realtimeSinceStartupAsDouble+15;
        while(FindAnyObjectByType<RedboolBattlePreparation>()==null||!runtime.PreviewFresh)
        {Deadline(end,"scene transition with persistent camera child");yield return null;}
        var afterReceiver=FindAnyObjectByType<RedboolDemoReceiver>();
        Check(afterReceiver!=null&&afterReceiver!=beforeReceiver&&afterReceiver.isActiveAndEnabled&&
            !afterReceiver.Status.StartsWith("SOCKET ERROR"),"New scene reopens input port while camera child stays alive");
        Check(runtime.ChildId==second&&Alive(second),"Scene change retains the same camera process");
        yield return new WaitForSecondsRealtime(.2f);
        yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(directory,"03_story_camera.png"));
        yield return new WaitForSecondsRealtime(.3f);
        var exit=ButtonNamed("DuelExitToMenu");Check(exit!=null&&RedboolDuelTest.Active,"Direct duel exposes menu exit");
        Time.timeScale=.06f;Time.fixedDeltaTime=.0012f;
        exit.onClick.Invoke();end=Time.realtimeSinceStartupAsDouble+15;
        while(SceneManager.GetActiveScene().name!=SceneRoutes.Start||runtime.DiagnosticsPending||Alive(second)||ButtonNamed("DuelTestButton")==null)
        {Deadline(end,"direct duel exit flushes camera diagnostics");yield return null;}
        Check(!RedboolDuelTest.Active&&Mathf.Approximately(Time.timeScale,1)&&Mathf.Approximately(Time.fixedDeltaTime,.02f),"Direct duel exit restores normal game time and clears test mode");
        Check(PlayerPrefs.GetString("VisionStriker.Run", "")==savedRun,"Direct duel exit preserves campaign save");
        Check(!runtime.DiagnosticsPending&&!Alive(second),"Direct duel exit finalizes camera records before export");
        ButtonNamed("DuelTestButton").onClick.Invoke();end=Time.realtimeSinceStartupAsDouble+30;
        while(!runtime.Running||!runtime.Fresh||!runtime.PreviewFresh) {Deadline(end,"direct duel can reopen after exit");yield return null;}
        second=runtime.ChildId;children.Add(second);
        Check(second>0&&runtime.Status.synthetic,"Direct duel reopens synthetic camera after menu exit");
        var stop=(IEnumerator)typeof(RedboolCameraRuntime).GetMethod("StopOwned",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(runtime,null);
        runtime.StartCoroutine(stop);
        Check(runtime.ChildId==0&&typeof(RedboolCameraRuntime).GetField("stoppingChild",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(runtime)!=null,"Shutdown retains detached child while coroutine yields");
        Destroy(runtime.gameObject);yield return null;
        end=Time.realtimeSinceStartupAsDouble+10;
        while(Alive(second)) {Deadline(end,"destroyed runtime stops detached child");yield return null;}
        Check(runtime==null&&!Alive(second),"Destroying runtime during shutdown stops child while game stays alive");
    }
    void Finish(bool passed,string error)
    {
        if(finished)return;finished=true;
        File.WriteAllText(Path.Combine(directory,"result.json"),JsonUtility.ToJson(new Report {passed=passed,error=error,checks=checks,children=children,preview_fps=measuredPreviewFps,render_scale=measuredRenderScale,width=Screen.width,height=Screen.height},true));
        Debug.Log("[CAMERA CHECK] "+(passed?"PASS":"FAIL "+error));
        Application.Quit(passed?0:1);
    }
}
