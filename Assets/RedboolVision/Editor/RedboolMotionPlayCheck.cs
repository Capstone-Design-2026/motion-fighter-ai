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
public static class RedboolMotionPlayCheck
{
    const string Active = "RedboolMotionPlayCheck.Active", SaveKey = "VisionStriker.Run";
    static IEnumerator flow;
    static int lastFrame = -1;
    static readonly List<string> checks = new List<string>();
    static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../../persistent_calibration_20261105/native_play"));
    static RedboolMotionPlayCheck() { if (SessionState.GetBool(Active, false)) Subscribe(); }
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
        RedboolBattleInstallationCheck.Run(); Check(true, "GameScene auto connection");
        var player = Object.FindAnyObjectByType<PlayerController>();
        var enemy = Object.FindAnyObjectByType<EnemyAI>();
        var gate = Object.FindAnyObjectByType<RedboolBattlePreparation>();
        var round = Object.FindAnyObjectByType<BattleSceneBridge>();
        var fighter = player.GetComponent<Fighter>();
        int hp = fighter.Health.CurrentHealth; float seconds = round.RemainingSeconds;
        for (var wait = Wait(.6f); wait.MoveNext();) yield return null;
        Check(!gate.AllowsPlayer && fighter.VisionPreparationLocked, "No camera holds player");
        Check(enemy.GetComponent<Fighter>().VisionPreparationLocked && !enemy.GetComponent<Fighter>().IsAttacking, "No camera holds enemy");
        Check(Mathf.Abs(round.RemainingSeconds - seconds) < .001f, "Preparation holds round clock");
        Check(fighter.Health.CurrentHealth == hp, "Preparation preserves HP");
        gate.Observe(false, "VALID", "READY", false, "NEUTRAL");
        Check(!gate.AllowsPlayer, "Uncalibrated ready pose cannot start battle");
        gate.Observe(true, "VALID", "READY", false, "NEUTRAL");
        Check(gate.AllowsPlayer && !fighter.VisionPreparationLocked, "Calibration starts immediately without extra hold");
        Check(!enemy.GetComponent<Fighter>().VisionPreparationLocked, "Calibrated live input releases enemy");
        fighter.InputLocked = true;
        gate.Observe(true, "VALID", "READY", false, "NEUTRAL");
        Check(fighter.InputLocked && !fighter.CanAct, "Preparation does not clear KO/story input lock");
        fighter.InputLocked = false;
        gate.Observe(false, "VALID", "READY", false, "NEUTRAL");
        Check(fighter.VisionPreparationLocked && gate.HoldRound, "Manual recalibration holds immediately");
        gate.Observe(true, "SYNTHETIC", "READY", false, "NEUTRAL");
        Check(!fighter.VisionPreparationLocked && enemy.GetComponent<Fighter>().VisionPreparationLocked, "Probe unlocks only player");
        var special = player.GetComponent<SpecialMove>();
        special.ResetCharge(); Check(!special.Activate(), "Special rejects empty charge");
        special.AddCharge(special.HitsToCharge);
        Check(special.Activate() && special.IsPlaying, "Charged special starts native game routine");
        Check(!special.IsReady && !special.Activate(), "One activation consumes charge and rejects duplicate");
        special.enabled = false; // End the test cinematic before the scene transition.
        SceneManager.LoadScene("ScoutPass"); yield return null; yield return null;
        RedboolBattleInstallationCheck.Run(); Check(true, "ScoutPass auto reconnects with one receiver");
        gate = Object.FindAnyObjectByType<RedboolBattlePreparation>();
        Check(!gate.AllowsPlayer, "New scene waits for a fresh packet");
        gate.Observe(true, "VALID", "ACTIVE", false, "LEFT");
        Check(gate.AllowsPlayer, "Next scene accepts preserved calibration without neutral pose or timer");
        Check(PlayerPrefs.GetString(SaveKey, "") == SessionState.GetString(Active + ".Save", ""), "Saved progress unchanged");
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
