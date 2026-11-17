using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Net;
using System.Net.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.EventSystems;
using Debug=UnityEngine.Debug;

/// <summary>Explicit development-only packaged lifecycle fixture. Synthetic camera only.</summary>
public sealed class RedboolCameraNativeCheck : MonoBehaviour
{
    [Serializable] sealed class Report {public bool passed;public string error;public List<string> checks;public List<int> children;public double preview_fps;public float render_scale;public int width,height;}
    readonly List<string> checks=new List<string>();
    readonly List<int> children=new List<int>();
    readonly List<UdpClient> portReservations=new List<UdpClient>();
    string directory;bool finished;
    double measuredPreviewFps;
    float measuredRenderScale;
    RunState mapFixtureRun;
    RunData mapOriginalData;
    string mapOriginalMessage;
    bool mapFixtureActive;
    const string CameraPreferenceKey="REDBOOL.Settings.CameraIndex";
    bool cameraPreferenceCaptured,cameraPreferencePresent;
    int cameraPreferenceValue;
    void CaptureCameraPreference()
    {
        if(cameraPreferenceCaptured)return;
        cameraPreferencePresent=PlayerPrefs.HasKey(CameraPreferenceKey);
        cameraPreferenceValue=PlayerPrefs.GetInt(CameraPreferenceKey);
        cameraPreferenceCaptured=true;
    }
    void RestoreCameraPreference()
    {
        if(!cameraPreferenceCaptured)return;
        if(cameraPreferencePresent)PlayerPrefs.SetInt(CameraPreferenceKey,cameraPreferenceValue);
        else PlayerPrefs.DeleteKey(CameraPreferenceKey);
        PlayerPrefs.Save();cameraPreferenceCaptured=false;
    }
    static bool ChildCameraArgument(RedboolCameraRuntime runtime,int cameraIndex)
    {
        var process=typeof(RedboolCameraRuntime).GetField("child",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(runtime) as Process;
        // Inspect the real ProcessStartInfo, but never print arguments (bridge
        // credentials are present). This fixture is explicitly synthetic only.
        return process!=null&&process.StartInfo.Arguments.Contains("\"--camera\" \""+cameraIndex+"\"");
    }
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
    IEnumerator CheckCameraPresentation(RedboolCameraRuntime runtime)
    {
        var panel=runtime.GetComponent<RedboolCameraPanel>();
        Check(panel!=null,"Camera presentation panel exists on the owned runtime");
        var refresh=typeof(RedboolCameraPanel).GetMethod("RefreshDisplay",BindingFlags.NonPublic|BindingFlags.Instance);
        var status=typeof(RedboolCameraRuntime).GetProperty("Status");
        var saved=runtime.Status;bool savedOperator=RedboolDemoSettings.OperatorMode;bool savedPanelEnabled=panel.enabled;
        // Freeze only automatic display refresh during UI-state injection, so
        // synthetic backend status cannot change the screenshot between frames.
        panel.enabled=false;
        var fixture=JsonUtility.FromJson<RedboolCameraRuntime.CameraStatus>("{\"schema\":1,\"synthetic\":true,\"phase\":\"test\",\"calibrated\":false,\"tracking\":\"VALID\"}");
        int child=runtime.ChildId;
        try
        {
            status.SetValue(runtime,fixture);RedboolDemoSettings.OperatorMode=false;refresh.Invoke(panel,null);
            Check(!panel.SmallPreview&&!panel.DetailedGuidance,"Before calibration story keeps the full setup panel");
            fixture.calibrated=true;refresh.Invoke(panel,null);
            Check(panel.SmallPreview&&!panel.DetailedGuidance&&runtime.PreviewVisible,"Calibration automatically shrinks preview without pausing its images");
            var frame=GameObject.Find("CameraFrame").GetComponent<RectTransform>();
            Check(frame.rect.width==240&&frame.rect.height==257,"Normal combat preview occupies the compact 240 by 257 layout");
            Check(!ButtonNamed("CameraCalibrate").transform.Find("MenuMark").gameObject.activeSelf,"Compact controls hide oversized decorative marks");
            Check(string.IsNullOrEmpty(panel.DisplayedFeedbackText),"Normal ready combat does not display routine action or recovery text");
            Text shortText=null;
            foreach(var label in panel.GetComponentsInChildren<Text>(true))if(label.name=="CombatInputFeedback")shortText=label;
            foreach(var code in new[]{RedboolCombatFeedbackCode.Interrupted,RedboolCombatFeedbackCode.NotCharged})
            {
                shortText.text=new RedboolCombatPresentation().Present(code,false,0);
                Check(shortText.preferredHeight<=24,"Compact combat notice fits one line: "+code);
            }
            refresh.Invoke(panel,null);

            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(directory,"04_calibrated_compact.png"));
            yield return new WaitForEndOfFrame();
            status.SetValue(runtime,fixture);ButtonNamed("ResizeCamera").onClick.Invoke();refresh.Invoke(panel,null);
            Check(!panel.SmallPreview&&!panel.DetailedGuidance&&runtime.PreviewVisible,"Manual large preview keeps combat notices minimal");
            refresh.Invoke(panel,null);Check(!panel.SmallPreview,"User enlargement persists through a status refresh");
            ButtonNamed("ResizeCamera").onClick.Invoke();refresh.Invoke(panel,null);
            Check(panel.SmallPreview,"Manual small preview restores the compact layout");
            fixture.error="fixture connection error";refresh.Invoke(panel,null);
            var cameraHint=GameObject.Find("CameraHint").GetComponent<Text>();
            Check(cameraHint.gameObject.activeInHierarchy&&cameraHint.text.Contains("연결")&&cameraHint.preferredHeight<=44,"Compact connection problem remains readable in two lines");
            refresh.Invoke(panel,null);
            Check(cameraHint.gameObject.activeInHierarchy,"Connection guidance persists instead of expiring like a combat notice");
            fixture.error=null;
            fixture.camera_slow=true;fixture.framing=new RedboolCameraRuntime.Framing{code="OUTSIDE",outside_edges=new[]{"BOTTOM"}};
            cameraHint.text=RedboolCameraPanel.CameraInputGuidance(fixture,true);
            Check(cameraHint.text.Contains("뒤로")&&cameraHint.text.Contains("팔꿈치")&&cameraHint.preferredHeight<=44,"Bottom-framing guidance outranks slow-capture warning and fits compact two-line panel");
            fixture.framing=new RedboolCameraRuntime.Framing{code="OUTSIDE",outside_edges=new[]{"LEFT"}};
            Check(RedboolCameraPanel.CameraInputGuidance(fixture,true).Contains("양팔"),"Side-framing warning does not incorrectly ask users to raise their fists");
            fixture.framing=null;cameraHint.text=RedboolCameraPanel.CameraInputGuidance(fixture,true);
            Check(cameraHint.text.Contains("다른 앱")&&cameraHint.preferredHeight<=44,"Slow capture gives bounded advice without claiming lighting is the cause");
            fixture.camera_slow=false;
            Check(RedboolCameraPanel.CameraInputGuidance(fixture,true)==null,"Healthy capture has no extra persistent warning");
            refresh.Invoke(panel,null);

            ButtonNamed("FoldCamera").onClick.Invoke();refresh.Invoke(panel,null);
            Check(!runtime.PreviewVisible&&frame.rect.height==98,"Explicit fold hides image work while retaining necessary controls");
            fixture.calibrated=false;refresh.Invoke(panel,null);
            Check(!panel.SmallPreview&&runtime.PreviewVisible,"New calibration expands and unfolds the camera guidance");
            fixture.calibrated=true;refresh.Invoke(panel,null);
            Check(panel.SmallPreview,"Completing recalibration automatically returns to compact combat");
            RedboolDemoSettings.OperatorMode=true;refresh.Invoke(panel,null);
            Check(panel.DetailedGuidance&&!panel.SmallPreview,"Operator mode retains the full detailed panel");
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(directory,"05_operator_expanded.png"));
            yield return new WaitForEndOfFrame();
            Check(runtime.ChildId==child,"Changing camera presentation never replaces the recognition child");
        }
        finally
        {status.SetValue(runtime,saved);RedboolDemoSettings.OperatorMode=savedOperator;panel.enabled=savedPanelEnabled;refresh.Invoke(panel,null);}
        Check(runtime.Synthetic&&!runtime.Status.camera_connected,"Presentation fixture keeps the physical camera closed");
    }
    static Rect ScreenRect(RectTransform item)
    {
        var corners=new Vector3[4];item.GetWorldCorners(corners);
        return Rect.MinMaxRect(corners[0].x,corners[0].y,corners[2].x,corners[2].y);
    }
    IEnumerator CheckPvpCameraPresentation(RedboolCameraRuntime runtime)
    {
        var game=RedboolPvp.Instance;var lobby=game.Lobby;
        Check(lobby!=null,"Native PvP includes the merged character-selection lobby");
        var panel=runtime.GetComponent<RedboolCameraPanel>();
        var refresh=typeof(RedboolCameraPanel).GetMethod("RefreshDisplay",BindingFlags.NonPublic|BindingFlags.Instance);
        var lobbyRefresh=typeof(RedboolPvpLobby).GetMethod("Refresh",BindingFlags.NonPublic|BindingFlags.Instance);
        var stage=typeof(RedboolPvpLobby).GetProperty("Current");
        var status=typeof(RedboolCameraRuntime).GetProperty("Status");
        var savedStatus=runtime.Status;var savedStage=lobby.Current;
        bool savedGame=game.enabled,savedLobby=lobby.enabled,savedPanel=panel.enabled;
        bool savedOperator=RedboolDemoSettings.OperatorMode;
        int child=runtime.ChildId;
        var fixture=JsonUtility.FromJson<RedboolCameraRuntime.CameraStatus>("{\"schema\":1,\"synthetic\":true,\"phase\":\"test\",\"calibrated\":true,\"tracking\":\"VALID\"}");
        game.enabled=false;lobby.enabled=false;panel.enabled=false;
        try
        {
            RedboolDemoSettings.OperatorMode=false;status.SetValue(runtime,fixture);
            stage.SetValue(lobby,RedboolPvpLobby.Stage.Room);refresh.Invoke(panel,null);
            Check(panel.SmallPreview&&!panel.DetailedGuidance,"Calibrated waiting room does not masquerade as motion practice");
            stage.SetValue(lobby,RedboolPvpLobby.Stage.Select);lobbyRefresh.Invoke(lobby,new object[]{null});refresh.Invoke(panel,null);
            Canvas.ForceUpdateCanvases();
            var frame=GameObject.Find("CameraFrame").GetComponent<RectTransform>();
            var selection=GameObject.Find("Select").transform;
            for(int i=0;i<RedboolPvpRoster.Count;i++)
                Check(!ScreenRect(frame).Overlaps(ScreenRect(selection.Find("Card"+i).GetComponent<RectTransform>())),"Own preview leaves character card visible: "+i);
            foreach(string name in new[]{"Name","Title","Line","Special","SpecialLine","State"})
                Check(!ScreenRect(frame).Overlaps(ScreenRect(selection.Find("Pick1/"+name).GetComponent<RectTransform>())),"Own preview leaves selected-character detail visible: "+name);
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(directory,"02_v36_character_camera.png"));
            yield return new WaitForEndOfFrame();
            status.SetValue(runtime,fixture);stage.SetValue(lobby,RedboolPvpLobby.Stage.Done);lobbyRefresh.Invoke(lobby,new object[]{null});refresh.Invoke(panel,null);
            Check(panel.SmallPreview&&!panel.DetailedGuidance,"PvP uses compact camera alongside dedicated detailed tutorial cards");
            Canvas.ForceUpdateCanvases();
            foreach(string name in new[]{"TutorialCard1","TutorialCard2"})
            {
                var card=GameObject.Find(name).GetComponent<RectTransform>();
                Check(card.gameObject.activeInHierarchy&&!ScreenRect(frame).Overlaps(ScreenRect(card)),"Camera does not cover detailed tutorial card: "+name);
            }
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(directory,"03_v36_tutorial_camera.png"));
            yield return new WaitForEndOfFrame();
            status.SetValue(runtime,fixture);fixture.calibrated=false;refresh.Invoke(panel,null);Canvas.ForceUpdateCanvases();
            Check(!panel.SmallPreview,"Recalibration expands its controls even during PvP tutorial");
            foreach(string name in new[]{"TutorialCard1","TutorialCard2"})
                Check(!ScreenRect(frame).Overlaps(ScreenRect(GameObject.Find(name).GetComponent<RectTransform>())),"Expanded recalibration leaves tutorial text visible: "+name);
            Check(runtime.ChildId==child,"Lobby and tutorial presentation preserve the existing camera child");
        }
        finally
        {
            stage.SetValue(lobby,savedStage);status.SetValue(runtime,savedStatus);
            RedboolDemoSettings.OperatorMode=savedOperator;
            lobbyRefresh.Invoke(lobby,new object[]{null});
            game.enabled=savedGame;lobby.enabled=savedLobby;panel.enabled=savedPanel;refresh.Invoke(panel,null);
        }
    }
    UdpClient ReserveInputPort(int port)
    {
        var reservation=new UdpClient(AddressFamily.InterNetwork);
        try
        {
            reservation.Client.ExclusiveAddressUse=true;
            reservation.Client.Bind(new IPEndPoint(IPAddress.Any,port));
            RedboolSocketOwnership.PreventInheritance(reservation.Client,"synthetic fixture occupied port "+port);
            portReservations.Add(reservation);return reservation;
        }
        catch {reservation.Dispose();throw;}
    }
    void ReleaseInputPort(UdpClient reservation)
    {
        reservation.Dispose();portReservations.Remove(reservation);
    }
    static RedboolDemoReceiver ReceiverOnPort(int port)
    {
        foreach(var receiver in FindObjectsByType<RedboolDemoReceiver>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            if(receiver.port==port&&receiver.gameObject.activeInHierarchy&&receiver.gameObject.scene==SceneManager.GetActiveScene())return receiver;
        return null;
    }
    static bool Alive(int id)
    {
        try {using(var p=Process.GetProcessById(id))return !p.HasExited;}
        catch(ArgumentException){return false;}catch(InvalidOperationException){return false;}
    }
    static bool ModalBlocksScreenPoint(Vector2 point,bool expected)
    {
        var hits=new List<RaycastResult>();
        if(EventSystem.current!=null)
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=point},hits);
        bool blocked=hits.Count>0&&hits[0].gameObject.GetComponentInParent<Canvas>()?.name=="GameMenuCanvas";
        if(blocked!=expected)
        {
            Debug.Log($"[CAMERA CHECK RAYCAST] expected={expected} actual={blocked} point={point} screen={Screen.width}x{Screen.height} event_system={EventSystem.current} hits={hits.Count}");
            foreach(var hit in hits)
                Debug.Log($"[CAMERA CHECK RAYCAST HIT] object={hit.gameObject.name} depth={hit.depth} sorting={hit.sortingLayer}/{hit.sortingOrder} distance={hit.distance} module={hit.module}");
            foreach(var menuCanvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            {
                if(menuCanvas.name!="GameMenuCanvas")continue;
                var caster=menuCanvas.GetComponent<GraphicRaycaster>();
                Debug.Log($"[CAMERA CHECK RAYCAST CANVAS] enabled={menuCanvas.enabled} active={menuCanvas.gameObject.activeInHierarchy} mode={menuCanvas.renderMode} order={menuCanvas.sortingOrder} scale={menuCanvas.scaleFactor} raycaster={caster?.enabled}");
                foreach(var graphic in menuCanvas.GetComponentsInChildren<Graphic>(true))
                    Debug.Log($"[CAMERA CHECK RAYCAST GRAPHIC] object={graphic.name} enabled={graphic.enabled} active={graphic.gameObject.activeInHierarchy} target={graphic.raycastTarget} depth={graphic.depth} cull={graphic.canvasRenderer.cull} rect={graphic.rectTransform.rect} position={graphic.rectTransform.position}");
            }
        }
        return blocked;
    }
    void InstallUnsavedMapFixture()
    {
        mapFixtureRun=RunState.Instance;
        if(mapFixtureRun==null||mapFixtureRun.Config==null)throw new Exception("Map fixture needs RunState config");
        mapOriginalData=mapFixtureRun.Data;mapOriginalMessage=mapFixtureRun.LastMessage;
        var data=new RunData{seed=20261115,storyPresentationVersion=1,tutorialVersion=1,tutorialCompleted=true};
        data.map=MapGenerator.Generate(mapFixtureRun.Config.map,mapFixtureRun.Config,data.seed);
        data.currentNodeId=data.map.storyVersion>0?"cave":data.map.nodes[0].id;
        data.clearedNodeIds.Add(data.currentNodeId);data.discoveredNodeIds.Add(data.currentNodeId);
        // Prevent automatic story presentation/autosave; do not call StartNewRun,
        // LoadRun or Save, and never write a fixture into the user's PlayerPrefs.
        foreach(var node in data.map.nodes){data.seenStoryIds.Add(node.id+"_visit");data.seenStoryIds.Add(node.id+"_after");}
        data.seenStoryIds.Add("cave_prologue");
        mapFixtureActive=true;
        typeof(RunState).GetProperty("Data").SetValue(mapFixtureRun,data);
    }
    void RestoreMapFixture()
    {
        if(!mapFixtureActive)return;
        if(mapFixtureRun!=null)
        {
            typeof(RunState).GetProperty("Data").SetValue(mapFixtureRun,mapOriginalData);
            mapFixtureRun.LastMessage=mapOriginalMessage;
        }
        mapFixtureActive=false;
    }
    IEnumerator Flow()
    {
        string savedRun=PlayerPrefs.GetString("VisionStriker.Run", "");
        bool savedRunPresent=PlayerPrefs.HasKey("VisionStriker.Run");
        var pipeline=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        float originalRenderScale=pipeline!=null?pipeline.renderScale:1;
        double end=Time.realtimeSinceStartupAsDouble+30;
        while(ButtonNamed("PvpButton")==null||RedboolCameraRuntime.Instance==null)
        {Deadline(end,"menu and integrated runtime");yield return null;}
        var runtime=RedboolCameraRuntime.Instance;
        Check(runtime.Synthetic&&RedboolCameraRuntime.Packaged,"Bundled runtime explicitly synthetic");
        Check(!runtime.Running,"Menu does not open camera child");
        Check(!RedboolDemoSettings.OperatorMode,"Normal launch has no operator shortcuts");
        Check(!Debug.developerConsoleEnabled,"Normal play uses game guidance instead of a developer console popup");
        RedboolSettingsPanel.Show();yield return new WaitForEndOfFrame();
        Check(RedboolSettingsPanel.IsOpen&&RedboolPauseMenu.BlocksStoryInput&&!runtime.Running,"Settings modal blocks story input without opening a camera");
        ScreenCapture.CaptureScreenshot(Path.Combine(directory,"00_settings.png"));yield return new WaitForEndOfFrame();
        ButtonNamed("CloseSettings").onClick.Invoke();
        Check(!RedboolSettingsPanel.IsOpen&&RedboolPauseMenu.BlocksStoryInput,"Settings close button consumes its closing-frame story input");
        yield return null;
        Check(!RedboolPauseMenu.BlocksStoryInput&&!runtime.Running,"Settings close releases input next frame without camera work");
        int confirmed=0;RedboolNewGameConfirmation.Show(()=>confirmed++);yield return null;
        ButtonNamed("CancelNewGame").onClick.Invoke();
        Check(!RedboolNewGameConfirmation.IsOpen&&RedboolPauseMenu.BlocksStoryInput&&confirmed==0,"New-game cancel consumes closing input without invoking confirmation");
        yield return null;
        Check(!RedboolPauseMenu.BlocksStoryInput,"New-game cancel releases story input next frame");
        RedboolNewGameConfirmation.Show(()=>confirmed++);yield return null;
        ButtonNamed("ConfirmNewGame").onClick.Invoke();
        Check(!RedboolNewGameConfirmation.IsOpen&&RedboolPauseMenu.BlocksStoryInput&&confirmed==1,"New-game confirmation consumes closing input before the harmless fixture callback");
        yield return null;
        Check(!RedboolPauseMenu.BlocksStoryInput,"New-game confirmation releases story input next frame");
        float menuTime=Time.timeScale,menuStep=Time.fixedDeltaTime;
        RedboolPauseMenu.Open();
        // Canvas depth/raycast registration is populated on its first displayed
        // frame; the behavior under test starts after that frame is available.
        yield return new WaitForEndOfFrame();Canvas.ForceUpdateCanvases();
        Check(RedboolPauseMenu.IsOpen&&ButtonNamed("GameMenuContinue")!=null&&ButtonNamed("GameMenuReturn")!=null,"Universal game menu opens with Continue and Return choices");
        Check(!runtime.Running&&!runtime.DiagnosticsPending,"Opening main-menu overlay does not start a camera child");
        Check(ModalBlocksScreenPoint(new Vector2(12,12),true),"Fullscreen modal prevents pointer clicks reaching the underlying scene");
        ScreenCapture.CaptureScreenshot(Path.Combine(directory,"00_menu_game_menu.png"));
        yield return new WaitForEndOfFrame();
        ButtonNamed("GameMenuContinue").onClick.Invoke();
        Check(!RedboolPauseMenu.IsOpen&&RedboolPauseMenu.BlocksStoryInput,"Continue closes overlay and consumes its closing-frame story input");
        Check(!ModalBlocksScreenPoint(new Vector2(12,12),false),"Closed menu no longer blocks pointer input to the scene");
        Check(Mathf.Approximately(Time.timeScale,menuTime)&&Mathf.Approximately(Time.fixedDeltaTime,menuStep),"Opening and continuing menu never takes ownership of simulation time");
        yield return null;
        Check(!RedboolPauseMenu.BlocksStoryInput&&!runtime.Running,"Story keyboard input resumes next frame without opening a camera");
        InstallUnsavedMapFixture();RedboolPauseMenu.Open();SceneRoutes.LoadMap();yield return null;
        end=Time.realtimeSinceStartupAsDouble+30;
        while(FindAnyObjectByType<MapSceneUI>()==null||ButtonNamed("MenuButton")==null)
        {Deadline(end,"unsaved map menu fixture");yield return null;}
        Check(SceneManager.GetActiveScene().name==SceneRoutes.Map&&!StoryCutscene.IsOpen&&!runtime.Running,"Real map scene opens from in-memory fixture without story or camera side effects");
        Check(RedboolPauseMenu.IsOpen&&EventSystem.current!=null&&EventSystem.current.currentSelectedGameObject==ButtonNamed("GameMenuContinue").gameObject,"Scene transition under an open overlay retains Continue keyboard focus");
        Check(ButtonNamed("GameMenuReturn").interactable,"Scene transition refreshes main-menu return availability");
        ButtonNamed("GameMenuContinue").onClick.Invoke();yield return null;
        ButtonNamed("MenuButton").onClick.Invoke();
        Check(RedboolPauseMenu.IsOpen&&SceneManager.GetActiveScene().name==SceneRoutes.Map,"Map menu button opens the same overlay without leaving the map");
        yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(directory,"00_map_game_menu.png"));
        ButtonNamed("GameMenuContinue").onClick.Invoke();
        Check(!RedboolPauseMenu.IsOpen&&SceneManager.GetActiveScene().name==SceneRoutes.Map,"Continue returns to the current map");
        RedboolPauseMenu.Open();ButtonNamed("GameMenuReturn").onClick.Invoke();yield return null;
        end=Time.realtimeSinceStartupAsDouble+15;
        while(SceneManager.GetActiveScene().name!=SceneRoutes.Start||ButtonNamed("PvpButton")==null)
        {Deadline(end,"map return to main menu");yield return null;}
        RestoreMapFixture();
        Check(!RedboolPauseMenu.IsOpen&&!runtime.Running&&PlayerPrefs.GetString("VisionStriker.Run","")==savedRun&&PlayerPrefs.HasKey("VisionStriker.Run")==savedRunPresent,"Map return preserves campaign save bytes and presence while leaving the camera closed");
        var menu=FindAnyObjectByType<StartSceneUI>();var lobby=menu.transform.Find("StartCanvas/TwoScreenMenu");
        Check(lobby!=null&&!lobby.gameObject.activeSelf,"Lobby hidden by default");
        ButtonNamed("PvpButton").onClick.Invoke();yield return null;
        Check(lobby.gameObject.activeSelf&&!RedboolPvp.Active,"PvP menu opens without pairing");
        yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(directory,"01_pvp_lobby.png"));
        yield return new WaitForSecondsRealtime(.3f);
        var occupiedP1=ReserveInputPort(5065);var occupiedP2=ReserveInputPort(5066);
        Debug.Log("[CAMERA CHECK EXPECTED PORT CONFLICT] occupying 5065 and 5066 before host start");
        ButtonNamed("Host").onClick.Invoke();
        end=Time.realtimeSinceStartupAsDouble+15;
        while(!runtime.InputConnectionBlocked||ReceiverOnPort(5065)==null||ReceiverOnPort(5066)==null)
        {Deadline(end,"occupied local ports withhold camera");yield return null;}
        Check(!runtime.Running&&!runtime.DiagnosticsPending&&ReceiverOnPort(5065).PortConflict&&ReceiverOnPort(5066).PortConflict,"Occupied host input ports do not start a camera child or send camera inputs elsewhere");
        Check(RedboolCameraPanel.Explain(runtime).Contains("다른 게임")&&!string.IsNullOrEmpty(runtime.LastInputConnectionError),"Port conflict overrides ready/synthetic camera guidance and retains a menu notice");
        ReleaseInputPort(occupiedP1);end=Time.realtimeSinceStartupAsDouble+5;
        while(!ReceiverOnPort(5065).Bound){Deadline(end,"first host input port automatic retry");yield return null;}
        Check(runtime.InputConnectionBlocked&&!runtime.Running&&ReceiverOnPort(5066).PortConflict&&occupiedP2.Client.IsBound,"Host requires both own input ports and leaves the other port owner untouched");
        ReleaseInputPort(occupiedP2);
        end=Time.realtimeSinceStartupAsDouble+60;
        while(!runtime.Fresh||runtime.Status==null||runtime.Status.phase!="test"||!runtime.PreviewFresh)
        {Deadline(end,"host synthetic status and preview");yield return null;}
        Check(RedboolPvp.Active&&!RedboolPvp.IsClient&&RedboolPvp.RequireView,"HostDual starts same-arena two-screen mode");
        Check(SceneManager.GetActiveScene().name=="UndergroundPit","PvP opens the new dedicated arena");
        Check(runtime.Status.synthetic&&!runtime.Status.camera_connected,"Synthetic backend reports physical camera closed");
        Check(ReceiverOnPort(5065).Bound&&ReceiverOnPort(5066).Bound&&!runtime.InputConnectionBlocked&&runtime.InputConnectionError==null&&runtime.LastInputConnectionError==null,"Both ports recover automatically and clear the conflict without restarting the game");
        Check(!Debug.developerConsoleVisible,"Expected port-conflict logs do not cover the game with a console");
        var pvpPresentation=CheckPvpCameraPresentation(runtime);
        while(pvpPresentation.MoveNext())yield return pvpPresentation.Current;

        Text feedbackText=null;
        foreach(var label in FindObjectsByType<Text>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            if(label.name=="CombatInputFeedback")feedbackText=label;
        Check(feedbackText!=null,"Combat feedback has a visible dedicated text area");
        string originalFeedback=feedbackText.text;
        foreach(RedboolCombatFeedbackCode code in Enum.GetValues(typeof(RedboolCombatFeedbackCode)))
        {
            feedbackText.text=RedboolCombatFeedback.Text(code);
            Canvas.ForceUpdateCanvases();
            Check(feedbackText.preferredHeight<=feedbackText.rectTransform.rect.height+1,"Combat message fits without vertical clipping: "+code);
        }
        feedbackText.text=originalFeedback;
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
        // Inject only an already-parsed local status for this synchronous fixture.
        // No backend mode, camera, process or input protocol is changed.
        var savedStatus=runtime.Status;bool savedSynthetic=runtime.Synthetic;
        int savedPreviewTarget=runtime.CurrentPreviewTargetFps;
        var pressured=JsonUtility.FromJson<RedboolCameraRuntime.CameraStatus>("{\"schema\":1,\"phase\":\"running\",\"camera_connected\":true,\"calibrated\":true,\"tracking\":\"VALID\",\"inference_mode\":\"lite_overload\",\"inference_warning\":\"AUXILIARY_DISABLED_FOR_LATENCY\"}");
        int displayWidth=Screen.width,displayHeight=Screen.height;
        try
        {
            typeof(RedboolCameraRuntime).GetProperty("Status").SetValue(runtime,pressured);
            typeof(RedboolCameraRuntime).GetProperty("Synthetic").SetValue(runtime,false);
            Check(runtime.InferencePressure&&runtime.Status.inference_mode=="lite_overload","Backend inference warning and mode survive local status parsing");
            float lowBudget=RedboolCameraRenderBudget.ScaleFor(2880,1800,1,true);
            Check(2880*lowBudget<=1280.1f&&1800*lowBudget<=720.1f&&Mathf.Approximately(RedboolCameraRenderBudget.ScaleFor(1280,720,.7f,true),.7f),"Latency budget caps 3D pixels at 720p and preserves lower settings");
            budget.Refresh();
            Check(Mathf.Approximately(pipeline.renderScale,RedboolCameraRenderBudget.ScaleFor(Screen.width,Screen.height,originalRenderScale,true))&&Screen.width==displayWidth&&Screen.height==displayHeight,"Actual inference warning lowers only scene rendering without resizing the window");
            pipeline.renderScale=.2f;budget.Refresh();
            Check(Mathf.Approximately(pipeline.renderScale,.2f),"Latency budget preserves an external lower render setting");
            pipeline.renderScale=originalRenderScale;budget.Refresh();
            typeof(RedboolCameraRuntime).GetMethod("ApplyInferencePressure",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(runtime,null);
            Check(runtime.CurrentPreviewTargetFps==8,"Latched inference pressure immediately limits preview work to 8fps");
            Check(RedboolCameraPanel.Explain(runtime).Contains("속도 우선"),"Valid tracking exposes speed-priority mode without an accuracy claim");
            pressured.calibrated=false;pressured.calibration_reason="STALE_FRAME";
            pressured.framing=new RedboolCameraRuntime.Framing{code="OUTSIDE",outside_edges=new[]{"BOTTOM"}};
            Check(RedboolCameraPanel.Explain(runtime).Contains("처리 지연")&&!RedboolCameraPanel.Explain(runtime).Contains("손을 들"),"Stale frames show processing delay rather than an incorrect pose instruction");
            pressured.framing=null;pressured.calibration_reason="JOINT_HISTORY";
            Check(RedboolCameraPanel.Explain(runtime).Contains("자세를 안정적으로")&&!RedboolCameraPanel.Explain(runtime).Contains("처리 지연"),"Joint-history guidance stays distinct from processing delay");
            pressured.phase="optimizing_camera";pressured.camera_connected=false;
            Check(RedboolCameraPanel.Explain(runtime).Contains("카메라 설정"),"Camera profile selection is explained before camera-connected status");
            pressured.phase="running";pressured.camera_connected=true;pressured.camera_slow=true;pressured.acquisition_fps=9.7f;pressured.camera_profile="DSHOW/YUY2";
            Check(RedboolCameraPanel.Explain(runtime).Contains("카메라 영상")&&!RedboolCameraPanel.Explain(runtime).Contains("처리 지연"),"Slow camera acquisition is distinguished from model-processing delay");
            var unknownRate=JsonUtility.FromJson<RedboolCameraRuntime.CameraStatus>("{\"schema\":1,\"acquisition_fps\":null}");
            Check(unknownRate.schema==1&&unknownRate.acquisition_fps==0,"Unknown acquisition cadence remains readable before camera profiling completes");
        }
        finally
        {
            typeof(RedboolCameraRuntime).GetProperty("Status").SetValue(runtime,savedStatus);
            typeof(RedboolCameraRuntime).GetProperty("Synthetic").SetValue(runtime,savedSynthetic);
            typeof(RedboolCameraRuntime).GetProperty("CurrentPreviewTargetFps").SetValue(runtime,savedPreviewTarget);
            pipeline.renderScale=originalRenderScale;budget.Refresh();
        }
        Check(runtime.Synthetic&&!runtime.InferencePressure&&Mathf.Approximately(pipeline.renderScale,RedboolCameraRenderBudget.ScaleFor(Screen.width,Screen.height,originalRenderScale)),"Removing fixture pressure restores normal budget and keeps physical camera closed");
        var pressurePolicy=(RedboolRenderPressure)typeof(RedboolCameraRuntime).GetField("renderPressure",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(runtime);
        int cameraGeneration=(int)typeof(RedboolCameraRuntime).GetField("generation",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(runtime);
        Check(!runtime.JointRenderPressure,"Synthetic healthy-camera lifecycle does not trigger joint render pressure");
        pressurePolicy.Observe(cameraGeneration,true,true,5,13,35);
        Check(!runtime.JointRenderPressure,"One poor capture/game window leaves actual packaged rendering unchanged");
        pressurePolicy.Observe(cameraGeneration,true,true,5,13,35);budget.Refresh();
        Check(runtime.JointRenderPressure&&Mathf.Approximately(pipeline.renderScale,RedboolCameraRenderBudget.ScaleFor(Screen.width,Screen.height,originalRenderScale,true)),"Two joint-pressure windows apply the 720p budget through the existing renderer owner");
        Check(runtime.ChildId==first&&runtime.Status==savedStatus&&Screen.width==displayWidth&&Screen.height==displayHeight,"Joint render fallback leaves recognition status, owned child and UI resolution untouched");
        yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(directory,"02_pvp_camera.png"));
        yield return new WaitForSecondsRealtime(.3f);
        float pvpTime=Time.timeScale,pvpStep=Time.fixedDeltaTime;
        RedboolPauseMenu.Open();
        Check(RedboolPauseMenu.IsOpen&&RedboolPvp.Active&&runtime.ChildId==first&&Mathf.Approximately(Time.timeScale,pvpTime)&&Mathf.Approximately(Time.fixedDeltaTime,pvpStep),"PvP game menu leaves match time and owned camera unchanged until confirmation");
        ButtonNamed("GameMenuReturn").onClick.Invoke();end=Time.realtimeSinceStartupAsDouble+15;
        while(SceneManager.GetActiveScene().name!=SceneRoutes.Start||runtime.DiagnosticsPending||Alive(first)||ButtonNamed("NewGameButton")==null)
        {Deadline(end,"return to menu stops owned child");yield return null;}
        Check(!runtime.Running&&!Alive(first),"Returning to menu stops old child PID");
        Check(!RedboolPauseMenu.IsOpen&&!RedboolPvp.Active,"Common menu confirmation uses the normal PvP disconnect route");
        Check(!runtime.DiagnosticsPending,"Diagnostics export waits until the owned camera finishes");
        budget.Refresh();Check(Mathf.Approximately(pipeline.renderScale,originalRenderScale)&&!runtime.JointRenderPressure,"Leaving camera gameplay restores authored render scale and clears joint pressure");
        RedboolDuelTest.Begin();end=Time.realtimeSinceStartupAsDouble+60;
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
        var presentationCheck=CheckCameraPresentation(runtime);
        while(presentationCheck.MoveNext())yield return presentationCheck.Current;
        CaptureCameraPreference();
        try
        {
            int originalCamera=RedboolDemoSettings.CameraIndex;
            Check(ChildCameraArgument(runtime,originalCamera),"Original story child receives the selected camera argument");
            int selectedCamera=(originalCamera+1)%5;
            RedboolDemoSettings.CameraIndex=selectedCamera;
            yield return new WaitForSecondsRealtime(1);
            Check(runtime.ChildId==second&&Alive(second)&&runtime.Fresh&&ChildCameraArgument(runtime,originalCamera),
                "Selecting another camera leaves the running child and its camera argument unchanged");
            Check(!(bool)typeof(RedboolCameraRuntime).GetField("restartRequested",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(runtime),
                "Camera selection alone does not request a restart");
            int previousCameraChild=second;
            RedboolDemoSettings.RequestCameraReconnect();
            end=Time.realtimeSinceStartupAsDouble+60;
            while(!runtime.Running||runtime.ChildId==previousCameraChild||!runtime.Fresh||!runtime.PreviewFresh
                ||runtime.Status==null||runtime.Status.phase!="test"||Alive(previousCameraChild))
            {Deadline(end,"explicit camera apply starts replacement synthetic child");yield return null;}
            second=runtime.ChildId;children.Add(second);
            Check(second>0&&second!=previousCameraChild&&!Alive(previousCameraChild)&&ChildCameraArgument(runtime,selectedCamera),
                "Explicit apply replaces only the owned child and forwards the actual selected --camera argument");
            Check(runtime.Synthetic&&runtime.Status.synthetic&&!runtime.Status.camera_connected,
                "Changing camera selection in the fixture never opens a physical camera");
        }
        finally {RestoreCameraPreference();}
        Check(PlayerPrefs.HasKey(CameraPreferenceKey)==cameraPreferencePresent
            &&(!cameraPreferencePresent||PlayerPrefs.GetInt(CameraPreferenceKey)==cameraPreferenceValue),
            "Camera preference value and original key presence are restored exactly");
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
        // Simulate a different process taking the port during a real scene
        // transition. Only this fixture's reservation is ever released.
        afterReceiver.enabled=false;var occupiedStory=ReserveInputPort(5065);
        Debug.Log("[CAMERA CHECK EXPECTED PORT CONFLICT] occupying 5065 during active-camera scene transition");
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);yield return null;
        end=Time.realtimeSinceStartupAsDouble+15;
        while(!runtime.InputConnectionBlocked||runtime.DiagnosticsPending||Alive(second))
        {Deadline(end,"scene port conflict stops owned camera and flushes diagnostics");yield return null;}
        Check(!runtime.Running&&ReceiverOnPort(5065)!=null&&ReceiverOnPort(5065).PortConflict&&occupiedStory.Client.IsBound,"A newly blocked scene stops its own child without closing the conflicting port owner");
        Check(!string.IsNullOrEmpty(runtime.LastInputConnectionError)&&RedboolCameraPanel.Explain(runtime).Contains("다른 게임"),"Active-game port conflict remains visible after camera shutdown");
        yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(directory,"03_story_port_conflict.png"));
        yield return new WaitForEndOfFrame();
        ReleaseInputPort(occupiedStory);int stoppedStoryChild=second;
        end=Time.realtimeSinceStartupAsDouble+60;
        while(!runtime.Running||!runtime.Fresh||!runtime.PreviewFresh||runtime.ChildId==stoppedStoryChild)
        {Deadline(end,"story recovers after conflicting port release");yield return null;}
        second=runtime.ChildId;children.Add(second);
        Check(ReceiverOnPort(5065).Bound&&!runtime.InputConnectionBlocked&&!Alive(stoppedStoryChild)&&runtime.Synthetic,"Scene conflict release automatically starts one fresh synthetic child after the old child exits");
        yield return new WaitForSecondsRealtime(.2f);
        yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(directory,"03_story_camera.png"));
        yield return new WaitForSecondsRealtime(.3f);
        var exit=ButtonNamed("DuelExitToMenu");Check(exit!=null&&RedboolDuelTest.Active,"Direct duel exposes the shared game menu");
        Time.timeScale=.06f;Time.fixedDeltaTime=.0012f;
        exit.onClick.Invoke();
        Check(RedboolPauseMenu.IsOpen&&RedboolDuelTest.Active&&SceneManager.GetActiveScene().name=="ScoutPass"&&runtime.ChildId==second,"Direct duel menu opens without immediately leaving combat or stopping camera");
        Check(Mathf.Approximately(Time.timeScale,.06f)&&Mathf.Approximately(Time.fixedDeltaTime,.0012f),"Direct duel menu preserves an existing slow-time owner");
        ButtonNamed("GameMenuContinue").onClick.Invoke();
        Check(!RedboolPauseMenu.IsOpen&&RedboolPauseMenu.BlocksStoryInput&&RedboolDuelTest.Active&&Alive(second),"Direct duel Continue keeps the same fight and camera while consuming closing input");
        Check(Mathf.Approximately(Time.timeScale,.06f)&&Mathf.Approximately(Time.fixedDeltaTime,.0012f),"Continuing does not restore or overwrite a special slow-time value");
        exit.onClick.Invoke();
        Check(RedboolPauseMenu.IsOpen,"Direct duel menu can reopen before return confirmation");
        ButtonNamed("GameMenuReturn").onClick.Invoke();end=Time.realtimeSinceStartupAsDouble+15;
        while(SceneManager.GetActiveScene().name!=SceneRoutes.Start||runtime.DiagnosticsPending||Alive(second)||ButtonNamed("NewGameButton")==null)
        {Deadline(end,"direct duel exit flushes camera diagnostics");yield return null;}
        Check(!RedboolDuelTest.Active&&Mathf.Approximately(Time.timeScale,1)&&Mathf.Approximately(Time.fixedDeltaTime,.02f),"Direct duel exit restores normal game time and clears test mode");
        Check(PlayerPrefs.GetString("VisionStriker.Run", "")==savedRun,"Direct duel exit preserves campaign save");
        Check(!runtime.DiagnosticsPending&&!Alive(second),"Direct duel exit finalizes camera records before export");
        RedboolDuelTest.Begin();end=Time.realtimeSinceStartupAsDouble+30;
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
        RestoreMapFixture();
        RestoreCameraPreference();
        foreach(var reservation in portReservations)reservation.Dispose();portReservations.Clear();
        File.WriteAllText(Path.Combine(directory,"result.json"),JsonUtility.ToJson(new Report {passed=passed,error=error,checks=checks,children=children,preview_fps=measuredPreviewFps,render_scale=measuredRenderScale,width=Screen.width,height=Screen.height},true));
        Debug.Log("[CAMERA CHECK] "+(passed?"PASS":"FAIL "+error));
        Application.Quit(passed?0:1);
    }
}
