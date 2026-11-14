using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Explicit, unsaved play-mode checks. Returns to the user's original editor scene.</summary>
[InitializeOnLoad]
public static class BattleIntroVerification
{
    private const string ActiveKey = "BattleIntroVerification.Active";
    private const string PreviousStartKey = "BattleIntroVerification.PreviousStart";
    private const string BridgeGuid = "aa043eace2d54669ad7b5007a315f75d";
    private static readonly List<string> checks = new List<string>();
    private static IEnumerator flow;
    private static int lastFrame = -1;
    private static double began;
    private static bool previousBackground;
    private static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    private static string Output => Path.GetFullPath(Path.Combine(Root, "..", "combat_intro_validation"));
    private static string Request => Path.Combine(Root, "Temp", "BattleIntroVerification.request");

    static BattleIntroVerification()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        if (SessionState.GetBool(ActiveKey, false)) EditorApplication.update += Tick;
        else if (File.Exists(Request)) EditorApplication.delayCall += Run;
    }

    [MenuItem("Tools/Motion Fighter/Verify Ready Fight Intro")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        Directory.CreateDirectory(Output);
        if (File.Exists(Request)) File.Delete(Request);
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty)
            {
                File.WriteAllText(Path.Combine(Output, "status.txt"), "SKIPPED: save the edited scene before verification");
                return;
            }
        File.WriteAllText(Path.Combine(Output, "status.txt"), "STARTING");
        SessionState.SetString(PreviousStartKey, AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/GameScene.unity");
        SessionState.SetBool(ActiveKey, true);
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetString(PreviousStartKey, "<none>") != "<none>")
        {
            string path = SessionState.GetString(PreviousStartKey, "");
            EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
            SessionState.EraseString(PreviousStartKey);
            if (SessionState.GetBool(ActiveKey, false))
            {
                File.WriteAllText(Path.Combine(Output, "status.txt"), "INTERRUPTED");
                SessionState.SetBool(ActiveKey, false);
            }
        }
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(ActiveKey, false))
        {
            EditorApplication.update -= Tick;
            return;
        }
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (flow == null)
        {
            checks.Clear(); began = EditorApplication.timeSinceStartup;
            previousBackground = Application.runInBackground;
            Application.runInBackground = true;
            RedboolBattlePreparation.SetKeyboardTest(true);
            flow = Tests();
        }
        if (EditorApplication.timeSinceStartup - began > 360)
        {
            Check(false, "Verification timed out"); Finish(); return;
        }
        EditorApplication.QueuePlayerLoopUpdate();
        if (lastFrame == Time.frameCount) return;
        lastFrame = Time.frameCount;
        try { if (!flow.MoveNext()) Finish(); }
        catch (Exception ex) { Check(false, ex.ToString()); Finish(); }
    }

    private static void Check(bool ok, string description)
    {
        checks.Add((ok ? "PASS " : "FAIL ") + description);
        File.WriteAllLines(Path.Combine(Output, "checks.txt"), checks);
    }

    private static void Finish()
    {
        File.WriteAllText(Path.Combine(Output, "status.txt"), checks.All(c => c.StartsWith("PASS ")) ? "PASS" : "FAIL");
        SessionState.SetBool(ActiveKey, false);
        EditorApplication.update -= Tick;
        Application.runInBackground = previousBackground;
        flow = null;
        EditorApplication.ExitPlaymode();
    }

    private static IEnumerator Wait(float seconds)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < end) yield return null;
    }

    private static IEnumerator UntilFinished(BattleIntroSequence intro)
    {
        float timeout = Time.realtimeSinceStartup + 10;
        while (intro != null && intro.HoldRound && Time.realtimeSinceStartup < timeout) yield return null;
        Check(intro != null && !intro.HoldRound, "Intro completes without a stuck lock");
    }

    private static IEnumerator Tests()
    {
        var scenes = AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" })
            .Select(AssetDatabase.GUIDToAssetPath).Where(p => File.ReadAllText(p).Contains(BridgeGuid))
            .OrderBy(p => p == "Assets/Scenes/GameScene.unity" ? "" : p).ToArray();
        Check(scenes.Length == 24, "All 24 battle scenes share BattleSceneBridge");
        foreach (string menu in new[] { "StartScene", "MapScene", "EndingScene" })
            Check(!File.ReadAllText("Assets/Scenes/" + menu + ".unity").Contains(BridgeGuid), menu + " has no battle intro");

        for (int sceneIndex = 0; sceneIndex < scenes.Length; sceneIndex++)
        {
            string path = scenes[sceneIndex];
            File.WriteAllText(Path.Combine(Output, "status.txt"), "RUNNING " + (sceneIndex + 1) + "/" + scenes.Length + " " + path);
            RedboolBattlePreparation.SetKeyboardTest(true);
            if (SceneManager.GetActiveScene().path != path)
                EditorSceneManager.LoadSceneInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            yield return null;
            var intro = Object.FindAnyObjectByType<BattleIntroSequence>();
            var battle = Object.FindAnyObjectByType<BattleSceneBridge>();
            var actors = Object.FindObjectsByType<Fighter>();
            Check(intro != null && battle != null && actors.Length >= 2, path + " installs intro and fighters");
            if (intro == null || battle == null || actors.Length < 2) continue;
            // Restart makes assertions independent of the editor's first-frame import time.
            battle.ResetRoundTimer();
            float time = battle.RemainingSeconds;
            float timeScale = Time.timeScale;
            var positions = actors.Select(f => f.transform.position.x).ToArray();
            var health = actors.Select(f => f.Health.CurrentHealth).ToArray();
            Check(actors.All(f => f.RoundIntroLocked && f.InputLocked && !f.TryAttack(CombatMove.Jab)), path + " blocks attacks before READY");
            Check(actors.All(f => !f.Health.TakeDamage(1, Vector2.zero, false, true)), path + " rejects damage during intro");
            bool sawReady = false, sawFight = false, timerHeld = true, enemiesHeld = true;
            bool readyShot = false, fightShot = false;
            float timeout = Time.realtimeSinceStartup + 10;
            while (intro.HoldRound && Time.realtimeSinceStartup < timeout)
            {
                sawReady |= intro.Phase == BattleIntroSequence.IntroPhase.Ready;
                sawFight |= intro.Phase == BattleIntroSequence.IntroPhase.Fight;
                timerHeld &= Mathf.Approximately(time, battle.RemainingSeconds);
                for (int i = 0; i < actors.Length; i++) enemiesHeld &= Mathf.Abs(actors[i].transform.position.x - positions[i]) < .04f;
                var group = intro.GetComponentInChildren<CanvasGroup>();
                if (sceneIndex == 0 && group != null && group.alpha > .95f)
                {
                    if (!readyShot && intro.Phase == BattleIntroSequence.IntroPhase.Ready)
                    { ScreenCapture.CaptureScreenshot(Path.Combine(Output, "ready.png")); readyShot = true; }
                    if (!fightShot && intro.Phase == BattleIntroSequence.IntroPhase.Fight)
                    { ScreenCapture.CaptureScreenshot(Path.Combine(Output, "fight.png")); fightShot = true; }
                }
                yield return null;
            }
            Check(sawReady && sawFight && !intro.HoldRound, path + " displays READY then FIGHT and finishes");
            Check(timerHeld && enemiesHeld, path + " holds timer and both fighter positions");
            Check(actors.All(f => !f.RoundIntroLocked && !f.InputLocked), path + " unlocks both fighters");
            Check(Mathf.Approximately(timeScale, Time.timeScale), path + " preserves global time scale");
            Check(actors.Select((f, i) => f.Health.CurrentHealth == health[i]).All(v => v), path + " preserves health");
            foreach (var actor in actors) actor.InputLocked = true;
            for (var wait = Wait(.15f); wait.MoveNext();) yield return null;
            Check(battle.RemainingSeconds < time, path + " timer starts after FIGHT");
            foreach (var actor in actors) actor.InputLocked = false;

            if (sceneIndex != 0) continue;
            battle.ResetRoundTimer();
            var gate = Object.FindAnyObjectByType<RedboolBattlePreparation>();
            var receiver = gate != null ? gate.receiver : null;
            bool receiverWasEnabled = receiver != null && receiver.enabled;
            if (receiver != null) receiver.enabled = false;
            if (gate != null) gate.ResetPreparation();
            RedboolBattlePreparation.SetKeyboardTest(false);
            for (var wait = Wait(2.2f); wait.MoveNext();) yield return null;
            Check(intro.HoldRound && intro.Phase == BattleIntroSequence.IntroPhase.Ready
                && Mathf.Approximately(battle.RemainingSeconds, time), "Camera preparation holds READY and timer");
            RedboolBattlePreparation.SetKeyboardTest(true);
            if (receiver != null) receiver.enabled = receiverWasEnabled;
            for (var wait = UntilFinished(intro); wait.MoveNext();) yield return null;

            battle.ResetRoundTimer();
            var first = actors[0];
            first.InputLocked = true;
            first.Health.DamageLocked = true;
            for (var wait = UntilFinished(intro); wait.MoveNext();) yield return null;
            Check(first.InputLocked && first.Health.DamageLocked && !first.RoundIntroLocked, "Ending intro preserves another owner's input/damage lock");
            first.InputLocked = false; first.Health.DamageLocked = false;

            battle.ResetRoundTimer();
            Time.timeScale = 0;
            for (var wait = Wait(.35f); wait.MoveNext();) yield return null;
            Check(intro.HoldRound && intro.Phase == BattleIntroSequence.IntroPhase.Settling, "Pause does not consume the intro");
            Time.timeScale = timeScale;
            intro.enabled = false;
            Check(actors.All(f => !f.RoundIntroLocked), "Disabling intro cleans up only its own locks");
        }
    }
}
