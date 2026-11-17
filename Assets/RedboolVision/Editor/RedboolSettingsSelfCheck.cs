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
            Check(canvas.Find("DuelTestButton") == null && canvas.Find("QuickDuelHelp") == null, "Menu has no quick-match entry or its help line");
            Check(canvas.Find("ReleaseVersion") == null, "Menu shows no release version label");
            canvas.Find("NewGameButton").GetComponent<Button>().onClick.Invoke();
            Check(RedboolNewGameConfirmation.IsOpen, "Saved campaign opens new-game confirmation");
            Check(PlayerPrefs.GetString(SaveKey) == sentinel && ReferenceEquals(RunState.Instance.Data, originalData), "Opening confirmation preserves saved bytes and in-memory run");
            var dialog = UnityEngine.Object.FindAnyObjectByType<RedboolNewGameConfirmation>();
            var cancel = dialog.transform.Find("NewGameConfirmationCanvas/ConfirmModal/ConfirmPanel/CancelNewGame").GetComponent<Button>();
            cancel.onClick.Invoke();
            Check(!RedboolNewGameConfirmation.IsOpen && PlayerPrefs.GetString(SaveKey) == sentinel && ReferenceEquals(RunState.Instance.Data, originalData), "Cancel preserves existing campaign exactly");
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
            Check(panel.Find("Version") == null, "Settings shows no release version label");
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
        }
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
            foreach(string button in new[]{"NewGameButton","ContinueButton","PvpButton"})
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
}
