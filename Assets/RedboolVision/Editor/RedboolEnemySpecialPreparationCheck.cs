using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Isolated play-mode regression for enemy special cancellation; no camera or remote player.</summary>
[InitializeOnLoad]
public static class RedboolEnemySpecialPreparationCheck
{
    const string Active = "RedboolEnemySpecialPreparationCheck.Active", SaveKey = "VisionStriker.Run";
    static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly List<string> checks = new List<string>();
    static IEnumerator flow;
    static int frame = -1;
    static EnemySpecial spell;
    static EnemyAI ai;
    static Fighter foe, hero;
    static SpecialMove heroSpecial;
    static string Output => Environment.GetEnvironmentVariable("REDBOOL_ENEMY_PREPARATION_CHECK_OUTPUT");

    static RedboolEnemySpecialPreparationCheck() { if (SessionState.GetBool(Active, false)) Subscribe(); }
    public static void Run()
    {
        if (!Application.isBatchMode || string.IsNullOrEmpty(Output)) throw new Exception("Isolated batch output required");
        Directory.CreateDirectory(Output);
        SessionState.SetBool(Active + ".HadSave", PlayerPrefs.HasKey(SaveKey));
        SessionState.SetString(Active + ".Save", PlayerPrefs.GetString(SaveKey, ""));
        SessionState.SetFloat(Active + ".StartupStart", (float)EditorApplication.timeSinceStartup);
        SessionState.SetBool(Active + ".PlayStarted", false);
        File.WriteAllText(Path.Combine(Output, "phase.txt"), "Opening scene; waiting for Play");
        EditorSceneManager.OpenScene("Assets/Scenes/ScoutPass.unity");
        SessionState.SetBool(Active, true); Subscribe();
        EditorApplication.delayCall += () => EditorApplication.EnterPlaymode();
    }
    static void Subscribe() { EditorApplication.update -= Tick; EditorApplication.update += Tick; }
    static void Tick()
    {
        if (!Application.isPlaying || EditorApplication.isCompiling)
        {
            if (!SessionState.GetBool(Active + ".PlayStarted", false) &&
                EditorApplication.timeSinceStartup - SessionState.GetFloat(Active + ".StartupStart", 0) > 300)
                Finish("Startup timeout before Play; no gameplay assertions ran");
            return;
        }
        if (!SessionState.GetBool(Active + ".PlayStarted", false))
        {
            SessionState.SetBool(Active + ".PlayStarted", true);
            SessionState.SetFloat(Active + ".PlayStart", (float)EditorApplication.timeSinceStartup);
            File.WriteAllText(Path.Combine(Output, "phase.txt"), "Play started; exercising actual enemy casts");
        }
        if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Active + ".PlayStart", 0) > 240) { Finish("Runtime timeout in Play"); return; }
        EditorApplication.isPaused = false; EditorApplication.QueuePlayerLoopUpdate();
        if (frame == Time.frameCount) return;
        frame = Time.frameCount;
        try { if (flow == null) flow = Tests(); if (!flow.MoveNext()) Finish(null); }
        catch (Exception error) { Finish(error.ToString()); }
    }
    static void Check(bool ok, string label)
    {
        checks.Add((ok ? "PASS " : "FAIL ") + label);
        File.WriteAllLines(Path.Combine(Output, "checks.txt"), checks);
        if (!ok) throw new Exception(label);
    }
    static object Get(object target, string field) => target.GetType().GetField(field, Private).GetValue(target);
    static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
    static object Call(object target, string name, params object[] arguments) => target.GetType().GetMethod(name, Private).Invoke(target, arguments);
    static List<Func<bool>> Updaters => (List<Func<bool>>)Get(spell, "updaters");
    static void Pvp(bool active) => typeof(RedboolPvp).GetField("<Active>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, active);
    static void ResetFixture()
    {
        Pvp(false); Time.timeScale = 1; Time.fixedDeltaTime = .02f;
        spell.CancelForPreparation(); spell.ResetForRound();
        foe.Health.ResetHealth(); hero.Health.ResetHealth();
        foe.Health.DamageLocked = hero.Health.DamageLocked = false;
        foe.InputLocked = hero.InputLocked = false;
        foe.SetVisionPreparationLock(false); hero.SetVisionPreparationLock(false);
        foe.EndSpecial(); hero.EndSpecial(); foe.ReleaseGuard(); hero.ReleaseGuard();
        ai.enabled = true; spell.enabled = true;
        // Keep ordinary AI decisions out of this fixture while the real special Update remains enabled.
        Set(ai, "target", null); Call(ai, "CancelPendingAttack");
        foe.transform.position = hero.transform.position + Vector3.right;
        var body = foe.GetComponent<Rigidbody2D>(); body.position = foe.transform.position; body.linearVelocity = Vector2.zero;
        hero.GetComponent<Rigidbody2D>().linearVelocity = Vector2.zero;
        heroSpecial.CancelForPause(); heroSpecial.ResetCharge(); heroSpecial.AddCharge(3);
        Set(spell, "fireDelay", 0f);
        Call(spell, "FindHero");
    }
    static IEnumerator Reach(EnemySpecial.Phase target)
    {
        ResetFixture(); spell.AddCharge(spell.HitsToCharge);
        double until = Time.realtimeSinceStartupAsDouble + 12;
        while (spell.CurrentPhase != target)
        {
            if (Time.realtimeSinceStartupAsDouble > until) throw new Exception("Did not reach real spell phase " + target + "; current=" + spell.CurrentPhase);
            yield return null;
        }
        Check(spell.IsPlaying && Get(spell, "routine") != null, target + " reached through actual Run coroutine");
    }
    static IEnumerator Tests()
    {
        yield return null; yield return null;
        RedboolBattlePreparation.SetKeyboardTest(true);
        double warmBy = Time.realtimeSinceStartupAsDouble + 40;
        while (true)
        {
            bool warming = false;
            foreach (var gate in Object.FindObjectsByType<RedboolBattlePreparation>(FindObjectsSortMode.None)) warming |= gate.EffectsPreparing;
            if (!warming) break;
            if (Time.realtimeSinceStartupAsDouble > warmBy) throw new Exception("Combat effect preparation did not finish");
            yield return null;
        }
        foreach (var receiver in Object.FindObjectsByType<RedboolDemoReceiver>(FindObjectsSortMode.None)) receiver.enabled = false;
        foreach (var receiver in Object.FindObjectsByType<UdpActionReceiver>(FindObjectsSortMode.None)) receiver.enabled = false;
        // The fixture owns preparation locks after warmup; keyboard mode would otherwise clear them every Update.
        foreach (var gate in Object.FindObjectsByType<RedboolBattlePreparation>(FindObjectsSortMode.None)) gate.enabled = false;
        ai = Object.FindAnyObjectByType<EnemyAI>();
        hero = Object.FindAnyObjectByType<PlayerController>().GetComponent<Fighter>();
        foe = ai.GetComponent<Fighter>(); spell = ai.GetComponent<EnemySpecial>(); heroSpecial = hero.GetComponent<SpecialMove>();
        Check(spell != null && spell.Available && heroSpecial != null, "Real ScoutPass enemy spell and player meter available");
        foreach (var phase in new[] { EnemySpecial.Phase.Retreat, EnemySpecial.Phase.CutIn, EnemySpecial.Phase.Gather, EnemySpecial.Phase.Acting })
        {
            var start = Reach(phase); while (start.MoveNext()) yield return null;
            int calls = 0, hp = hero.Health.CurrentHealth, charge = heroSpecial.Charge;
            Updaters.Add(() => { calls++; return false; });
            foe.GetComponent<Rigidbody2D>().linearVelocity = new Vector2(4f, 0f);
            // Exercise either participant's preparation lock, before the updater queue is drained.
            if (phase == EnemySpecial.Phase.Retreat || phase == EnemySpecial.Phase.Gather) hero.SetVisionPreparationLock(true);
            else foe.SetVisionPreparationLock(true);
            Call(spell, "Update");
            Check(calls == 0 && Updaters.Count == 0, phase + " preparation cancels before side-effect updaters");
            Check(!spell.IsPlaying && !foe.IsSpecialActive && Get(spell, "routine") == null, phase + " cancels coroutine and fighter ownership");
            Check(Mathf.Abs(foe.GetComponent<Rigidbody2D>().linearVelocity.x) < .0001f, phase + " cancels retreat horizontal velocity");
            Check(spell.Charge == (phase == EnemySpecial.Phase.Acting ? 0 : spell.HitsToCharge), phase + " preserves correct pre/post release charge");
            Check(spell.Used == (phase == EnemySpecial.Phase.Acting), phase + " preserves one-use semantics");
            Check(!((bool)Get(spell, "ownsTime")) && Mathf.Abs(Time.timeScale - 1f) < .0001f, phase + " restores only its own slow time");
            for (int i = 0; i < 4; i++) yield return null;
            Check(calls == 0 && hero.Health.CurrentHealth == hp && heroSpecial.Charge == charge && !hero.IsAttackSealed, phase + " has no delayed damage, meter drain or attack seal");
            bool struck = (bool)Call(spell, "Strike", 10, Vector2.zero, 0f, 0f, 1.15f, float.NaN, 1.5f);
            Check(!struck && hero.Health.CurrentHealth == hp, phase + " direct strike rejects preparation lock");
        }

        // Every cleanup route must handle fades queued by a naturally finished cast.
        foreach (string cleanup in new[] { "preparation", "round reset", "component disable" })
        {
            var acting = Reach(EnemySpecial.Phase.Acting); while (acting.MoveNext()) yield return null;
            double finishBy = Time.realtimeSinceStartupAsDouble + 12;
            while (spell.IsPlaying) { if (Time.realtimeSinceStartupAsDouble > finishBy) throw new Exception("Real spell did not settle"); yield return null; }
            Check(spell.Used && Updaters.Count > 0, cleanup + " reaches real Idle with unfinished fades");
            int idleCalls = 0, idleCharge = spell.Charge; Updaters.Add(() => { idleCalls++; return false; });
            if (cleanup == "preparation") { hero.SetVisionPreparationLock(true); Call(spell, "Update"); }
            else if (cleanup == "round reset") spell.ResetForRound();
            else spell.enabled = false;
            Check(Updaters.Count == 0 && idleCalls == 0 && !(bool)Get(spell, "lightingCaptured"), cleanup + " removes Idle callbacks and lighting ownership");
            Check(Get(spell, "root") == null && ((IList)Get(spell, "extras")).Count == 0, cleanup + " drops Idle owned effects");
            Check(spell.Charge == (cleanup == "round reset" ? 0 : idleCharge) && spell.Used == (cleanup != "round reset"), cleanup + " preserves intended charge and use semantics");
            for (int i = 0; i < 4; i++) yield return null;
            Check(idleCalls == 0 && Updaters.Count == 0, cleanup + " has no surviving late callback");
        }

        foreach (string owner in new[] { "disabled AI", "KO", "PvP" })
        {
            var cutIn = Reach(EnemySpecial.Phase.CutIn); while (cutIn.MoveNext()) yield return null;
            if (owner == "disabled AI") ai.enabled = false;
            else if (owner == "KO") { foe.InputLocked = true; hero.SetVisionPreparationLock(true); Time.timeScale = .15f; Time.fixedDeltaTime = .003f; }
            else { Pvp(true); Time.timeScale = 0; Time.fixedDeltaTime = .02f; }
            Call(spell, "Update");
            Check(!spell.IsPlaying && !foe.IsSpecialActive, owner + " cancels an active cast");
            float expected = owner == "KO" ? .15f : owner == "PvP" ? 0f : 1f;
            Check(Mathf.Abs(Time.timeScale - expected) < .0001f, owner + " preserves current time owner");
            if (owner == "KO") Check(Mathf.Abs(Time.fixedDeltaTime - .003f) < .0001f, "KO fixed timestep remains owned by KO");
            if (owner == "PvP") Check(Mathf.Abs(Time.fixedDeltaTime - .02f) < .0001f, "PvP freeze timestep is not overwritten");
            Pvp(false); Time.timeScale = 1; Time.fixedDeltaTime = .02f;
        }

        foreach (string cleanup in new[] { "round reset", "component disable" })
        foreach (string owner in new[] { "KO", "PvP" })
        {
            var cutIn = Reach(EnemySpecial.Phase.CutIn); while (cutIn.MoveNext()) yield return null;
            if (owner == "KO") { foe.InputLocked = true; Time.timeScale = .15f; Time.fixedDeltaTime = .003f; }
            else { Pvp(true); Time.timeScale = 0; Time.fixedDeltaTime = .02f; }
            if (cleanup == "round reset") spell.ResetForRound(); else spell.enabled = false;
            Check(!spell.IsPlaying && Get(spell, "routine") == null && Updaters.Count == 0, cleanup + " interrupts active CutIn under " + owner);
            Check(Mathf.Abs(Time.timeScale - (owner == "KO" ? .15f : 0f)) < .0001f &&
                Mathf.Abs(Time.fixedDeltaTime - (owner == "KO" ? .003f : .02f)) < .0001f, cleanup + " preserves newer " + owner + " time ownership");
            Check(spell.Charge == 0 && !spell.Used, cleanup + " does not refund interrupted charge");
            Pvp(false); Time.timeScale = 1; Time.fixedDeltaTime = .02f;
        }

        ResetFixture(); hero.SealAttacks(60f);
        Check(hero.IsAttackSealed && !hero.TryAttack(CombatMove.Jab) && !hero.BeginSpecial(), "Attack seal rejects attacks and specials");
        Check(hero.TryGuard(false), "Attack seal still permits guard"); hero.ReleaseGuard();
        hero.SetVisionPreparationLock(true); spell.CancelForPreparation();
        Check(hero.IsAttackSealed, "Camera preparation does not erase an existing attack seal");
        hero.Health.ResetHealth(); hero.SetVisionPreparationLock(false);
        Check(!hero.IsAttackSealed && hero.TryAttack(CombatMove.Jab), "Round health reset clears seal and permits next attack"); hero.CancelAttack();
        Check(PlayerPrefs.GetString(SaveKey, "") == SessionState.GetString(Active + ".Save", ""), "Campaign save unchanged");
    }
    static void Finish(string failure)
    {
        SessionState.SetBool(Active, false); EditorApplication.update -= Tick;
        Pvp(false); Time.timeScale = 1; Time.fixedDeltaTime = .02f;
        if (SessionState.GetBool(Active + ".HadSave", false)) PlayerPrefs.SetString(SaveKey, SessionState.GetString(Active + ".Save", ""));
        else PlayerPrefs.DeleteKey(SaveKey);
        PlayerPrefs.Save();
        Directory.CreateDirectory(Output);
        File.WriteAllText(Path.Combine(Output, "result.txt"), failure == null ? "PASS\n" + checks.Count + " checks" : "FAIL\n" + failure);
        EditorApplication.Exit(failure == null ? 0 : 1);
    }
}
