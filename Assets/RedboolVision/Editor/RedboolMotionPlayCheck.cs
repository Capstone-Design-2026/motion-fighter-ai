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
    static RedboolBattlePreparation walkingGate;
    static string walkingTracking = "SYNTHETIC";
    static readonly List<string> checks = new List<string>();
    static string Output => Environment.GetEnvironmentVariable("REDBOOL_MOTION_CHECK_OUTPUT") ?? Path.GetFullPath(Path.Combine(Application.dataPath, "../../persistent_calibration_20261105/native_play"));
    static RedboolMotionPlayCheck() { if (SessionState.GetBool(Active, false)) Subscribe(); }
    public static void Run()
    {
        if (!Application.isBatchMode) throw new Exception("Run this isolated smoke check with Unity -batchmode.");
        RedboolHeroAssetsCheck.Run();
        AssetDatabase.DisallowAutoRefresh(); EditorApplication.LockReloadAssemblies();
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
        if (!Application.isPlaying) return; EditorApplication.isPaused = false; // batch: ignore Error Pause from unrelated editor errors
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
        while (Time.realtimeSinceStartupAsDouble < until) {
            if(walkingGate!=null) walkingGate.Observe(true,walkingTracking,"READY",false,"NEUTRAL");
            yield return null;
        }
    }
    static void Shot(string name)
    {
        var cam=Camera.main; if(cam==null)return;
        var rt=new RenderTexture(1280,720,24);
        UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam,new UnityEngine.Rendering.RenderPipeline.StandardRequest{destination=rt});
        var old=RenderTexture.active; RenderTexture.active=rt;
        var image=new Texture2D(1280,720,TextureFormat.RGB24,false);
        image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();
        File.WriteAllBytes(Path.Combine(Output,name+".png"),image.EncodeToPNG());
        RenderTexture.active=old;Object.Destroy(image);rt.Release();Object.Destroy(rt);
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
        var heroView=player.GetComponent<Fighter3DView>();
        Check(heroView.visualRoot.name.StartsWith("Hero02") && heroView.modelAnimator.isHuman,
            "Upstream Hero02 is the playable humanoid");
        Check(fighter.Contacts.Ready,"Hero02 attack and hurt-volume bones resolve");
        int hp = fighter.Health.CurrentHealth; float seconds = round.RemainingSeconds;
        if (gate.EffectsPreparing) {
            gate.Observe(true, "SYNTHETIC", "READY", false, "NEUTRAL");
            Check(!gate.AllowsPlayer && fighter.VisionPreparationLocked, "Effect preparation holds even calibrated input");
            gate.ResetPreparation();
        }
        for (var wait = Wait(.6f); wait.MoveNext();) yield return null;
        while (gate.EffectsPreparing) yield return null;
        Check(Camera.main == null || Camera.main.targetTexture == null, "Warmup restores camera target");
        Check(GameObject.Find("Effect warmup sample") == null && specialCharge(player) == 0, "Warmup leaves no effect samples or charge");
        Shot("hero_waiting");
        Check(!gate.AllowsPlayer && fighter.VisionPreparationLocked, "No camera holds player");
        Check(fighter.Facing == (enemy.transform.position.x > player.transform.position.x ? 1 : -1)
            && enemy.GetComponent<Fighter>().Facing == -fighter.Facing, "Both fighters face each other before calibration");
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
        while (gate.EffectsPreparing) yield return null;
        Check(!gate.AllowsPlayer, "New scene waits for a fresh packet");
        gate.Observe(true, "VALID", "ACTIVE", false, "LEFT");
        Check(gate.AllowsPlayer, "Next scene accepts preserved calibration without neutral pose or timer");
        Check(PlayerPrefs.GetString(SaveKey, "") == SessionState.GetString(Active + ".Save", ""), "Saved progress unchanged");
        gate.Observe(true, "SYNTHETIC", "READY", false, "NEUTRAL");
        player = Object.FindAnyObjectByType<PlayerController>();
        fighter = player.GetComponent<Fighter>();
        player.useKeyboard = false;
        var body = player.GetComponent<Rigidbody2D>();
        var view = player.GetComponent<Fighter3DView>();
        Check(RedboolDemoReceiver.WalkStrength(0) == 0 && RedboolDemoReceiver.WalkStrength(float.NaN) == 0,
            "Neutral and invalid strengths cannot start walking");
        Check(Mathf.Abs(RedboolDemoReceiver.WalkStrength(.35f)-.65f)<.001f && RedboolDemoReceiver.WalkStrength(1)==1,
            "Light lean walks at 65 percent, full lean remains bounded");
        // Keep each rehearsal away from the opponent/walls; preserve real physics and animation.
        walkingGate=gate;
        var walkingReceiver=Object.FindAnyObjectByType<RedboolDemoReceiver>();
        typeof(RedboolDemoReceiver).GetField("deadline",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(walkingReceiver,long.MaxValue);
        Vector2 origin = new Vector2(0, body.position.y);
        var walkingEnemy=Object.FindAnyObjectByType<EnemyAI>();
        walkingEnemy.GetComponent<Rigidbody2D>().position = new Vector2(15,body.position.y);
        body.position=origin;Physics2D.SyncTransforms();
        foreach (int sign in new[]{-1,1})
        {
            body.position = origin; Physics2D.SyncTransforms();
            player.ExternalMovement(sign * RedboolDemoReceiver.WalkStrength(.35f));
            for (var w=Wait(.25f);w.MoveNext();) yield return null;
            Check(Mathf.Abs(body.linearVelocity.x)>.65f, "Light lean travels at useful speed " + sign + " velocity=" + body.linearVelocity.x + " locked=" + fighter.VisionPreparationLocked);
            Check(view.Walk.StepLength>.34f, "Player stride length grows with actual travel " + sign);
            foreach(string action in new[]{"JAB","STRAIGHT","HIGH_KICK","LOW_KICK"})
            {
                player.ExternalAction(action);
                Check(fighter.IsAttacking, "Held movement accepts " + action + " direction " + sign + " vx=" + body.linearVelocity.x + " attack=" + fighter.IsAttacking);
                for(var w=Wait(.15f);w.MoveNext();)yield return null;
                Check(view.CurrentState== (action=="STRAIGHT"?"CROSS":action=="HIGH_KICK"?"HIGHKICK":action=="LOW_KICK"?"LOWKICK":action),
                    "Hero02 animation follows " + action);
                if(sign==1)Shot("hero_"+action);
                for(var w=Wait(1.65f);w.MoveNext();)yield return null;
                Check(!fighter.IsAttacking && Mathf.Abs(body.linearVelocity.x)>.65f,
                    "Held direction resumes after " + action + " direction " + sign + " vx=" + body.linearVelocity.x + " attack=" + fighter.IsAttacking);
                body.position=origin;Physics2D.SyncTransforms();
            }
            player.ExternalMovement(0);
            for(var w=Wait(.2f);w.MoveNext();)yield return null;
            Check(Mathf.Abs(body.linearVelocity.x)<.01f,"Neutral stops travel " + sign);
        }

        // A new visual rig must cause actual contact damage, not only ACK an action.
        walkingEnemy.enabled=false;
        walkingTracking="VALID"; // SYNTHETIC deliberately leaves the opponent preparation-locked and immune.
        walkingEnemy.GetComponent<Health>().Configure("Hero contact verification",10000);
        player.GetComponent<Health>().Configure("Hero contact verification",10000);
        var enemyBody=walkingEnemy.GetComponent<Rigidbody2D>();
        foreach(string action in new[]{"JAB","STRAIGHT","HIGH_KICK","LOW_KICK"})
        {
            body.position=new Vector2(-.45f,body.position.y);
            enemyBody.position=new Vector2(.45f,enemyBody.position.y);
            body.linearVelocity=enemyBody.linearVelocity=Vector2.zero; Physics2D.SyncTransforms();
            for(var w=Wait(.3f);w.MoveNext();)yield return null;
            int before=walkingEnemy.GetComponent<Health>().CurrentHealth;
            player.ExternalAction(action);
            for(var w=Wait(.3f);w.MoveNext();)yield return null;
            Shot("hero_contact_"+action);
            for(var w=Wait(1.5f);w.MoveNext();)yield return null;
            Check(walkingEnemy.GetComponent<Health>().CurrentHealth<before,
                "Hero02 causes actual contact damage with "+action);
        }

        walkingGate=null;
        typeof(RedboolDemoReceiver).GetField("deadline",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(walkingReceiver,0L);
        // Optional external Python sender exercises the actual UDP socket, clocks and ACKs.
        // Only available in this isolated batch runner; never enabled in ordinary play.
        string udpDirectory = Environment.GetEnvironmentVariable("REDBOOL_UDP_CHECK_DIR");
        if (!String.IsNullOrEmpty(udpDirectory))
        {
            Directory.CreateDirectory(udpDirectory);
            RedboolDuelTest.Begin(); yield return null; yield return null;
            Check(SceneManager.GetActiveScene().name == RedboolDuelTest.ArenaScene, "Real UDP check runs in authored duel arena");
            player = Object.FindAnyObjectByType<PlayerController>();
            special = player.GetComponent<SpecialMove>(); special.ResetCharge();
            var receiver = Object.FindAnyObjectByType<RedboolDemoReceiver>();
            int appliedBefore = receiver.AttacksApplied;
            File.WriteAllText(Path.Combine(udpDirectory, "ready"), "UDP 5065 ready");
            bool charged = false;
            while (!File.Exists(Path.Combine(udpDirectory, "done")))
            {
                if (!charged && File.Exists(Path.Combine(udpDirectory, "charge.request")))
                {
                    special.AddCharge(special.HitsToCharge); charged = true;
                    File.WriteAllText(Path.Combine(udpDirectory, "charged"), "ready");
                }
                yield return null;
            }
            Check(File.ReadAllText(Path.Combine(udpDirectory, "done")).Trim() == "PASS", "Python real UDP rehearsal passed");
            Check(charged && !special.IsReady, "UDP special consumes charge");
            Check(!special.IsPlaying && !player.GetComponent<Fighter>().IsSpecialActive, "QiPalm completes and releases combat lock");
            Check(Mathf.Abs(Time.timeScale - 1f) < .001f, "QiPalm restores normal game clock");
            Check(receiver.AttacksApplied - appliedBefore == 6, "Four attacks, exactly one UDP special, and a follow-up punch applied");
        }
    }
    static int specialCharge(PlayerController player) => player.GetComponent<SpecialMove>().Charge;
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
