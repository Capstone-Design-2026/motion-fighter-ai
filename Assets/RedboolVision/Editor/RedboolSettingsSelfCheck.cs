using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Explicit editor checks; no scene transition, camera or network traffic.</summary>
public static class RedboolSettingsSelfCheck
{
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    const string Prefix = "REDBOOL.Settings.", SaveKey = "VisionStriker.Run";
    static readonly List<string> checks = new List<string>();
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks.Add(message); }

    public static void Run()
    {
        checks.Clear();
        string[] ints = { "CameraIndex", "LowQuality", "Fullscreen" };
        var had = new Dictionary<string, bool>(); var values = new Dictionary<string, int>();
        foreach (string key in ints) { had[key] = PlayerPrefs.HasKey(Prefix + key); values[key] = PlayerPrefs.GetInt(Prefix + key); }
        bool hadVolume = PlayerPrefs.HasKey(Prefix + "Volume"), hadSave = PlayerPrefs.HasKey(SaveKey);
        float volume = PlayerPrefs.GetFloat(Prefix + "Volume"), audio = AudioListener.volume;
        string save = PlayerPrefs.GetString(SaveKey); bool oldOperator = RedboolDemoSettings.OperatorMode;
        // These checks run synchronously in one Editor frame. Closing a real
        // modal must consume that frame, but must not leak the fixture's input
        // suppression into the next independent check in an aggregate run.
        var consumedFrame = typeof(RedboolPauseMenu).GetField("consumedInputFrame", BindingFlags.Static | BindingFlags.NonPublic);
        int originalConsumedFrame = (int)consumedFrame.GetValue(null);
        GameObject runRoot = null, menuRoot = null;
        Action reconnect = null;
        try
        {
            foreach (string key in ints) PlayerPrefs.DeleteKey(Prefix + key);
            PlayerPrefs.DeleteKey(Prefix + "Volume");
            Check(RedboolDemoSettings.CameraIndex == 0 && !RedboolDemoSettings.LowQuality, "Fresh preferences use default camera and automatic quality");
            Check(RedboolDemoSettings.MasterVolume == 1, "Fresh master volume is audible");
            RedboolDemoSettings.CameraIndex = -100; Check(RedboolDemoSettings.CameraIndex == 0, "Negative camera index clamps to zero");
            RedboolDemoSettings.CameraIndex = 100; Check(RedboolDemoSettings.CameraIndex == 4, "Camera selection bounded to five indices");
            RedboolDemoSettings.MasterVolume = -.3f; Check(AudioListener.volume == 0, "Master mute applies to AudioListener");
            RedboolDemoSettings.MasterVolume = 4; Check(AudioListener.volume == 1, "Master audio cannot exceed one");
            RedboolDemoSettings.MasterVolume = float.NaN; Check(RedboolDemoSettings.MasterVolume == 1, "Invalid volume cannot poison audio state");
            RedboolDemoSettings.LowQuality = true; Check(RedboolDemoSettings.LowQuality, "Low rendering preference persists independently");
            RedboolDemoSettings.Fullscreen = false; Check(!RedboolDemoSettings.Fullscreen, "Window preference round trips without resizing editor");
            int requested = 0; reconnect = () => requested++;
            RedboolDemoSettings.CameraReconnectRequested += reconnect;
            RedboolDemoSettings.CameraIndex = 2;
            Check(requested == 0, "Selecting camera never reconnects implicitly");
            RedboolDemoSettings.RequestCameraReconnect(); Check(requested == 1, "Explicit apply emits one reconnect request");
            RedboolDemoSettings.CameraReconnectRequested -= reconnect; reconnect = null;
            RedboolDemoSettings.OperatorMode = true;
            typeof(RedboolDemoSettings).GetMethod("ResetSession", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            Check(!RedboolDemoSettings.OperatorMode, "Operator keys return off after session reset");
            Check(RedboolDemoSettings.CameraIndex == 2 && RedboolDemoSettings.LowQuality, "Session reset retains user preferences");

            // The fixture contains deliberately exact opaque bytes; cancellation
            // must not parse, migrate, save or clear the existing campaign.
            const string sentinel = "{\"test\":\"기존 저장 그대로\",\"seed\":49381,\"opaque\":true}";
            PlayerPrefs.SetString(SaveKey, sentinel);
            if (RunState.Instance == null)
            {
                runRoot = new GameObject("SettingsFixtureRun"); var run = runRoot.AddComponent<RunState>();
                // Editor fixture only: production Awake calls DontDestroyOnLoad,
                // which is invalid outside Play. Build real UI around a seeded
                // RunState without invoking lifecycle or changing production code.
                typeof(RunState).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public).GetSetMethod(true).Invoke(null, new object[] { run });
                typeof(RunState).GetProperty("Config").GetSetMethod(true).Invoke(run, new object[] { Resources.Load<RunConfig>(RunConfig.ResourcePath) });
            }
            var originalData = RunState.Instance.Data;
            menuRoot = new GameObject("SettingsFixtureMenu"); var menu = menuRoot.AddComponent<StartSceneUI>();
            typeof(StartSceneUI).GetMethod("Start", Private).Invoke(menu, null);
            Transform canvas = menuRoot.transform.Find("StartCanvas");
            Check(canvas.Find("DuelTestButton").GetComponentInChildren<Text>().text == "빠른 대전", "Quick match has a player-facing label");
            Check(canvas.Find("QuickDuelHelp").GetComponent<Text>().text.Contains("혼자"), "Quick match distinguishes solo from PvP");
            Check(canvas.Find("ReleaseVersion").GetComponent<Text>().text.Contains(RedboolDemoSettings.Release), "Operator release label matches the packaged version");
            foreach(string name in new[]{"DuelTestButton","QuickDuelHelp","ReleaseVersion"})
                Check(!canvas.Find(name).gameObject.activeSelf,"Default release menu hides "+name);
            float defaultPvpY=canvas.Find("PvpButton").GetComponent<RectTransform>().anchoredPosition.y;
            RedboolDemoSettings.OperatorMode=true;
            foreach(string name in new[]{"DuelTestButton","QuickDuelHelp","ReleaseVersion"})
                Check(canvas.Find(name).gameObject.activeSelf,"Explicit operator mode restores "+name);
            Check(canvas.Find("PvpButton").GetComponent<RectTransform>().anchoredPosition.y<defaultPvpY,"Showing the test tool reflows menu buttons without overlap");
            RedboolDemoSettings.OperatorMode=false;
            Check(canvas.Find("PvpButton").GetComponent<RectTransform>().anchoredPosition.y==defaultPvpY,"Hiding the test tool removes its empty menu row");
            CaptureUi("release_menu",canvas.GetComponent<Canvas>());
            canvas.Find("NewGameButton").GetComponent<Button>().onClick.Invoke();
            Check(RedboolNewGameConfirmation.IsOpen, "Saved campaign opens new-game confirmation");
            Check(PlayerPrefs.GetString(SaveKey) == sentinel && ReferenceEquals(RunState.Instance.Data, originalData), "Opening confirmation preserves saved bytes and in-memory run");
            var dialog = UnityEngine.Object.FindAnyObjectByType<RedboolNewGameConfirmation>();
            var cancel = dialog.transform.Find("NewGameConfirmationCanvas/ConfirmModal/ConfirmPanel/CancelNewGame").GetComponent<Button>();
            cancel.onClick.Invoke();
            Check(!RedboolNewGameConfirmation.IsOpen && PlayerPrefs.GetString(SaveKey) == sentinel && ReferenceEquals(RunState.Instance.Data, originalData), "Cancel preserves existing campaign exactly");
            Check(RedboolPauseMenu.BlocksStoryInput && (int)consumedFrame.GetValue(null) == Time.frameCount, "Closing a modal still consumes its actual input frame");
            if (dialog != null) UnityEngine.Object.DestroyImmediate(dialog.gameObject);
            int confirmed = 0; RedboolNewGameConfirmation.Show(() => confirmed++);
            dialog = UnityEngine.Object.FindAnyObjectByType<RedboolNewGameConfirmation>();
            var confirm = dialog.transform.Find("NewGameConfirmationCanvas/ConfirmModal/ConfirmPanel/ConfirmNewGame").GetComponent<Button>();
            // Capture the event before the immediate editor-only fixture cleanup.
            var clicked = confirm.onClick; clicked.Invoke(); clicked.Invoke();
            Check(confirmed == 1 && !RedboolNewGameConfirmation.IsOpen, "Explicit confirmation callback cannot execute twice");
            if (dialog != null) UnityEngine.Object.DestroyImmediate(dialog.gameObject);

            int before = requested; reconnect = () => requested++; RedboolDemoSettings.CameraReconnectRequested += reconnect;
            canvas.Find("SettingsButton").GetComponent<Button>().onClick.Invoke();
            Check(RedboolSettingsPanel.IsOpen && requested == before, "Settings opening performs no camera request");
            var settings = UnityEngine.Object.FindAnyObjectByType<RedboolSettingsPanel>();
            Transform panel = settings.transform.Find("SettingsCanvas/SettingsModal/SettingsPanel");
            panel.Find("ApplyCamera").GetComponent<Button>().onClick.Invoke();
            Check(requested == before + 1, "UI camera apply issues exactly one request");
            Check(!panel.Find("Version").gameObject.activeSelf, "Default settings hides the developer release label");
            Check(panel.Find("ExportDiagnostics").GetComponent<Button>()!=null, "Diagnostics remains reachable from settings without a menu footer");
            Check(!RedboolPackageDiagnostics.CanExport, "Editor fixture has no packaged exporter and cannot access user records");
            Check(!RedboolPackageDiagnostics.RequestExport(), "A missing packaged exporter refuses export without filesystem work");
            panel.Find("ToggleOperator").GetComponent<Button>().onClick.Invoke();
            Check(panel.Find("Version").gameObject.activeSelf&&canvas.Find("DuelTestButton").gameObject.activeSelf,"Settings operator toggle updates both settings and underlying menu");
            panel.Find("ToggleOperator").GetComponent<Button>().onClick.Invoke();
            Check(!panel.Find("Version").gameObject.activeSelf&&!canvas.Find("DuelTestButton").gameObject.activeSelf,"Returning to normal mode hides test tools immediately");
            CaptureUi("release_settings",canvas.GetComponent<Canvas>(),settings.GetComponentInChildren<Canvas>());
            panel.Find("CloseSettings").GetComponent<Button>().onClick.Invoke();
            Check(!RedboolSettingsPanel.IsOpen, "Settings closes without a scene change");
            if (settings != null) UnityEngine.Object.DestroyImmediate(settings.gameObject);
            Check(PlayerPrefs.GetString(SaveKey) == sentinel, "All settings leave campaign bytes unchanged");
            var config = Resources.Load<RunConfig>(RunConfig.ResourcePath);
            Check(config != null && config.mapMusic != null && config.mapMusic.name == "BGM_Tutorial", "Upstream tutorial music asset resolves through RunConfig");
            Check(config.heroMovement == HeroMovementStyle.CommonWalk, "BGM integration preserves existing walking configuration");
            CheckExportEntryGuard(canvas, menu, sentinel);
        }
        finally
        {
            if (reconnect != null) RedboolDemoSettings.CameraReconnectRequested -= reconnect;
            foreach (var panel in UnityEngine.Object.FindObjectsByType<RedboolSettingsPanel>(FindObjectsInactive.Include, FindObjectsSortMode.None)) UnityEngine.Object.DestroyImmediate(panel.gameObject);
            foreach (var panel in UnityEngine.Object.FindObjectsByType<RedboolNewGameConfirmation>(FindObjectsInactive.Include, FindObjectsSortMode.None)) UnityEngine.Object.DestroyImmediate(panel.gameObject);
            if (menuRoot != null) UnityEngine.Object.DestroyImmediate(menuRoot);
            if (runRoot != null)
            {
                typeof(RunState).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public).GetSetMethod(true).Invoke(null, new object[] { null });
                UnityEngine.Object.DestroyImmediate(runRoot);
            }
            foreach (string key in ints) { if (had[key]) PlayerPrefs.SetInt(Prefix + key, values[key]); else PlayerPrefs.DeleteKey(Prefix + key); }
            if (hadVolume) PlayerPrefs.SetFloat(Prefix + "Volume", volume); else PlayerPrefs.DeleteKey(Prefix + "Volume");
            if (hadSave) PlayerPrefs.SetString(SaveKey, save); else PlayerPrefs.DeleteKey(SaveKey);
            RedboolDemoSettings.OperatorMode = oldOperator; AudioListener.volume = audio; PlayerPrefs.Save();
            consumedFrame.SetValue(null, originalConsumedFrame);
        }
        Check((int)consumedFrame.GetValue(null) == originalConsumedFrame, "Settings fixture restores prior modal input suppression for subsequent checks");
        CheckStorage();
        string result = "PASS " + checks.Count + " settings/new-game checks\n" + string.Join("\n", checks);
        string output = Environment.GetEnvironmentVariable("REDBOOL_SETTINGS_CHECK_OUTPUT");
        if (!string.IsNullOrEmpty(output)) File.WriteAllText(output, result);
        Debug.Log(result);
    }

    static void CheckExportEntryGuard(Transform canvas, StartSceneUI menu, string saved)
    {
        var field=typeof(RedboolPackageDiagnostics).GetField("activeExport",BindingFlags.Static|BindingFlags.NonPublic);
        var previous=field.GetValue(null);var pending=new System.Threading.Tasks.TaskCompletionSource<string>();
        bool duel=RedboolDuelTest.Active,pvp=RedboolPvp.Active;
        try
        {
            field.SetValue(null,pending.Task);
            Check(RedboolPackageDiagnostics.IsExporting,"Pending export blocks game entry immediately");
            foreach(string button in new[]{"NewGameButton","ContinueButton","DuelTestButton","PvpButton"})
            {
                canvas.Find(button).GetComponent<Button>().onClick.Invoke();
                Check(canvas.Find("Message").GetComponent<Text>().text=="기록 저장이 끝나면 시작해주세요","Export explains blocked "+button);
            }
            Check(!RedboolNewGameConfirmation.IsOpen&&!canvas.Find("TwoScreenMenu").gameObject.activeSelf
                &&RedboolDuelTest.Active==duel&&RedboolPvp.Active==pvp&&PlayerPrefs.GetString(SaveKey)==saved,
                "Blocked entry creates no mode, lobby, save or deferred action");
            canvas.Find("SettingsButton").GetComponent<Button>().onClick.Invoke();
            Check(RedboolSettingsPanel.IsOpen,"Settings remains accessible during export");
            RedboolSettingsPanel.Close();
            pending.SetResult("fixture.zip");
            Check(!RedboolPackageDiagnostics.IsExporting,"Export completion unlocks without an Update tick");
            canvas.Find("PvpButton").GetComponent<Button>().onClick.Invoke();
            Check(canvas.Find("TwoScreenMenu").gameObject.activeSelf,"A fresh click opens lobby after export finishes");
            canvas.Find("PvpButton").GetComponent<Button>().onClick.Invoke();
            var fault=new System.Threading.Tasks.TaskCompletionSource<string>();field.SetValue(null,fault.Task);fault.SetException(new IOException("fixture"));
            Check(!RedboolPackageDiagnostics.IsExporting,"Failed export cannot permanently lock game entry");
            var observed=fault.Task.Exception;
            var cancelled=new System.Threading.Tasks.TaskCompletionSource<string>();field.SetValue(null,cancelled.Task);cancelled.SetCanceled();
            Check(!RedboolPackageDiagnostics.IsExporting,"Cancelled export cannot permanently lock game entry");
        }
        finally {pending.TrySetResult("fixture.zip");field.SetValue(null,previous);}
    }

    static void CheckStorage()
    {
        string root = Path.Combine(Path.GetTempPath(), "REDBOOL_storage_check_" + Guid.NewGuid().ToString("N"));
        string camera = Path.Combine(root, "CameraDiagnostics", "session");
        string exports = Path.Combine(root, "DiagnosticsExports");
        Directory.CreateDirectory(camera); Directory.CreateDirectory(exports);
        string record = Path.Combine(camera, "summary.json"), zip = Path.Combine(exports, "kept.zip");
        File.WriteAllBytes(record, new byte[33]); File.WriteAllBytes(zip, new byte[17]);
        File.WriteAllBytes(Path.Combine(root, "unrelated.mp4"), new byte[71]);
        try
        {
            var usage = RedboolPackageDiagnostics.MeasureStorage(root);
            Check(usage.camera_bytes == 33 && usage.export_bytes == 17 && usage.files == 2 && !usage.partial, "Storage totals cover diagnostic roots only");
            Check(File.ReadAllBytes(record).Length == 33 && File.ReadAllBytes(zip).Length == 17 && File.Exists(Path.Combine(root, "unrelated.mp4")), "Size inspection preserves every source and ZIP");
            using (var cancelled = new System.Threading.CancellationTokenSource())
            {
                cancelled.Cancel(); var stopped = RedboolPackageDiagnostics.MeasureStorage(root, cancelled.Token);
                Check(stopped.cancelled && stopped.files == 0, "Storage scan respects scene-exit cancellation before filesystem traversal");
            }
            Check(RedboolPackageDiagnostics.MeasureStorage(Path.Combine(root, "missing")).files == 0, "Fresh install with no logs returns zero without creating directories");
            Check(!Directory.Exists(Path.Combine(root, "missing")), "Storage inspection never creates recording folders");
            if (Application.platform == RuntimePlatform.OSXEditor)
            {
                string link = Path.Combine(camera, "linked-outside");
                using (var child = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
                    FileName = "/bin/ln", Arguments = "-s \"" + exports + "\" \"" + link + "\"", UseShellExecute = false, CreateNoWindow = true }))
                { child.WaitForExit(); Check(child.ExitCode == 0, "Linked storage fixture created"); }
                try
                {
                    var linked = RedboolPackageDiagnostics.MeasureStorage(root);
                    Check(linked.skipped_links == 1 && linked.partial && linked.camera_bytes == 33 && linked.export_bytes == 17, "Storage scan excludes symbolic links without double counting");
                }
                finally { Directory.Delete(link); }
            }
        }
        finally { Directory.Delete(root, true); }
    }

    static void CaptureUi(string name,params Canvas[] canvases)
    {
        string output=Environment.GetEnvironmentVariable("REDBOOL_SETTINGS_UI_OUTPUT");
        if(string.IsNullOrEmpty(output))return;
        Directory.CreateDirectory(output);
        var modes=new RenderMode[canvases.Length];var cameras=new Camera[canvases.Length];
        var distances=new float[canvases.Length];var scales=new float[canvases.Length];
        var enabled=new bool[canvases.Length];
        var camera=new GameObject("Settings fixture render camera").AddComponent<Camera>();
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.025f,.035f,.04f);
        var target=new RenderTexture(1280,720,24);target.Create();camera.targetTexture=target;
        var previous=RenderTexture.active;Texture2D image=null;
        try
        {
            for(int i=0;i<canvases.Length;i++)
            {
                var c=canvases[i];modes[i]=c.renderMode;cameras[i]=c.worldCamera;distances[i]=c.planeDistance;scales[i]=c.scaleFactor;
                var scaler=c.GetComponent<CanvasScaler>();enabled[i]=scaler.enabled;scaler.enabled=false;
                c.renderMode=RenderMode.ScreenSpaceCamera;c.worldCamera=camera;c.planeDistance=1;c.scaleFactor=2f/3f;
            }
            Canvas.ForceUpdateCanvases();
            if(UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline!=null)
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,new UnityEngine.Rendering.RenderPipeline.StandardRequest {destination=target});
            else camera.Render();
            RenderTexture.active=target;image=new Texture2D(1280,720,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();
            File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());
        }
        finally
        {
            for(int i=0;i<canvases.Length;i++)
            {
                var c=canvases[i];c.renderMode=modes[i];c.worldCamera=cameras[i];c.planeDistance=distances[i];c.scaleFactor=scales[i];c.GetComponent<CanvasScaler>().enabled=enabled[i];
            }
            RenderTexture.active=previous;camera.targetTexture=null;
            if(image!=null)UnityEngine.Object.DestroyImmediate(image);
            target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(camera.gameObject);
        }
    }
}
