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
public static class RedboolHeldGuardPlayCheck
{
    const string Active = "RedboolHeldGuardPlayCheck.Active", SaveKey = "VisionStriker.Run";
    static IEnumerator flow;
    static int lastFrame = -1;
    static readonly List<string> checks = new List<string>();
    static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../../held_guard_20261109/native_guard"));
    static RedboolHeldGuardPlayCheck() { if (SessionState.GetBool(Active, false)) Subscribe(); }
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
        // An unrelated editor error (e.g. a plugin login failure) can trigger Error Pause in batch mode.
        EditorApplication.isPaused = false;
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
        var receiver=Object.FindAnyObjectByType<RedboolDemoReceiver>();
        // Isolate Fighter mechanics from packet expiry; Safe is checked explicitly below.
        typeof(RedboolDemoReceiver).GetField("deadline",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(receiver,long.MaxValue);
        var player=Object.FindAnyObjectByType<PlayerController>();
        var fighter=player.GetComponent<Fighter>();
        var gate=Object.FindAnyObjectByType<RedboolBattlePreparation>();
        Check(!fighter.SetVisionGuard(true), "Uncalibrated player cannot start guard");
        gate.Observe(true,"SYNTHETIC","READY",false,"NEUTRAL");
        Check(fighter.SetVisionGuard(true), "Live pose starts camera guard");
        Check(fighter.IsGuarding && fighter.IsVisionGuardHeld, "Held guard is owned by the camera pose");
        // Hold-based guard (2026-11-09 combat rules): no timed expiry. Hold well past the old 1.5 s limit.
        double until=Time.realtimeSinceStartupAsDouble+2.0;
        while(Time.realtimeSinceStartupAsDouble<until) {
            gate.Observe(true,"SYNTHETIC","READY",true,"NEUTRAL");
            if(!fighter.IsGuarding) throw new Exception("Held guard expired early");
            yield return null;
        }
        Check(fighter.IsGuarding,"Guard remains held beyond the old timed limit");
        Check(fighter.CanAct,"Held pose never acquires guard-failure recovery");
        typeof(Fighter).GetMethod("GuardSucceeded",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(fighter,null);
        Check(fighter.IsGuarding && fighter.IsVisionGuardHeld,"Successful block retains camera guard");
        fighter.SetVisionGuard(false);
        Check(!fighter.IsGuarding && !fighter.IsVisionGuardHeld && fighter.CanAttack,"Release permits attack in same frame");
        Check(fighter.TryAttack(CombatMove.Jab),"Immediate follow-up jab is accepted");
        Check(!fighter.SetVisionGuard(true),"Guard does not cancel an active attack");
        fighter.CancelAttack();
        Check(fighter.SetVisionGuard(true),"New pose can guard after attack");
        typeof(RedboolDemoReceiver).GetMethod("Safe",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(receiver,null);
        Check(!fighter.IsGuarding,"Receiver expiry/disconnect safety releases owned guard");
        Check(fighter.SetVisionGuard(true),"Guard can restart after safe release");
        fighter.SetVisionPreparationLock(true);
        Check(!fighter.IsGuarding && !fighter.IsVisionGuardHeld,"Tracking/preparation lock releases held guard");
        fighter.SetVisionGuard(false);
        Check(!fighter.CanAct,"Release cannot clear preparation lock");
        fighter.SetVisionPreparationLock(false);
        fighter.InputLocked=true;
        Check(!fighter.SetVisionGuard(true),"KO/story lock cannot be bypassed");
        fighter.SetVisionGuard(false); Check(fighter.InputLocked,"Release preserves KO/story lock");
        fighter.InputLocked=false;
        Check(fighter.TryGuard(false),"Keyboard guard remains available");
        Check(!fighter.IsVisionGuardHeld,"Keyboard guard has no camera owner");
        fighter.SetVisionGuard(false);Check(fighter.IsGuarding,"Camera release cannot cancel unrelated keyboard guard");
        fighter.ReleaseGuard();
        Check(!fighter.IsGuarding && fighter.CanAttack,"Keyboard guard ends on release with no failure delay");
        Check(PlayerPrefs.GetString(SaveKey,"")==SessionState.GetString(Active+".Save",""),"Campaign save unchanged");
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
