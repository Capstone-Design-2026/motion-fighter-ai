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
/// 전투 씬을 실제로 재생하면서 주인공과 상대가 같은 순서로 움직이는 모습을 프레임으로 저장한다 (batchmode 전용, 확인용 영상 재료).
/// 순서: 걷기(앞·뒤) → 잽 → 스트레이트 → 잽·스트레이트 연속 → 얼굴 막기 → 로우킥 막기, 그리고 서로 실제로 맞히는 장면.
/// 스트레이트·잽이 "주먹이 다 뻗은 시각"과 "피해가 들어간 시각"도 함께 적는다.
/// Unity -batchmode -executeMethod MotionFlowPreview.Run -motionFlowReport &lt;폴더&gt; [-motionFlowScene &lt;씬 경로&gt;] -redbool-keyboard
/// 결과: 폴더에 frames/f_00000.jpg…(60fps), markers.txt(프레임 번호와 장면 이름), flow_report.txt, flow_done.txt.
/// 저장 데이터는 시작 전에 보관했다가 끝나면 되돌린다.
/// </summary>
[InitializeOnLoad]
public static class MotionFlowPreview
{
    const string Active = "MotionFlowPreview.Active", SaveKey = "VisionStriker.Run";
    const int CaptureFps = 60;
    static IEnumerator flow;
    static int lastFrame = -1, frameIndex;
    static bool recording;
    static Camera cam; static RenderTexture rt; static Texture2D img;
    static readonly List<string> lines = new List<string>(), markers = new List<string>();
    static MotionFlowPreview() { if (SessionState.GetBool(Active, false)) Subscribe(); }
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
        string outDir = Arg("-motionFlowReport", Path.GetFullPath("Logs/MotionFlow")); Directory.CreateDirectory(Path.Combine(outDir, "frames"));
        SessionState.SetString(Active + ".Out", outDir);
        SessionState.SetBool(Active + ".HadSave", PlayerPrefs.HasKey(SaveKey));
        SessionState.SetString(Active + ".Save", PlayerPrefs.GetString(SaveKey, ""));
        SessionState.SetFloat(Active + ".Start", (float)EditorApplication.timeSinceStartup);
        EditorSceneManager.OpenScene(Arg("-motionFlowScene", "Assets/Scenes/StoryStages/SealedGate.unity"));
        SessionState.SetBool(Active, true); Subscribe();
        EditorApplication.delayCall += () => EditorApplication.EnterPlaymode();
    }

    static void Tick()
    {
        if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Active + ".Start", 0) > 540) { Finish("Timeout"); return; }
        if (!Application.isPlaying) return;
        EditorApplication.isPaused = false; EditorApplication.QueuePlayerLoopUpdate();
        if (lastFrame == Time.frameCount) return;
        lastFrame = Time.frameCount;
        try
        {
            if (flow == null) flow = Flow();
            bool more = flow.MoveNext();
            if (recording) Capture();
            if (!more) Finish(null);
        }
        catch (Exception e) { Finish(e.ToString()); }
    }

    static void Capture()
    {
        RenderPipeline.SubmitRenderRequest(cam, new RenderPipeline.StandardRequest { destination = rt });
        RenderTexture.active = rt; img.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); img.Apply(); RenderTexture.active = null;
        File.WriteAllBytes(Path.Combine(Out, "frames", "f_" + frameIndex.ToString("00000") + ".jpg"), img.EncodeToJPG(86));
        frameIndex++;
    }
    static void Mark(string label) { markers.Add(frameIndex + "\t" + label); Debug.Log("[MotionFlow] " + frameIndex + " " + label); }
    static void Note(string what) { lines.Add(what); Debug.Log("[MotionFlow] " + what); }
    static IEnumerator Wait(float seconds) { float until = Time.time + seconds; while (Time.time < until) yield return null; }

    static void Place(GameObject fighter, float x)
    {
        var p = fighter.transform.position; p.x = x; fighter.transform.position = p;
        var body = fighter.GetComponent<Rigidbody2D>(); if (body != null) { body.position = new Vector2(x, body.position.y); body.linearVelocity = Vector2.zero; }
    }

    /// <summary>공격 한 번. 주먹이 가장 멀리 나간 시각, 거의 다 나간(97%) 시각, 피해가 들어간 시각을 잰다.</summary>
    static IEnumerator Strike(string who, Fighter attacker, Fighter target, CombatMove move, float seconds)
    {
        var view = attacker.GetComponent<Fighter3DView>(); var anim = view.modelAnimator; var hp = target.GetComponent<Health>();
        var hand = anim.GetBoneTransform(move == CombatMove.Jab ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        int before = hp.CurrentHealth; bool started = attacker.TryAttack(move);
        float t0 = attacker.AttackStarted, hit = -1f, free = -1f; var reach = new List<Vector2>();
        float until = Time.time + seconds;
        while (Time.time < until)
        {
            float t = Time.time - t0;
            reach.Add(new Vector2(t, (hand.position.x - attacker.transform.position.x) * attacker.Facing));
            if (hit < 0f && hp.CurrentHealth < before) hit = t;
            if (free < 0f && t > .05f && !attacker.IsAttacking) free = t;
            yield return null;
        }
        float max = float.MinValue, maxAt = 0f, arrive = -1f;
        foreach (var r in reach) if (r.y > max) { max = r.y; maxAt = r.x; }
        float rest = reach.Count > 0 ? reach[0].y : 0f;
        foreach (var r in reach) if (r.y >= rest + (max - rest) * .97f) { arrive = r.x; break; }
        var window = CombatContactWorld.Window(move);
        Note($"{who} {move}: 시작됨={started} 주먹이 거의 다 나간 시각 {arrive:F3}s (가장 멀리 {maxAt:F3}s, {max:F3}m) / 피해 {before - hp.CurrentHealth} 들어간 시각 {(hit < 0 ? "없음" : hit.ToString("F3") + "s")} / 판정 구간 {window.x:F3}~{window.y:F3}s / 다시 움직일 수 있는 시각 {free:F3}s / 끝난 뒤 상태 {view.CurrentState}");
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
        Place(player.gameObject, -.85f); Place(enemy.gameObject, .85f);
        wait = Wait(2.5f); while (wait.MoveNext()) yield return null;

        var hero = player.GetComponent<Fighter>(); var foe = enemy.GetComponent<Fighter>();
        var heroView = player.GetComponent<Fighter3DView>(); var foeView = enemy.GetComponent<Fighter3DView>();
        var heroMove = player.GetComponent<HeroAuthoredMovement>(); var foeBody = enemy.GetComponent<Rigidbody2D>();
        var heroHp = player.GetComponent<Health>(); var foeHp = enemy.GetComponent<Health>();
        cam = Camera.main; rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32); img = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        Note("scene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name + " hero controller=" + heroView.modelAnimator.runtimeAnimatorController.name + " foe visual=" + foeView.visualRoot.name + " foe authoredSteps=" + foeView.authoredSteps);
        foreach (var c in heroView.modelAnimator.runtimeAnimatorController.animationClips)
            if (c != null && (c.name.EndsWith("_JAB") || c.name.EndsWith("_CROSS"))) Note("주인공 클립 " + c.name + " length=" + c.length.ToString("F3") + " path=" + AssetDatabase.GetAssetPath(c));

        // 걷는 동안 딛고 있는 발이 바닥에서 얼마나 미끄러지는지 잰다. 한 발이 다른 발보다 1.5cm 넘게 낮을 때 그 발을 딛은 발로 본다.
        IEnumerator Walk(string who, Fighter3DView view, Rigidbody2D body, float seconds, Action drive, Action stop)
        {
            float until = Time.time + seconds, slid = 0f, time = 0f, x0 = body.position.x, t0 = Time.time, playback = 1f; int planted = -1; Vector3 last = default; string state = "";
            var anim = view.modelAnimator; var feet = new[] { anim.GetBoneTransform(HumanBodyBones.LeftFoot), anim.GetBoneTransform(HumanBodyBones.RightFoot) };
            while (Time.time < until)
            {
                drive();
                float gap = feet[0].position.y - feet[1].position.y; int low = gap < -.015f ? 0 : gap > .015f ? 1 : -1;
                if (low >= 0 && low == planted && Time.time - t0 > .25f) { slid += Mathf.Abs(feet[low].position.x - last.x); time += Time.deltaTime; }
                planted = low; if (low >= 0) last = feet[low].position;
                if (view.CurrentState != "IDLE") { state = view.CurrentState; playback = anim.speed; }
                yield return null;
            }
            float travelled = Mathf.Abs(body.position.x - x0) / Mathf.Max(.01f, Time.time - t0);
            stop();
            Note($"{who} 걷기: 상태 {state}, 실제 이동 {travelled:F2} m/s, 재생 빠르기 {playback:F2}, 딛은 발이 미끄러진 빠르기 평균 {(time > 0 ? slid / time : 0f) * 100f:F1} cm/s (잰 시간 {time:F2}s)");
        }
        IEnumerator HeroWalk(float axis, float seconds) => Walk("주인공 " + (axis > 0 ? "앞으로" : "뒤로"), heroView, player.GetComponent<Rigidbody2D>(), seconds,
            () => { if (heroMove != null) heroMove.SetInput(axis); }, () => { if (heroMove != null) heroMove.SetInput(0f); });
        IEnumerator FoeWalk(float dir, float seconds, float speed) => Walk("상대 " + (dir < 0 ? "앞으로" : "뒤로"), foeView, foeBody, seconds,
            () => foeBody.linearVelocity = new Vector2(dir * speed, foeBody.linearVelocity.y), () => foeBody.linearVelocity = new Vector2(0f, foeBody.linearVelocity.y));
        // 앞 동작이 끝나 다시 공격할 수 있게 되는 바로 그 프레임에 다음 공격을 넣는다(연속기).
        IEnumerator Chain(string who, Fighter attacker, Fighter target, CombatMove first, CombatMove second, float tail)
        {
            var a = Strike(who, attacker, target, first, .3f); while (a.MoveNext()) yield return null;
            float t0 = attacker.AttackStarted; while (!attacker.CanAttack && Time.time - t0 < 2f) yield return null;
            Note($"{who} {first} → {second}: 다음 공격을 넣을 수 있게 된 시각 {Time.time - t0:F3}s");
            var b = Strike(who, attacker, target, second, tail); while (b.MoveNext()) yield return null;
        }
        // 맞히는 장면은 매번 같은 거리에서 다시 시작한다(앞 공격에 맞아 밀려난 거리를 되돌린다).
        IEnumerator Reset(float distance)
        {
            recording = false; heroHp.ResetHealth(); foeHp.ResetHealth();
            Place(player.gameObject, -distance * .5f); Place(enemy.gameObject, distance * .5f);
            var w = Wait(1.1f); while (w.MoveNext()) yield return null;
            recording = true;
        }

        Time.captureFramerate = CaptureFps; recording = true;
        IEnumerator s;

        // ── 1. 주인공: 닿지 않는 거리에서 동작만 ──
        Mark("주인공  |  기본자세");
        s = Wait(1.0f); while (s.MoveNext()) yield return null;
        Mark("주인공  |  앞으로 걷기"); s = HeroWalk(1f, .9f); while (s.MoveNext()) yield return null;
        Mark("주인공  |  뒤로 걷기"); s = HeroWalk(-1f, .9f); while (s.MoveNext()) yield return null;
        s = Wait(.5f); while (s.MoveNext()) yield return null;
        Mark("주인공  |  잽"); s = Strike("주인공(헛침)", hero, foe, CombatMove.Jab, .95f); while (s.MoveNext()) yield return null;
        Mark("주인공  |  스트레이트"); s = Strike("주인공(헛침)", hero, foe, CombatMove.Straight, 1.45f); while (s.MoveNext()) yield return null;
        Mark("주인공  |  잽 → 스트레이트 (가장 빨리 이었을 때)");
        s = Chain("주인공(헛침)", hero, foe, CombatMove.Jab, CombatMove.Straight, 1.45f); while (s.MoveNext()) yield return null;
        Mark("주인공  |  얼굴 막기"); hero.TryGuard(false); s = Wait(.9f); while (s.MoveNext()) yield return null; hero.ReleaseGuard(); s = Wait(.6f); while (s.MoveNext()) yield return null;
        Mark("주인공  |  로우킥 막기"); hero.TryGuard(true); s = Wait(.9f); while (s.MoveNext()) yield return null; hero.ReleaseGuard(); s = Wait(.7f); while (s.MoveNext()) yield return null;

        // ── 2. 상대: 같은 순서 ──
        float foeSpeed = 1.04f;
        Mark("상대  |  기본자세"); s = Wait(.8f); while (s.MoveNext()) yield return null;
        Mark("상대  |  앞으로 걷기"); s = FoeWalk(-1f, .9f, foeSpeed); while (s.MoveNext()) yield return null;
        Mark("상대  |  뒤로 걷기"); s = FoeWalk(1f, .9f, foeSpeed); while (s.MoveNext()) yield return null;
        s = Wait(.5f); while (s.MoveNext()) yield return null;
        Mark("상대  |  잽"); s = Strike("상대(헛침)", foe, hero, CombatMove.Jab, .95f); while (s.MoveNext()) yield return null;
        Mark("상대  |  스트레이트"); s = Strike("상대(헛침)", foe, hero, CombatMove.Straight, 1.45f); while (s.MoveNext()) yield return null;
        Mark("상대  |  잽 → 스트레이트 (가장 빨리 이었을 때)");
        s = Chain("상대(헛침)", foe, hero, CombatMove.Jab, CombatMove.Straight, 1.45f); while (s.MoveNext()) yield return null;
        Mark("상대  |  얼굴 막기"); foe.TryGuard(false); s = Wait(.9f); while (s.MoveNext()) yield return null; foe.ReleaseGuard(); s = Wait(.6f); while (s.MoveNext()) yield return null;
        Mark("상대  |  로우킥 막기"); foe.TryGuard(true); s = Wait(.9f); while (s.MoveNext()) yield return null; foe.ReleaseGuard(); s = Wait(.7f); while (s.MoveNext()) yield return null;

        // ── 3. 실제로 맞히기 ──
        recording = false;
        var profile = CombatReach.MeasureJab(heroView, player.transform);
        yield return null; yield return null;
        float heroReach = CombatReach.MaxHitDistance(profile, hero, foe);
        float foeReach = enemy.StrikeRange;
        Note($"주인공 잽이 닿는 가장 먼 거리 {heroReach:F3} m / 상대가 공격을 시작하는 거리 {foeReach:F3} m");
        float near = Mathf.Clamp(Mathf.Min(heroReach, foeReach) - .06f, .5f, .9f);
        s = Reset(near); while (s.MoveNext()) yield return null;
        Mark("맞히기  |  주인공 잽"); s = Strike("주인공(맞힘)", hero, foe, CombatMove.Jab, 1.3f); while (s.MoveNext()) yield return null;
        s = Reset(near); while (s.MoveNext()) yield return null;
        Mark("맞히기  |  주인공 스트레이트"); s = Strike("주인공(맞힘)", hero, foe, CombatMove.Straight, 1.9f); while (s.MoveNext()) yield return null;
        s = Reset(near); while (s.MoveNext()) yield return null;
        Mark("맞히기  |  주인공 잽 → 스트레이트"); s = Chain("주인공(맞힘)", hero, foe, CombatMove.Jab, CombatMove.Straight, 1.9f); while (s.MoveNext()) yield return null;
        s = Reset(near); while (s.MoveNext()) yield return null;
        Mark("맞히기  |  상대 잽"); s = Strike("상대(맞힘)", foe, hero, CombatMove.Jab, 1.3f); while (s.MoveNext()) yield return null;
        s = Reset(near); while (s.MoveNext()) yield return null;
        Mark("맞히기  |  상대 스트레이트"); s = Strike("상대(맞힘)", foe, hero, CombatMove.Straight, 1.9f); while (s.MoveNext()) yield return null;
        Mark("끝");
        recording = false; Time.captureFramerate = 0;
    }

    static void Finish(string failure)
    {
        Time.captureFramerate = 0; recording = false;
        SessionState.SetBool(Active, false); EditorApplication.update -= Tick;
        EditorApplication.UnlockReloadAssemblies(); AssetDatabase.AllowAutoRefresh();
        if (SessionState.GetBool(Active + ".HadSave", false)) PlayerPrefs.SetString(SaveKey, SessionState.GetString(Active + ".Save", ""));
        else PlayerPrefs.DeleteKey(SaveKey);
        PlayerPrefs.Save();
        if (failure != null) lines.Add("ERROR " + failure);
        File.WriteAllLines(Path.Combine(Out, "flow_report.txt"), lines);
        File.WriteAllLines(Path.Combine(Out, "markers.txt"), markers);
        File.WriteAllText(Path.Combine(Out, "flow_done.txt"), (failure == null ? "PASS " : "FAIL ") + frameIndex + " frames" + (failure != null ? " " + failure.Split('\n')[0] : ""));
        EditorApplication.Exit(failure == null ? 0 : 1);
    }
}
