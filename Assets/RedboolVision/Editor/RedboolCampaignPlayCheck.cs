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
public static class RedboolCampaignPlayCheck
{
    const string Active = "RedboolCampaignPlayCheck.Active", SaveKey = "VisionStriker.Run";
    static IEnumerator flow;
    static int lastFrame = -1;
    static readonly List<string> checks = new List<string>();
    static string Output => Environment.GetEnvironmentVariable("REDBOOL_FINAL_CHECK_OUTPUT");
    static RedboolCampaignPlayCheck() { if (SessionState.GetBool(Active, false)) Subscribe(); }
    public static void Run()
    {
        if (!Application.isBatchMode) throw new Exception("Run this isolated smoke check with Unity -batchmode.");
        AssetDatabase.DisallowAutoRefresh();
        EditorApplication.LockReloadAssemblies();
        Directory.CreateDirectory(Output);
        File.WriteAllText(Path.Combine(Output,"original-save.txt"),PlayerPrefs.GetString(SaveKey,""));
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
        if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Active + ".Start", 0) > 360) { Finish("Timeout"); return; }
        if (!Application.isPlaying) return;
        EditorApplication.isPaused = false; // batch: ignore Error Pause from unrelated editor errors
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
        Check(!RedboolDuelTest.Active, "Campaign startup is not unsaved duel mode");
        var run=RunState.Instance;
        run.StartNewRun(20261110);
        Check(run.HasRun && run.HasSave() && !run.Data.tutorialCompleted, "New run creates save and pending tutorial");
        run.CompleteTutorial();
        Check(run.Data.tutorialCompleted && run.Data.clearedNodeIds.Contains("cave"), "Tutorial completion unlocks map");
        int seed=run.Data.seed;
        Check(run.LoadRun() && run.Data.seed==seed && run.Data.tutorialCompleted, "Saved tutorial and seed survive reload");
        var nodes=run.Data.map.nodes.FindAll(n=>n.IsBattle);
        run.Data.pendingNodeId=nodes[0].id;run.Save();
        string pendingScene=SceneRoutes.PendingBattleScene;
        SceneRoutes.LoadStart();yield return null;yield return null;yield return null;
        var start=Object.FindAnyObjectByType<StartSceneUI>();
        Check(start!=null,"Start menu present for real Continue callback");
        typeof(StartSceneUI).GetMethod("OnContinue",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(start,null);
        yield return null;yield return null;yield return null;
        Check(SceneManager.GetActiveScene().name==pendingScene && run.HasPendingBattle,"Continue resumes saved battle instead of locked map");
        Check(Object.FindAnyObjectByType<RedboolBattlePreparation>().HoldRound,"Resumed battle waits for fresh calibrated camera state");

        if(Environment.GetEnvironmentVariable("REDBOOL_CONTINUE_ONLY")=="1")
        {
            run.Data.pendingNodeId="";run.Save();SceneRoutes.LoadStart();yield return null;yield return null;yield return null;
            start=Object.FindAnyObjectByType<StartSceneUI>();
            typeof(StartSceneUI).GetMethod("OnContinue",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(start,null);
            yield return null;yield return null;yield return null;
            Check(SceneManager.GetActiveScene().name==SceneRoutes.Map && !run.HasPendingBattle,"Continue without pending battle still opens map");
            yield break;
        }
        foreach(var node in nodes)
        {
            run.StartNewRun(20261110); run.CompleteTutorial();
            run.Data.seenStoryIds.Add("final_after");
            foreach(var n in run.Data.map.nodes) {run.Data.seenStoryIds.Add(n.id+"_visit");run.Data.seenStoryIds.Add(n.id+"_after");}
            run.Data.pendingNodeId=node.id;run.Data.playerHealth=73;run.Save();
            Check(run.LoadRun() && run.HasPendingBattle && run.PendingNode.id==node.id, node.id+" pending battle survives reload");
            string expected=SceneRoutes.PendingBattleScene;
            Check(Application.CanStreamedLevelBeLoaded(expected), node.id+" configured scene exists in build");
            SceneRoutes.LoadBattle(); yield return null; yield return null; yield return null;
            Check(SceneManager.GetActiveScene().name==expected,node.id+" routes to correct scene");
            RedboolBattleInstallationCheck.Run();Check(true,node.id+" one receiver, correct player, no legacy socket");
            var player=Object.FindAnyObjectByType<PlayerController>();var enemy=Object.FindAnyObjectByType<EnemyAI>();
            var gate=Object.FindAnyObjectByType<RedboolBattlePreparation>();var round=Object.FindAnyObjectByType<BattleSceneBridge>();
            Check(player!=null && enemy!=null && round!=null,node.id+" combat actors and round controller present");
            Check(player.GetComponent<Health>().CurrentHealth==(run.Config.carryPlayerHealth?73:run.Data.playerMaxHealth),node.id+" saved HP carried into battle");
            float seconds=round.RemainingSeconds;int hp=player.GetComponent<Health>().CurrentHealth;
            for(var wait=Wait(.2f);wait.MoveNext();)yield return null;
            Check(gate.HoldRound && enemy.GetComponent<Fighter>().VisionPreparationLocked && player.GetComponent<Health>().CurrentHealth==hp && Mathf.Abs(round.RemainingSeconds-seconds)<.01f,node.id+" waiting camera holds enemy, HP and timer");
            gate.Observe(true,"VALID","ACTIVE",false,"LEFT");
            Check(gate.AllowsPlayer && !gate.HoldRound,node.id+" preserved calibration starts without another pose hold");
            enemy.enabled=false;
            Check(enemy.GetComponent<Health>().TakeDamage(99999,Vector2.zero,false,true),node.id+" real death handler triggered");
            double deadline=Time.realtimeSinceStartupAsDouble+10;
            while(SceneManager.GetActiveScene().name==expected && Time.realtimeSinceStartupAsDouble<deadline)yield return null;
            string destination=node.type==NodeType.Boss?SceneRoutes.Ending:SceneRoutes.Map;
            Check(SceneManager.GetActiveScene().name==destination,node.id+" victory routes to "+destination);
            Check(!run.HasPendingBattle && run.Data.clearedNodeIds.Contains(node.id),node.id+" victory clears pending node and records completion");
            Check(Mathf.Abs(Time.timeScale-1f)<.001f,node.id+" restores normal clock after KO");
            if(node.type==NodeType.Boss)Check(run.Data.finished && !run.HasSave(),"Boss completion ends run and clears continue save");
            else Check(run.LoadRun() && run.Data.clearedNodeIds.Contains(node.id),node.id+" victory survives save/reload");
        }
        run.StartNewRun(20261110);run.CompleteTutorial();run.Data.pendingNodeId=nodes[0].id;run.Save();
        SceneRoutes.LoadBattle();yield return null;yield return null;yield return null;
        var p=Object.FindAnyObjectByType<PlayerController>();var g=Object.FindAnyObjectByType<RedboolBattlePreparation>();
        g.Observe(true,"VALID","READY",false,"NEUTRAL");
        Check(p.GetComponent<Health>().TakeDamage(99999,Vector2.zero,false,true),"Player KO triggers actual loss handler");
        double end=Time.realtimeSinceStartupAsDouble+10;
        while(SceneManager.GetActiveScene().name!=SceneRoutes.Start && Time.realtimeSinceStartupAsDouble<end)yield return null;
        Check(SceneManager.GetActiveScene().name==SceneRoutes.Start && !run.HasSave() && !run.HasRun,"Loss returns to start and clears defeated run");
        run.StartNewRun(20261110);run.CompleteTutorial();run.Save();string save=PlayerPrefs.GetString(SaveKey,"");
        RedboolDuelTest.Begin();yield return null;yield return null;yield return null;
        Check(RedboolDuelTest.Active && SceneManager.GetActiveScene().name==RedboolDuelTest.ArenaScene,"Direct final rehearsal uses authored ScoutPass arena and skips story/tutorial");
        run.Save();run.ClearSave();
        Check(PlayerPrefs.GetString(SaveKey,"")==save,"Direct rehearsal cannot overwrite campaign save");
        RedboolDuelTest.Restart();yield return null;yield return null;
        Check(Object.FindObjectsByType<RedboolDemoReceiver>(FindObjectsSortMode.None).Length==1 && Object.FindAnyObjectByType<RedboolBattlePreparation>().HoldRound,"Duel restart installs one waiting receiver");
    }
    static void Finish(string failure)
    {
        SessionState.SetBool(Active, false); EditorApplication.update -= Tick;
        EditorApplication.UnlockReloadAssemblies(); AssetDatabase.AllowAutoRefresh();
        if (SessionState.GetBool(Active + ".HadSave", false)) PlayerPrefs.SetString(SaveKey, SessionState.GetString(Active + ".Save", ""));
        else PlayerPrefs.DeleteKey(SaveKey);
        PlayerPrefs.Save();
        File.WriteAllText(Path.Combine(Output, "result.txt"), failure == null ? "PASS\n" + checks.Count + " checks" : "FAIL\n" + failure);
        EditorApplication.Exit(failure == null ? 0 : 1);
    }
}
