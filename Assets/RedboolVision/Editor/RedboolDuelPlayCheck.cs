using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Batch-only smoke check on real combat scenes; never opens a camera.</summary>
[InitializeOnLoad]
public static class RedboolDuelPlayCheck
{
    const string Active = "RedboolDuelPlayCheck.Active", SaveKey = "VisionStriker.Run";
    static IEnumerator flow;
    static int lastFrame = -1;
    static readonly List<string> checks = new List<string>();
    static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../../right_fix_20261108/native_duel"));
    static RedboolDuelPlayCheck() { if (SessionState.GetBool(Active, false)) Subscribe(); }
    public static void Run()
    {
        if (!Application.isBatchMode) throw new Exception("Run this isolated smoke check with Unity -batchmode.");
        Directory.CreateDirectory(Output);
        SessionState.SetBool(Active + ".HadSave", PlayerPrefs.HasKey(SaveKey));
        SessionState.SetString(Active + ".Save", PlayerPrefs.GetString(SaveKey, ""));
        SessionState.SetFloat(Active + ".Start", (float)EditorApplication.timeSinceStartup);
        EditorSceneManager.OpenScene("Assets/Scenes/GameScene.unity");
        SessionState.SetBool(Active, true); Subscribe();
        EditorApplication.delayCall += () => EditorApplication.EnterPlaymode();
    }
    static void Subscribe() { EditorApplication.update -= Tick; EditorApplication.update += Tick; }
    static void Tick()
    {
        if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Active + ".Start", 0) > 120) { Finish("Timeout"); return; }
        if (!Application.isPlaying || EditorApplication.isCompiling) return;
        EditorApplication.QueuePlayerLoopUpdate();
        if (lastFrame == Time.frameCount) return;
        lastFrame = Time.frameCount;
        try { if (flow == null) flow = Tests(); if (!flow.MoveNext()) Finish(null); }
        catch (Exception e) { Finish(e.ToString()); }
    }
    static void Check(bool ok, string message)
    {
        checks.Add((ok ? "PASS " : "FAIL ") + message);
        File.WriteAllLines(Path.Combine(Output, "checks.txt"), checks);
        if (!ok) throw new Exception(message);
    }
    static IEnumerator Wait(float seconds)
    {
        double until = Time.realtimeSinceStartupAsDouble + seconds;
        while (Time.realtimeSinceStartupAsDouble < until) yield return null;
    }
    static IEnumerator Tests()
    {
        yield return null; yield return null;
        Check(!RedboolDuelTest.Active, "Ordinary startup is campaign mode");
        var run = RunState.Instance;
        var data = new RunData(); data.map.nodes.Add(new MapNode { id = "saved-stage" });
        data.pendingNodeId = "saved-stage"; data.playerHealth = 1;
        typeof(RunState).GetProperty("Data").SetValue(run, data);
        Check(run.HasPendingBattle, "Pending campaign exists in memory before direct test");
        string save = PlayerPrefs.GetString(SaveKey, "");
        RedboolDuelTest.Begin(); yield return null; yield return null;
        Check(RedboolDuelTest.Active && SceneManager.GetActiveScene().name == SceneRoutes.Battle,
            "Direct entry skips story/map/tutorial");
        Check(Object.FindAnyObjectByType<CaveTutorial>() == null, "No tutorial in duel");
        Check(Object.FindObjectsByType<RedboolDemoReceiver>(FindObjectsSortMode.None).Length == 1,
            "Exactly one automatically connected receiver");
        var player = Object.FindAnyObjectByType<PlayerController>();
        var health = player.GetComponent<Health>();
        Check(health.CurrentHealth == health.MaxHealth, "Pending campaign HP is not applied");
        Check(Object.FindAnyObjectByType<CombatHUD>().AllowRestart, "Round restart remains enabled");
        var gate = Object.FindAnyObjectByType<RedboolBattlePreparation>();
        Check(gate.HoldRound, "Duel still waits for camera calibration");
        gate.Observe(true, "SYNTHETIC", "READY", false, "NEUTRAL");
        Check(gate.AllowsPlayer, "Fresh calibrated input starts duel");
        var special = player.GetComponent<SpecialMove>(); special.AddCharge(special.HitsToCharge);
        Check(special.Activate(), "Special activates in direct duel");
        special.enabled = false;
        run.Save(); run.ClearSave();
        Check(PlayerPrefs.GetString(SaveKey, "") == save, "Save and clear cannot modify campaign in duel");
        RedboolDuelTest.Restart(); yield return null; yield return null;
        Check(SceneManager.GetActiveScene().name == SceneRoutes.Battle,
            "Restart returns straight to combat");
        Check(Object.FindObjectsByType<RedboolDuelTest>(FindObjectsSortMode.None).Length == 1,
            "Restart retains one test controller");
        Check(Object.FindObjectsByType<RedboolDemoReceiver>(FindObjectsSortMode.None).Length == 1,
            "Restart creates one receiver");
        Check(Object.FindAnyObjectByType<RedboolBattlePreparation>().HoldRound,
            "Restart waits for fresh state before enemy attacks");
        Check(PlayerPrefs.GetString(SaveKey, "") == save, "Restart preserves campaign save");
    }
    static void Finish(string failure)
    {
        SessionState.SetBool(Active, false); EditorApplication.update -= Tick;
        if (SessionState.GetBool(Active + ".HadSave", false)) PlayerPrefs.SetString(SaveKey, SessionState.GetString(Active + ".Save", ""));
        else PlayerPrefs.DeleteKey(SaveKey);
        PlayerPrefs.Save();
        File.WriteAllText(Path.Combine(Output, "result.txt"), failure == null ? "PASS\n" + checks.Count + " checks" : "FAIL\n" + failure);
        EditorApplication.Exit(failure == null ? 0 : 1);
    }
}
