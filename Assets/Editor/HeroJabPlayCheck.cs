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
/// 전투 씬을 실제로 재생해 주인공의 잽·스트레이트(승인 v10 / B)을 확인한다 (batchmode 전용).
/// 잽이 판정 구간 안에서 맞는지, 닿는 거리와 헛치는 거리, 막혔을 때, 끝난 뒤 기본자세로 돌아오는지를 보고 화면을 저장한다.
/// Unity -batchmode -executeMethod HeroJabPlayCheck.Run -heroMotionReport &lt;폴더&gt; [-heroJabScene &lt;씬 경로&gt;] -redbool-keyboard
/// 저장 데이터는 시작 전에 보관했다가 끝나면 되돌린다.
/// </summary>
[InitializeOnLoad]
public static class HeroJabPlayCheck
{
    const string Active = "HeroJabPlayCheck.Active", SaveKey = "VisionStriker.Run";
    static IEnumerator flow;
    static int lastFrame = -1, passed, total;
    static readonly List<string> lines = new List<string>();
    static HeroJabPlayCheck() { if (SessionState.GetBool(Active, false)) Subscribe(); }
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
        string outDir = Arg("-heroMotionReport", Path.GetFullPath("Logs/HeroJab")); Directory.CreateDirectory(outDir);
        SessionState.SetString(Active + ".Out", outDir);
        SessionState.SetBool(Active + ".HadSave", PlayerPrefs.HasKey(SaveKey));
        SessionState.SetString(Active + ".Save", PlayerPrefs.GetString(SaveKey, ""));
        SessionState.SetFloat(Active + ".Start", (float)EditorApplication.timeSinceStartup);
        EditorSceneManager.OpenScene(Arg("-heroJabScene", "Assets/Scenes/StoryStages/SealedGate.unity"));
        SessionState.SetBool(Active, true); Subscribe();
        EditorApplication.delayCall += () => EditorApplication.EnterPlaymode();
    }

    static void Tick()
    {
        if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Active + ".Start", 0) > 300) { Finish("Timeout"); return; }
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
    static void Check(bool ok, string what) { total++; if (ok) passed++; lines.Add((ok ? "PASS " : "FAIL ") + what); Debug.Log("[HeroJabCheck] " + (ok ? "PASS " : "FAIL ") + what); }
    static void Note(string what) { lines.Add("     " + what); Debug.Log("[HeroJabCheck] " + what); }

    // 한 번의 잽 시도 결과
    static bool started, sawJab, sawSurface; static int dealt; static float hitTime, maxElbow, elbowAt, freeAt, heroDrift, footStep, footLift; static string clipAtContact, endState, foeReaction;

    static IEnumerator Jab(Fighter hero, Fighter foe, float distance, bool foeGuards, Action<float> each, CombatMove move = CombatMove.Jab)
    {
        var view = hero.GetComponent<Fighter3DView>(); var anim = view.modelAnimator; var foeHp = foe.GetComponent<Health>(); var foeView = foe.GetComponent<Fighter3DView>();
        foe.ReleaseGuard(); foeHp.ResetHealth();
        Place(hero.gameObject, -distance * .5f); Place(foe.gameObject, distance * .5f);
        var w = Wait(1.1f); while (w.MoveNext()) yield return null;
        if (foeGuards) { foe.TryGuard(false); w = Wait(.5f); while (w.MoveNext()) yield return null; }
        var foot = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
        var heroMeter = hero.GetComponent<SpecialMove>(); var foeMeter = foe.GetComponent<SpecialMove>();
        int heroCharge = heroMeter.Charge, foeCharge = foeMeter.Charge;
        int before = foeHp.CurrentHealth; float x0 = hero.transform.position.x, foot0 = foot.position.x, footY0 = foot.position.y;
        hitTime = freeAt = -1f; maxElbow = footStep = footLift = 0f; sawJab = sawSurface = false; clipAtContact = "none"; foeReaction = "";
        started = hero.TryAttack(move);
        float t0 = hero.AttackStarted, until = Time.time + 2f;
        while (Time.time < until)
        {
            float t = Time.time - t0;
            if (view.CurrentState == (move == CombatMove.Jab ? "JAB" : "CROSS")) sawJab = true;
            var surface = view.visualRoot.GetComponentInChildren<HeroPunchSurfaceVisibility>(true);
            if (surface != null && surface.CurrentSurface == (move == CombatMove.Jab ? 1 : 2)) sawSurface = true;
            float e = Elbow(anim,move); if (e > maxElbow) { maxElbow = e; elbowAt = t; }
            footStep = Mathf.Max(footStep, (foot.position.x - foot0) * hero.Facing); footLift = Mathf.Max(footLift, foot.position.y - footY0);
            if (t >= .13f && clipAtContact == "none") clipAtContact = Playing(anim);
            if (hitTime < 0f && foeHp.CurrentHealth < before) hitTime = t;
            if (foeView.CurrentState != null && foeView.CurrentState.EndsWith("_HIT")) foeReaction = foeView.CurrentState;
            else if (foeView.CurrentState == "HURT" && foeReaction == "") foeReaction = "HURT";
            each?.Invoke(t);
            if (t > .05f && freeAt < 0f && !hero.IsAttacking) freeAt = t;
            if (freeAt >= 0f && t > freeAt + .35f) break;
            yield return null;
        }
        dealt = before - foeHp.CurrentHealth; heroDrift = hero.transform.position.x - x0; endState = view.CurrentState;
        int expectedCharge = Mathf.Min(heroMeter.HitsToCharge, heroCharge + (dealt > 0 && !foeGuards ? 1 : 0));
        Check(heroMeter.Charge == expectedCharge, "막히지 않은 실제 일반 타격만 공격자 게이지 +1 (" + heroCharge + "→" + heroMeter.Charge + ", expected=" + expectedCharge + ")");
        Check(foeMeter.Charge == foeCharge, "피격·방어로 받는 쪽 게이지는 차지 않는다");
        if (foeGuards) foe.ReleaseGuard();
    }

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
        var heroMeter = player.GetComponent<SpecialMove>();
        var foeMeter = enemy.GetComponent<SpecialMove>();
        if (foeMeter == null) foeMeter = enemy.gameObject.AddComponent<SpecialMove>();
        heroMeter.ResetCharge(); foeMeter.ResetCharge();
        Check(!new SerializedObject(heroMeter).FindProperty("chargeWhenHit").boolValue && !new SerializedObject(foeMeter).FindProperty("chargeWhenHit").boolValue,
            "씬에 저장된 주인공과 새 상대 모두 피격 충전 옵션은 꺼져 있다");
        bool pvpJab = Array.IndexOf(Environment.GetCommandLineArgs(), "-redbool-pvp-jab") >= 0;
        var view = player.GetComponent<Fighter3DView>();
        if (pvpJab) {
            Check(view.ApplyVisual(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Characters/StoryOpponents/05_Cheolmun/05_Cheolmun_Visual.prefab")) && RedboolPvpJab.Apply(view), "PvP 2P와 같은 철문 모델·잽 전용 컨트롤러 적용");
            yield return null; yield return null;
        }
        var anim = view.modelAnimator;
        string expectedClip = pvpJab ? "05_Cheolmun_JAB" : HeroJabBaker.JabClip;
        var cam = Camera.main; var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
        Note("scene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name + " controller=" + anim.runtimeAnimatorController.name + " visual=" + view.visualRoot.name);
        Check(anim.runtimeAnimatorController.name == (pvpJab ? "RedboolPvpCheolmun" : "Hero_ApprovedMovement"), "검증 대상 캐릭터에 맞는 컨트롤러를 쓴다");
        AnimationClip jabClip = null;
        foreach (var c in anim.runtimeAnimatorController.animationClips) if (c != null && c.name.EndsWith("_JAB")) jabClip = c;
        Note("잽 클립: " + (jabClip ? jabClip.name + $" length={jabClip.length:F3}s fps={jabClip.frameRate:F0} human={jabClip.isHumanMotion}" : "없음"));
        Check(jabClip != null && jabClip.name == expectedClip, "컨트롤러 잽 클립이 " + expectedClip + "이다");
        Check(jabClip != null && jabClip.isHumanMotion && Mathf.Abs(jabClip.length - (pvpJab ? AssetDatabase.LoadAssetAtPath<AnimationClip>(RedboolPvpJabBake.Original).length : HeroJabBaker.Length)) < .002f, "잽 클립은 Humanoid이고 원래 길이를 유지한다");
        Check(view.CurrentState == "IDLE" && hero.CanAct, "시작: 기본자세, 조작 가능 (state=" + view.CurrentState + ")");

        // 닿는 거리: 상대 AI가 쓰는 것과 같은 방법으로 잰다(판정 구간 동안의 왼주먹 위치와 상대 몸통 판정).
        var profile = CombatReach.MeasureJab(view, player.transform);
        yield return null; yield return null;
        float maxHit = CombatReach.MaxHitDistance(profile, hero, foe);
        Note($"잽이 닿는 가장 먼 거리(몸 중심 사이): {maxHit:F3} m / 주먹 앞면이 몸 중심에서 나가는 거리: {(profile != null ? profile.forwardReach : -1f):F3} m");
        Check(maxHit > .6f && maxHit < 1.3f, "잽이 닿는 거리를 잴 수 있다");
        var window = CombatContactWorld.Window(CombatMove.Jab);
        float near = maxHit - .10f;

        // ① 60fps, 여유 있는 거리
        Time.captureFramerate = 60;
        var shots = new Queue<(float t, string name)>(new[] { (0f, "0_start"), (.067f, "1_067"), (.117f, "2_117"), (.15f, "3_150_contact"), (.20f, "4_200"), (.30f, "5_300"), (.45f, "6_450") });
        var run = Jab(hero, foe, near, false, t => { if (shots.Count > 0 && t >= shots.Peek().t) Shot(cam, rt, shots.Dequeue().name); });
        while (run.MoveNext()) yield return null;
        Note($"60fps 거리 {near:F3} m: 피해 {dealt}, 맞은 시각 {hitTime:F3}s, 왼팔 최대 펴짐 {maxElbow:F1}도 ({elbowAt:F3}s), 앞발 내딛음 {footStep:F3} m / 들림 {footLift:F3} m, 다시 움직일 수 있는 시각 {freeAt:F3}s, 재생 클립 {clipAtContact}, 상대 반응 {foeReaction}");
        Check(started && sawJab, "잽이 시작되고 JAB 상태를 재생한다");
        Check(clipAtContact == expectedClip, "뻗는 순간 재생 중인 클립이 " + expectedClip + " (clip=" + clipAtContact + ")");
        Check(dealt > 0, "잽이 맞아 피해가 들어간다");
        if (!pvpJab) Check(sawSurface, "실제 잽 재생 중 전용 보정 메시를 사용한다");
        Check(hitTime >= window.x - .001f && hitTime <= window.y + 1f / 60f + .001f, $"맞는 시각이 판정 구간({window.x:F3}~{window.y:F3}s) 안이다 ({hitTime:F3}s)");
        Check(maxElbow >= 165f, $"왼팔이 다 펴진다 (최대 {maxElbow:F1}도)");
        Check(footStep > .02f && footLift > .01f, "앞발을 들어 내딛는다");
        Check(Mathf.Abs(freeAt - Fighter3DView.Duration(CombatMove.Jab)) < .05f, $"맞혔을 때 잽 전체 시간은 그대로 {Fighter3DView.Duration(CombatMove.Jab):F2}초 ({freeAt:F3}s)");
        Check(endState == "IDLE" && hero.CanAct, "끝나면 기본자세로 돌아오고 다시 조작할 수 있다 (state=" + endState + ")");
        Check(Mathf.Abs(heroDrift) < .01f, $"잽을 쳐도 주인공 위치는 그대로다 ({heroDrift:F4} m)");

        // ② 30fps (낮은 프레임에서도 맞아야 한다)
        Time.captureFramerate = 30;
        run = Jab(hero, foe, near, false, null); while (run.MoveNext()) yield return null;
        Note($"30fps 거리 {near:F3} m: 피해 {dealt}, 맞은 시각 {hitTime:F3}s");
        Check(dealt > 0 && hitTime >= window.x - .001f && hitTime <= window.y + 1f / 30f + .001f, "30fps에서도 판정 구간 안에서 맞는다");
        Time.captureFramerate = 60;

        // ③ 닿는 한계 거리와 그 밖
        run = Jab(hero, foe, maxHit - .01f, false, null); while (run.MoveNext()) yield return null;
        Note($"한계 거리 {maxHit - .01f:F3} m: 피해 {dealt}, 맞은 시각 {hitTime:F3}s");
        Check(dealt > 0, "닿는 한계 거리 바로 안에서는 맞는다");
        run = Jab(hero, foe, maxHit + .10f, false, t => { if (t >= .15f && t < .17f) Shot(cam, rt, "7_whiff"); }); while (run.MoveNext()) yield return null;
        Note($"한계 밖 {maxHit + .10f:F3} m: 피해 {dealt}, 다시 움직일 수 있는 시각 {freeAt:F3}s");
        Check(dealt == 0, "닿는 거리 밖에서는 헛친다");
        Check(endState == "IDLE" && hero.CanAct, "헛친 뒤에도 기본자세로 돌아온다 (state=" + endState + ")");

        // ④ 상대가 막고 있을 때
        run = Jab(hero, foe, near, true, t => { if (t >= .15f && t < .17f) Shot(cam, rt, "8_blocked"); }); while (run.MoveNext()) yield return null;
        Note($"상대 방어 중: 피해 {dealt}, 상대 반응 {foeReaction}, 다시 움직일 수 있는 시각 {freeAt:F3}s");
        Check(dealt == hero.GuardedDamageOf(CombatMove.Jab) && foeReaction == "GUARD_HIT", "상대가 막으면 정해진 칩 피해와 막기 반응이 나온다");
        Check(endState == "IDLE" && hero.CanAct, "막힌 뒤에도 기본자세로 돌아온다 (state=" + endState + ")");

        // Only the three unguarded contacts above counted: the miss and blocked jab did not.
        Check(heroMeter.Charge == 3 && !heroMeter.IsReady && foeMeter.Charge == 0, "세 번 직격 = 공격자 3회; 막힌 공격·헛치기·피격은 충전하지 않는다");
        run = Jab(hero, foe, near, false, null); while (run.MoveNext()) yield return null;
        Check(heroMeter.Charge == 4 && !heroMeter.IsReady, "네 번째 직격은 아직 준비 전");
        run = Jab(hero, foe, near, false, null); while (run.MoveNext()) yield return null;
        Check(heroMeter.Charge == 5 && heroMeter.IsReady, "다섯 번째 실제 직격으로 공격자 준비 완료");
        run = Jab(hero, foe, near, false, null); while (run.MoveNext()) yield return null;
        Check(heroMeter.Charge == 5 && foeMeter.Charge == 0, "추가 직격에도 공격자 게이지는 5, 받는 쪽은 0");
        float canActDeadline = Time.time + 2f;
        while (!hero.CanAct && Time.time < canActDeadline) yield return null;
        Check(hero.CanAct, "일반 공격 회복 뒤 필살기를 사용할 수 있다");
        Check(heroMeter.Activate() && heroMeter.Charge == 0 && heroMeter.IsPlaying, "파운장 한 번 입력으로 즉시 게이지를 소비하고 발동한다");
        wait = Wait(4f); while (wait.MoveNext()) yield return null;
        Check(!heroMeter.IsPlaying && !heroMeter.IsReady, "키를 유지하거나 다시 떼지 않아도 파운장 발사가 끝난다");
        Check(heroMeter.LastResult == SpecialMove.Result.Hit && heroMeter.Charge == 0 && foeMeter.Charge == 0,
            "파운장이 실제 적중해도 양쪽 게이지는 다시 차지 않는다 (result=" + heroMeter.LastResult + ")");
        heroMeter.AddCharge(2); heroMeter.ResetCharge();
        Check(heroMeter.Charge == 0 && !heroMeter.IsReady, "새 라운드 초기화는 게이지를 비운다");

        // The newly approved straight extends much earlier; verify actual contact
        // at both frame rates, not merely the configured impact constant.
        var straightWindow = CombatContactWorld.Window(CombatMove.Straight);
        string cross = pvpJab ? "05_Cheolmun_CROSS" : "Hero_CROSS";
        foreach (int fps in new[] {60,30}) {
            Time.captureFramerate=fps;
            run=Jab(hero,foe,.78f,false,null,CombatMove.Straight);while(run.MoveNext())yield return null;
            Check(started && sawJab && clipAtContact==cross, fps+"fps 승인 스트레이트 클립을 재생한다 ("+clipAtContact+")");
            Check(dealt>0 && hitTime>=straightWindow.x-.001f && hitTime<=straightWindow.y+1f/fps+.001f,
                fps+"fps 스트레이트는 새 실제 접촉 구간에서 적중한다 (t="+hitTime+")");
            Check(endState=="IDLE" && hero.CanAct,"스트레이트 후 즉시 기본 조작 가능");
            if(!pvpJab)Check(sawSurface,"스트레이트 전용 보정 메시를 실제 재생 중 사용한다");
        }
        Time.captureFramerate=60;
        run=Jab(hero,foe,2.5f,false,null,CombatMove.Straight);while(run.MoveNext())yield return null;
        Check(dealt==0,"스트레이트도 주먹이 닿지 않는 거리에서는 헛친다");
        run=Jab(hero,foe,.78f,true,null,CombatMove.Straight);while(run.MoveNext())yield return null;
        Check(dealt==hero.GuardedDamageOf(CombatMove.Straight) && foeReaction=="GUARD_HIT","막힌 스트레이트는 칩 피해만 주고 추가 충전하지 않는다");
        Check(hero.SetVisionGuard(true) && hero.IsGuarding,"모션 가드를 유지한다");
        hero.SetVisionGuard(false);
        Check(!hero.IsGuarding && hero.TryAttack(CombatMove.Straight),"모션 가드 해제 직후 스트레이트를 시작한다");
        wait=Wait(2f);while(wait.MoveNext())yield return null;
        Check(hero.CanAct && !hero.IsAttacking,"블락 후 스트레이트 연결 뒤에도 정상 복귀한다");
        Time.captureFramerate = 0;
        rt.Release();
    }

    static string Playing(Animator anim)
    {
        var info = anim.GetCurrentAnimatorClipInfo(0);
        return info.Length > 0 && info[0].clip != null ? info[0].clip.name : "none";
    }
    /// <summary>왼팔꿈치 각도(도). 다 펴면 180.</summary>
    static float Elbow(Animator anim, CombatMove move)
    {
        var s = anim.GetBoneTransform(move == CombatMove.Jab ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm); var e = anim.GetBoneTransform(move == CombatMove.Jab ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm); var h = anim.GetBoneTransform(move == CombatMove.Jab ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        return Vector3.Angle(s.position - e.position, h.position - e.position);
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
        Time.captureFramerate = 0;
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
