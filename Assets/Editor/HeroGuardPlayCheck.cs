using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>
/// 전투 씬을 실제로 재생해 주인공의 얼굴 막기(하이가드)를 확인한다 (batchmode 전용).
/// 방어를 누르고 있는 동안 자세가 유지되는지, 막힌 타격에 반응한 뒤 다시 유지로 돌아오는지, 떼면 기본자세로 돌아오는지 보고 화면을 저장한다.
/// Unity -batchmode -executeMethod HeroGuardPlayCheck.Run -heroGuardReport &lt;폴더&gt; [-heroGuardScene &lt;씬 경로&gt;] -redbool-keyboard
/// 저장 데이터는 시작 전에 보관했다가 끝나면 되돌린다.
/// </summary>
[InitializeOnLoad]
public static class HeroGuardPlayCheck
{
    const string Active = "HeroGuardPlayCheck.Active", SaveKey = "VisionStriker.Run";
    static IEnumerator flow;
    static int lastFrame = -1, passed, total;
    static readonly List<string> lines = new List<string>();
    static HeroGuardPlayCheck() { if (SessionState.GetBool(Active, false)) Subscribe(); }
    static void Subscribe() { EditorApplication.update -= Tick; EditorApplication.update += Tick; }

    static string Arg(string name, string fallback)
    {
        var args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
    }
    static string Out => SessionState.GetString(Active + ".Out", ".");

    public static void Run()
    {
        if (!Application.isBatchMode) throw new Exception("Run with Unity -batchmode.");
        AssetDatabase.DisallowAutoRefresh(); EditorApplication.LockReloadAssemblies();
        string outDir = Arg("-heroGuardReport", Path.GetFullPath("Logs/HeroGuard")); Directory.CreateDirectory(outDir);
        SessionState.SetString(Active + ".Out", outDir);
        SessionState.SetBool(Active + ".HadSave", PlayerPrefs.HasKey(SaveKey));
        SessionState.SetString(Active + ".Save", PlayerPrefs.GetString(SaveKey, ""));
        SessionState.SetFloat(Active + ".Start", (float)EditorApplication.timeSinceStartup);
        EditorSceneManager.OpenScene(Arg("-heroGuardScene", "Assets/Scenes/StoryStages/SealedGate.unity"));
        SessionState.SetBool(Active, true); Subscribe();
        EditorApplication.delayCall += () => EditorApplication.EnterPlaymode();
    }

    static void Tick()
    {
        if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Active + ".Start", 0) > 240) { Finish("Timeout"); return; }
        if (!Application.isPlaying) return;
        EditorApplication.isPaused = false; EditorApplication.QueuePlayerLoopUpdate();
        if (lastFrame == Time.frameCount) return;
        lastFrame = Time.frameCount;
        try { if (flow == null) flow = Flow(); if (!flow.MoveNext()) Finish(null); }
        catch (Exception e) { Finish(e.ToString()); }
    }

    static IEnumerator Wait(float seconds)
    {
        float until = Time.time + seconds;
        while (Time.time < until) yield return null;
    }
    static void Check(bool ok, string what) { total++; if (ok) passed++; lines.Add((ok ? "PASS " : "FAIL ") + what); Debug.Log("[HeroGuardCheck] " + (ok ? "PASS " : "FAIL ") + what); }
    static void Note(string what) { lines.Add("     " + what); Debug.Log("[HeroGuardCheck] " + what); }

    static IEnumerator Flow()
    {
        yield return null; yield return null;
        var wait = Wait(1.5f); while (wait.MoveNext()) yield return null;
        var player = Object.FindAnyObjectByType<PlayerController>(); var enemy = Object.FindAnyObjectByType<EnemyAI>();
        if (player == null || enemy == null) throw new Exception("fighters not found");
        // 입력과 상대 AI를 끄고 Fighter를 직접 움직인다. 장면 전환도 막는다.
        player.enabled = false; enemy.enabled = false;
        foreach (var udp in Object.FindObjectsByType<UdpActionReceiver>(FindObjectsSortMode.None)) udp.enabled = false;
        foreach (var bridge in Object.FindObjectsByType<BattleSceneBridge>(FindObjectsSortMode.None)) Object.Destroy(bridge);
        Place(player.gameObject, -.5f); Place(enemy.gameObject, .5f);
        wait = Wait(2.5f); while (wait.MoveNext()) yield return null;

        var hero = player.GetComponent<Fighter>(); var foe = enemy.GetComponent<Fighter>();
        var view = player.GetComponent<Fighter3DView>(); var hp = player.GetComponent<Health>();
        var anim = view.modelAnimator;
        var cam = Camera.main; var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
        Note("scene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name + " controller=" + anim.runtimeAnimatorController.name + " visual=" + view.visualRoot.name);
        Check(anim.runtimeAnimatorController.name == "Hero_ApprovedMovement", "주인공이 Hero_ApprovedMovement 컨트롤러를 쓴다");
        Check(view.CurrentState == "IDLE" && hero.CanAct, "시작: 기본자세, 조작 가능 (state=" + view.CurrentState + ")");
        float idleL = HandAboveHead(anim, true), idleR = HandAboveHead(anim, false);
        Shot(cam, rt, "0_idle");

        // 방어를 누른다 → 팔이 올라가 유지로 넘어간다
        Check(hero.TryGuard(false), "방어 시작");
        wait = Wait(.12f); while (wait.MoveNext()) yield return null;
        Check(view.CurrentState == "GUARD", "누른 직후: 올리기(GUARD) (state=" + view.CurrentState + ")");
        Shot(cam, rt, "1_raise");
        wait = Wait(.5f); while (wait.MoveNext()) yield return null;
        Check(view.CurrentState == "GUARD_HOLD" && hero.IsGuarding, "0.6초 뒤: 유지(GUARD_HOLD) (state=" + view.CurrentState + ")");
        Check(Playing(anim) == HeroGuardBaker.HoldClip, "유지 상태가 주인공 하이가드 클립을 재생 (clip=" + Playing(anim) + ")");
        float holdL = HandAboveHead(anim, true), holdR = HandAboveHead(anim, false);
        Note($"손목 높이 - 목 위 관절 높이 (m): 기본자세 L {idleL:F3} R {idleR:F3} → 막기 L {holdL:F3} R {holdR:F3}");
        Check(holdL > 0 && holdR > 0 && holdL > idleL + .03f && holdR > idleR + .03f, "두 손이 얼굴 높이로 올라가 있다");
        Shot(cam, rt, "2_hold");

        // 계속 누르고 있으면 그대로 유지된다
        wait = Wait(2.5f); while (wait.MoveNext()) yield return null;
        Check(view.CurrentState == "GUARD_HOLD" && hero.IsGuarding, "3초 넘게 누르고 있어도 유지 (state=" + view.CurrentState + ")");
        Check(HandAboveHead(anim, true) > 0 && HandAboveHead(anim, false) > 0, "유지 중에도 두 손이 얼굴 높이");
        Shot(cam, rt, "3_hold_3s");

        // 막힌 타격 → 반응 후 다시 유지
        hp.TakeDamage(1, Vector2.zero, true); hp.ClearStun(); yield return null;
        Check(view.CurrentState == "GUARD_HIT", "막힌 타격에 반응(GUARD_HIT) (state=" + view.CurrentState + ")");
        wait = Wait(.07f); while (wait.MoveNext()) yield return null;
        Shot(cam, rt, "4_hit");
        wait = Wait(.5f); while (wait.MoveNext()) yield return null;
        Check(view.CurrentState == "GUARD_HOLD" && hero.IsGuarding, "반응 뒤 다시 유지 (state=" + view.CurrentState + ")");

        // 상대의 잽을 실제로 받아 본다
        int before = hp.CurrentHealth; bool reacted = false;
        bool jabbed = foe.TryAttack(CombatMove.Jab);
        float until = Time.time + .9f; bool shot = false; float jabAt = Time.time;
        while (Time.time < until)
        {
            if (view.CurrentState == "GUARD_HIT") reacted = true;
            if (!shot && Time.time - jabAt >= Fighter3DView.Impact(CombatMove.Jab) + .02f) { Shot(cam, rt, "5_enemy_jab"); shot = true; }
            yield return null;
        }
        Note($"상대 잽: 시작 {jabbed}, 막기 반응 {reacted}, 체력 {before} → {hp.CurrentHealth}");
        Check(hero.IsGuarding && view.CurrentState == "GUARD_HOLD", "상대 잽 뒤에도 유지 (state=" + view.CurrentState + ")");

        // 뗀다 → 풀기 → 기본자세
        hero.ReleaseGuard(); yield return null; yield return null;
        Check(view.CurrentState == "GUARD_RELEASE", "떼면 풀기(GUARD_RELEASE) (state=" + view.CurrentState + ")");
        wait = Wait(.1f); while (wait.MoveNext()) yield return null;
        Shot(cam, rt, "6_release");
        until = Time.time + 1.5f; while (Time.time < until && view.CurrentState != "IDLE") yield return null;
        Check(view.CurrentState == "IDLE" && !hero.IsGuarding, "기본자세로 돌아온다 (state=" + view.CurrentState + ")");
        wait = Wait(.4f); while (wait.MoveNext()) yield return null;
        Check(HandAboveHead(anim, false) < holdR - .03f, "풀린 뒤 손이 다시 내려온다");
        Shot(cam, rt, "7_idle_again");

        // 다시 눌러도 같은 흐름
        Check(hero.TryGuard(false), "다시 방어 시작");
        wait = Wait(.6f); while (wait.MoveNext()) yield return null;
        Check(view.CurrentState == "GUARD_HOLD", "다시 유지 (state=" + view.CurrentState + ")");

        // ── 로우킥 막기: 누르고만 있으면 상대 공격에 맞춰 자세가 바뀐다 ──
        var footL = anim.GetBoneTransform(HumanBodyBones.LeftFoot); var footR = anim.GetBoneTransform(HumanBodyBones.RightFoot);
        float liftHigh = footL.position.y - footR.position.y;
        foe.DeclareIntent(CombatMove.LowKick);
        wait = Wait(.35f); while (wait.MoveNext()) yield return null;
        Check(view.CurrentState == "GUARD_LOWKICK_HOLD" && hero.IsCrouching, "막는 중에 상대가 로우킥을 예고하면 로우킥 막기로 바뀐다 (state=" + view.CurrentState + ")");
        Check(Playing(anim) == HeroGuardBaker.LowHoldClip, "로우킥 막기 유지가 주인공 무에타이 클립을 재생 (clip=" + Playing(anim) + ")");
        float liftLow = footL.position.y - footR.position.y;
        Note($"앞발 높이 - 뒷발 높이 (m): 얼굴 막기 {liftHigh:F3} → 로우킥 막기 {liftLow:F3}");
        Check(liftLow > liftHigh + .25f, "앞다리를 들어 정강이로 받는 자세");
        Check(HandAboveHead(anim, true) > idleL && HandAboveHead(anim, false) > idleR, "로우킥 막기 중에도 두 손은 얼굴 옆에 올라가 있다");
        Shot(cam, rt, "8_low_hold");
        hp.TakeDamage(1, Vector2.zero, true); hp.ClearStun(); yield return null;
        Check(view.CurrentState == "GUARD_LOWKICK_HIT", "로우킥이 막히면 반응(GUARD_LOWKICK_HIT) (state=" + view.CurrentState + ")");
        wait = Wait(.07f); while (wait.MoveNext()) yield return null;
        Shot(cam, rt, "9_low_hit");
        wait = Wait(.45f); while (wait.MoveNext()) yield return null;
        Check(view.CurrentState == "GUARD_LOWKICK_HOLD", "반응 뒤 다시 로우킥 막기 유지 (state=" + view.CurrentState + ")");

        foe.DeclareIntent(CombatMove.Jab);
        wait = Wait(.3f); while (wait.MoveNext()) yield return null;
        Check(view.CurrentState == "GUARD_HOLD" && !hero.IsCrouching, "상대가 잽을 예고하면 얼굴 막기로 바뀐다 (state=" + view.CurrentState + ")");
        foe.DeclareIntent(CombatMove.LowKick);
        wait = Wait(.3f); while (wait.MoveNext()) yield return null;
        Check(view.CurrentState == "GUARD_LOWKICK_HOLD", "다시 로우킥을 예고하면 로우킥 막기로 바뀐다 (state=" + view.CurrentState + ")");
        foe.ClearIntent();
        wait = Wait(Fighter.LowGuardLinger + .4f); while (wait.MoveNext()) yield return null;
        Check(view.CurrentState == "GUARD_HOLD" && !hero.IsCrouching && hero.IsGuarding, "로우킥이 끝나면 잠시 뒤 얼굴 막기로 돌아온다 (state=" + view.CurrentState + ")");
        Check(footL.position.y - footR.position.y < liftHigh + .08f, "든 다리가 다시 내려온다");

        // 상대의 실제 로우킥
        int hpBefore = hp.CurrentHealth; bool wentLow = false, lowReact = false;
        bool kicked = foe.TryAttack(CombatMove.LowKick); float kickAt = Time.time; shot = false;
        until = Time.time + Fighter3DView.Duration(CombatMove.LowKick) + 1f;
        while (Time.time < until)
        {
            if (view.CurrentState.StartsWith("GUARD_LOWKICK")) wentLow = true;
            if (view.CurrentState == "GUARD_LOWKICK_HIT") lowReact = true;
            if (!shot && Time.time - kickAt >= Fighter3DView.Impact(CombatMove.LowKick) + .03f) { Shot(cam, rt, "10_enemy_lowkick"); shot = true; }
            yield return null;
        }
        Note($"상대 로우킥: 시작 {kicked}, 로우킥 막기로 전환 {wentLow}, 막기 반응 {lowReact}, 체력 {hpBefore} → {hp.CurrentHealth}");
        Check(kicked && wentLow, "상대의 실제 로우킥에 맞춰 로우킥 막기로 바뀐다");
        Check(hp.CurrentHealth == hpBefore, "막고 있으면 로우킥 피해가 없다");
        Check(view.CurrentState == "GUARD_HOLD" && hero.IsGuarding, "로우킥이 끝난 뒤 얼굴 막기 유지로 복귀 (state=" + view.CurrentState + ")");

        // 로우킥이 예고된 상태에서 새로 누르면 처음부터 로우킥 막기로 올린다
        hero.ReleaseGuard();
        until = Time.time + 1.5f; while (Time.time < until && view.CurrentState != "IDLE") yield return null;
        foe.DeclareIntent(CombatMove.LowKick);
        Check(hero.TryGuard(false), "로우킥이 예고된 상태에서 방어 시작");
        wait = Wait(.1f); while (wait.MoveNext()) yield return null;
        Check(view.CurrentState == "GUARD_LOWKICK", "처음부터 로우킥 막기로 올린다 (state=" + view.CurrentState + ")");
        Shot(cam, rt, "11_low_raise");
        wait = Wait(.45f); while (wait.MoveNext()) yield return null;
        Check(view.CurrentState == "GUARD_LOWKICK_HOLD", "로우킥 막기 유지 (state=" + view.CurrentState + ")");
        foe.ClearIntent(); hero.ReleaseGuard();
        until = Time.time + 1.5f; while (Time.time < until && view.CurrentState != "IDLE") yield return null;
        Check(view.CurrentState == "IDLE" && !hero.IsGuarding, "떼면 기본자세로 돌아온다 (state=" + view.CurrentState + ")");

        // ── 필살기(파운장)를 막았을 때: 약하게만 밀린다 ──
        var special = player.GetComponent<SpecialMove>(); var foeHp = enemy.GetComponent<Health>(); var foeBody = enemy.GetComponent<Rigidbody2D>();
        Place(player.gameObject, -1.2f); Place(enemy.gameObject, 1.2f);
        wait = Wait(.8f); while (wait.MoveNext()) yield return null;
        special.ResetCharge(); special.AddCharge(special.HitsToCharge);
        Check(foe.TryGuard(false), "상대가 방어를 올린다");
        wait = Wait(.4f); while (wait.MoveNext()) yield return null;
        float x0 = foeBody.position.x, y0 = foeBody.position.y; int foeHpBefore = foeHp.CurrentHealth;
        Check(special.Activate(), "파운장 발동");
        until = Time.time + 5f; while (Time.time < until && special.LastResult == SpecialMove.Result.None) yield return null;
        Check(special.LastResult == SpecialMove.Result.Blocked, "상대가 파운장을 막았다 (result=" + special.LastResult + ")");
        // 막은 쪽 경직(GuardReactionDuration)이 끝나면 상대 AI가 다시 움직임을 잡는다. 실제로 밀리는 것은 그때까지다.
        float hop = 0f; until = Time.time + Fighter3DView.GuardReactionDuration;
        while (Time.time < until) { hop = Mathf.Max(hop, foeBody.position.y - y0); yield return null; }
        float pushed = foeBody.position.x - x0;
        foeBody.linearVelocity = new Vector2(0f, foeBody.linearVelocity.y);
        Shot(cam, rt, "12_special_blocked");
        Note($"파운장을 막았을 때: 경직 {Fighter3DView.GuardReactionDuration:F3}초 동안 {pushed:F3} m 밀림, 뜬 높이 {hop:F3} m, 체력 {foeHpBefore} → {foeHp.CurrentHealth}");
        Check(pushed > .08f && pushed < .35f, "막았을 때는 약하게만 밀린다 (0.08~0.35 m)");
        Check(hop < .02f, "막았을 때 뜨지 않는다");
        rt.Release();
    }

    static string Playing(Animator anim)
    {
        var info = anim.GetCurrentAnimatorClipInfo(0);
        return info.Length > 0 && info[0].clip != null ? info[0].clip.name : "none";
    }
    /// <summary>손목이 목 위 관절(머리 뼈 시작점)보다 얼마나 높은지 (m).</summary>
    static float HandAboveHead(Animator anim, bool left)
    {
        var hand = anim.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand); var head = anim.GetBoneTransform(HumanBodyBones.Head);
        return hand.position.y - head.position.y;
    }
    static void Place(GameObject fighter, float x)
    {
        var p = fighter.transform.position; p.x = x; fighter.transform.position = p;
        var body = fighter.GetComponent<Rigidbody2D>(); if (body != null) { body.position = new Vector2(x, body.position.y); body.linearVelocity = Vector2.zero; }
    }
    static void Shot(Camera cam, RenderTexture rt, string name)
    {
        RenderPipeline.SubmitRenderRequest(cam, new RenderPipeline.StandardRequest { destination = rt });
        var img = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        RenderTexture.active = rt; img.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); img.Apply(); RenderTexture.active = null;
        File.WriteAllBytes(Path.Combine(Out, "play_" + name + ".jpg"), img.EncodeToJPG(90)); Object.Destroy(img);
    }

    static void Finish(string failure)
    {
        SessionState.SetBool(Active, false); EditorApplication.update -= Tick;
        EditorApplication.UnlockReloadAssemblies(); AssetDatabase.AllowAutoRefresh();
        if (SessionState.GetBool(Active + ".HadSave", false)) PlayerPrefs.SetString(SaveKey, SessionState.GetString(Active + ".Save", ""));
        else PlayerPrefs.DeleteKey(SaveKey);
        PlayerPrefs.Save();
        if (failure != null) lines.Add("ERROR " + failure);
        bool ok = failure == null && passed == total && total > 0;
        File.WriteAllLines(Path.Combine(Out, "play_report.txt"), lines);
        File.WriteAllText(Path.Combine(Out, "play_done.txt"), (ok ? "PASS " : "FAIL ") + passed + "/" + total + (failure != null ? " " + failure.Split('\n')[0] : ""));
        EditorApplication.Exit(ok ? 0 : 1);
    }
}
