using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// 파운장(破雲掌) Play 모드 검증 + 시연 촬영. 실제 지도 진입(river · RiverAmbush)으로 전투를 열고
/// 게이지, 컷인 슬로우, 정타/방어/끊김/최대 시간/빗나감, 웹캠 입력 경로, 적 AI 반응, K.O.·승리 처리를 확인한다.
/// Run: Unity -batchmode -executeMethod QiPalmVerification.Run -qiReport &lt;folder&gt;
/// </summary>
[InitializeOnLoad]
public static class QiPalmVerification
{
    const string Flag = "QiPalmVerification.Active", SaveKey = "VisionStriker.Run";
    static string Out { get { var a = Environment.GetCommandLineArgs(); int i = Array.IndexOf(a, "-qiReport"); return i >= 0 && i + 1 < a.Length ? a[i + 1] : Path.Combine(Application.temporaryCachePath, "QiPalmVerify"); } }
    static IEnumerator flow; static int last = -1, number; static bool recording;
    static readonly List<string> checks = new List<string>(), errors = new List<string>();
    static Camera cam; static Canvas hud; static Fighter3DView view; static Fighter enemyF, playerF; static Health eh, ph; static Rigidbody2D eb, pb;
    static SpecialMove special; static PlayerController player; static EnemyAI enemy;
    static RenderTexture rt; static Texture2D img;

    static QiPalmVerification() { if (SessionState.GetBool(Flag, false)) Subscribe(); }
    public static void Run()
    {
        Directory.CreateDirectory(Out + "/stills"); Directory.CreateDirectory(Out + "/frames");
        SessionState.SetBool(Flag + ".HadSave", PlayerPrefs.HasKey(SaveKey)); SessionState.SetString(Flag + ".Save", PlayerPrefs.GetString(SaveKey, ""));
        EditorSceneManager.OpenScene("Assets/Scenes/ScoutPass.unity"); SessionState.SetBool(Flag, true); Subscribe();
        EditorApplication.delayCall += () => EditorApplication.EnterPlaymode();
    }
    static void Subscribe() { EditorApplication.update -= Tick; EditorApplication.update += Tick; Application.logMessageReceived -= Log; Application.logMessageReceived += Log; }
    static void Log(string m, string s, LogType t) { if ((t == LogType.Error || t == LogType.Exception) && !m.Contains("MCP") && !s.Contains("IvanMurzak")) errors.Add(m + "\n" + s); }
    static void Tick()
    {
        if (!Application.isPlaying || EditorApplication.isCompiling) return;
        Application.runInBackground = true; EditorApplication.isPaused = false; EditorApplication.QueuePlayerLoopUpdate();
        if (Time.frameCount == last) return; last = Time.frameCount;
        try { if (flow == null) flow = Tests(); bool next = flow.MoveNext(); if (recording && cam) { LogEvents(); Capture(Out + "/frames/" + (number++).ToString("D5") + ".jpg"); } if (!next) Finish(); }
        catch (Exception e) { errors.Add(e.ToString()); Finish(); }
    }
    // Showcase edit support: frame-indexed events (phase changes, launch, result, K.O.) and labelled segments.
    static SpecialMove.Phase lastPhase; static SpecialMove.Result lastResult; static QiWave lastWave; static bool koLogged;
    static void Mark(string e) => File.AppendAllText(Out + "/events.txt", number + " " + e + "\n");
    static void Segment(string name) { Mark("segment " + name); lastPhase = special.CurrentPhase; lastResult = special.LastResult; lastWave = special.LastWave; koLogged = eh.IsDead; }
    static void LogEvents()
    {
        if (special == null) return;
        if (special.CurrentPhase != lastPhase) { Mark("phase " + special.CurrentPhase); lastPhase = special.CurrentPhase; }
        if (special.LastWave != null && special.LastWave != lastWave) { Mark("launch"); lastWave = special.LastWave; }
        if (special.LastResult != lastResult) { if (special.LastResult != SpecialMove.Result.None) Mark("result " + special.LastResult); lastResult = special.LastResult; }
        if (eh != null && eh.IsDead && !koLogged) { Mark("ko"); koLogged = true; }
    }
    static void Check(bool ok, string text) { checks.Add((ok ? "PASS " : "FAIL ") + text); File.WriteAllLines(Out + "/checks.txt", checks); }
    static void Finish()
    {
        recording = false;
        if (SessionState.GetBool(Flag + ".HadSave", false)) PlayerPrefs.SetString(SaveKey, SessionState.GetString(Flag + ".Save", "")); else PlayerPrefs.DeleteKey(SaveKey);
        PlayerPrefs.Save(); Time.timeScale = 1; Time.captureFramerate = 0; HitImpact.ReleaseTimeControl();
        Check(PlayerPrefs.GetString(SaveKey, "") == SessionState.GetString(Flag + ".Save", ""), "Original progress restored");
        File.WriteAllLines(Out + "/runtime_errors.txt", errors);
        bool pass = errors.Count == 0 && checks.All(x => x.StartsWith("PASS"));
        File.WriteAllText(Out + "/verify_done.txt", pass ? "PASS" : "FAIL");
        SessionState.SetBool(Flag, false); EditorApplication.update -= Tick; Application.logMessageReceived -= Log; EditorApplication.Exit(pass ? 0 : 1);
    }

    // ───────────── helpers ─────────────
    static IEnumerator Wait(float s) { float t = Time.time + s; while (Time.time < t) yield return null; }
    static IEnumerator WaitReal(float s) { float t = Time.realtimeSinceStartup + s; while (Time.realtimeSinceStartup < t) yield return null; }
    static IEnumerator Until(Func<bool> cond, float realTimeout) { float t = Time.realtimeSinceStartup + realTimeout; while (!cond() && Time.realtimeSinceStartup < t) yield return null; }
    static void Position(float left, float right)
    {
        foreach (var pair in new[] { (pb, left), (eb, right) }) { pair.Item1.position = new Vector2(pair.Item2, pair.Item1.position.y); pair.Item1.linearVelocity = Vector2.zero; }
        Physics2D.SyncTransforms(); playerF.Facing = 1; enemyF.Facing = -1;
    }
    static void Isolate(string id)
    {
        var run = RunState.Instance; var data = new RunData { seed = 1104, playerHealth = 100, playerMaxHealth = 100, pendingNodeId = id, currentNodeId = "cave", tutorialCompleted = true, tutorialVersion = 1, storyPresentationVersion = 1 };
        data.map = MapGenerator.Generate(run.Config.map, run.Config, data.seed); typeof(RunState).GetProperty("Data").SetValue(run, data);
    }
    static void Capture(string path, int w = 1280, int h = 720)
    {
        if (!rt) { rt = new RenderTexture(w, h, 24); rt.Create(); img = new Texture2D(w, h, TextureFormat.RGB24, false); }
        foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            if (c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay) { c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = cam; c.planeDistance = 1f; var sc = c.GetComponent<UnityEngine.UI.CanvasScaler>(); if (sc && c != hud) { } }
        cam.aspect = (float)w / h; cam.targetTexture = rt;
        RenderPipeline.SubmitRenderRequest(cam, new RenderPipeline.StandardRequest { destination = rt });
        var old = RenderTexture.active; RenderTexture.active = rt; img.ReadPixels(new Rect(0, 0, w, h), 0, 0); img.Apply(); RenderTexture.active = old; cam.targetTexture = null;
        File.WriteAllBytes(path, path.EndsWith("png") ? img.EncodeToPNG() : img.EncodeToJPG(88));
    }
    static void Still(string name) { if (cam) Capture(Out + "/stills/" + name + ".png"); }
    static void Ready() { special.ResetCharge(); special.AddCharge(special.HitsToCharge); }

    // ───────────── tests ─────────────
    static IEnumerator Tests()
    {
        yield return null;
        Isolate("river"); SceneRoutes.LoadBattle();
        for (var w = WaitReal(1.2f); w.MoveNext();) yield return null;
        Time.timeScale = 1;
        player = Object.FindAnyObjectByType<PlayerController>(); enemy = Object.FindAnyObjectByType<EnemyAI>(); player.enabled = enemy.enabled = false;
        foreach (var udp in Object.FindObjectsByType<UdpActionReceiver>(FindObjectsSortMode.None)) udp.enabled = false;
        special = player.GetComponent<SpecialMove>(); view = player.GetComponent<Fighter3DView>();
        playerF = player.GetComponent<Fighter>(); enemyF = enemy.GetComponent<Fighter>(); ph = player.GetComponent<Health>(); eh = enemy.GetComponent<Health>();
        pb = player.GetComponent<Rigidbody2D>(); eb = enemy.GetComponent<Rigidbody2D>();
        foreach (var p in Object.FindObjectsByType<Pushbox>(FindObjectsSortMode.None)) p.enabled = false;
        cam = Camera.main; var rig = cam.GetComponent<Battle3DCamera>(); rig.allowMouseOrbit = false; rig.automaticCombatFraming = false; rig.SetOrbit(-24, 9, true);
        hud = Object.FindAnyObjectByType<CombatHUD>()?.GetComponentInChildren<Canvas>(); if (hud) hud.transform.Find("KeyGuide")?.gameObject.SetActive(false);
        var webcamUi = GameObject.Find("WebcamUI"); if (webcamUi) webcamUi.SetActive(false);
        File.WriteAllText(Out + "/events.txt", "");
        Check(special != null && QiPalmAssets.Load() != null, "SpecialMove and QiPalmAssets present in " + SceneManager.GetActiveScene().name);
        Check(view.modelAnimator.runtimeAnimatorController != null && view.modelAnimator.HasState(0, Animator.StringToHash(QiPalmTiming.ChargeState)) && view.modelAnimator.HasState(0, Animator.StringToHash(QiPalmTiming.ReleaseState)), "player animator has SPECIAL_CHARGE / SPECIAL_RELEASE");

        // 1. 게이지가 없으면 나가지 않는다
        special.ResetCharge(); Check(!special.BeginCharge() && !special.IsPlaying, "not ready: no charge");

        // 2. 정타 (녹화)
        Position(-1.25f, 1.25f); ph.ResetHealth(); eh.ResetHealth(); Ready();
        for (var w = Wait(.6f); w.MoveNext();) yield return null;
        Time.captureFramerate = 30; number = 0; recording = true; Segment("hit");
        for (var w = Wait(.4f); w.MoveNext();) yield return null;
        int hp = eh.CurrentHealth; float startX = eb.position.x;
        Check(special.BeginCharge(), "charge starts when ready");
        for (int f = 0; f < 3; f++) yield return null;
        Check(Time.timeScale < .2f, "cut-in slows time: " + Time.timeScale.ToString("F2"));
        Check(Object.FindObjectsByType<QiChargeFx>(FindObjectsSortMode.None).Length == 1, "charge effect spawned");
        Still("01_cutin");
        for (var w = Until(() => special.CurrentPhase == SpecialMove.Phase.Charging, 3f); w.MoveNext();) yield return null;
        Check(Mathf.Approximately(Time.timeScale, 1f), "time restored after cut-in");
        for (var w = Wait(.9f); w.MoveNext();) yield return null;
        Check(view.CurrentState == QiPalmTiming.ChargeState, "charge pose playing: " + view.CurrentState);
        Still("02_charge");
        special.Release();
        for (var w = Until(() => special.LastWave != null, 2f); w.MoveNext();) yield return null;
        Check(special.LastWave != null && view.CurrentState == QiPalmTiming.ReleaseState, "release pose + wave launched");
        yield return null; yield return null; Still("03_release");
        for (var w = Until(() => special.LastResult != SpecialMove.Result.None, 2f); w.MoveNext();) yield return null;
        yield return null; Still("04_impact");
        Check(special.LastResult == SpecialMove.Result.Hit, "wave hits: " + special.LastResult);
        Check(hp - eh.CurrentHealth == special.LastDamage && special.LastDamage >= special.Damage, $"hit damage {hp - eh.CurrentHealth} (= {special.LastDamage}, base {special.Damage})");
        for (var w = Wait(.35f); w.MoveNext();) yield return null;
        Check(eb.position.x > startX + .25f, "knockback pushes enemy: " + (eb.position.x - startX).ToString("F2"));
        for (var w = Until(() => !special.IsPlaying, 3f); w.MoveNext();) yield return null;
        for (var w = Wait(.5f); w.MoveNext();) yield return null;
        recording = false; Time.captureFramerate = 0;
        Check(!playerF.IsSpecialActive && view.CurrentState == "IDLE", "player recovers to idle: " + view.CurrentState);
        Check(Mathf.Approximately(Time.timeScale, 1f), "time scale normal after special");

        // 3. 방어 → 기 관통
        Position(-1.25f, 1.25f); eh.ResetHealth(); ph.ResetHealth(); Ready();
        for (var w = Wait(.5f); w.MoveNext();) yield return null;
        Time.captureFramerate = 30; recording = true; Segment("guard");
        for (var w = Wait(.3f); w.MoveNext();) yield return null;
        hp = eh.CurrentHealth;
        special.BeginCharge();
        for (var w = Until(() => special.CurrentPhase == SpecialMove.Phase.Charging, 3f); w.MoveNext();) yield return null;
        for (var w = Wait(.3f); w.MoveNext();) yield return null;
        Check(enemyF.TryGuard(false), "enemy casts guard during charge");
        for (var w = Wait(.2f); w.MoveNext();) yield return null;
        special.Release();
        for (var w = Until(() => special.LastResult != SpecialMove.Result.None, 2f); w.MoveNext();) yield return null;
        int expectedChip = Mathf.Max(1, Mathf.RoundToInt(special.LastDamage * .25f));
        Check(special.LastResult == SpecialMove.Result.Blocked && hp - eh.CurrentHealth == expectedChip, $"guarded: chip damage {hp - eh.CurrentHealth} (expected {expectedChip})");
        for (var w = Wait(.4f); w.MoveNext();) yield return null;
        Check(!enemyF.IsGuarding, "guard broken by the wave");
        for (var w = Until(() => !special.IsPlaying, 3f); w.MoveNext();) yield return null;
        for (var w = Wait(.6f); w.MoveNext();) yield return null;
        recording = false; Time.captureFramerate = 0;

        // 4. 모으는 중 피격 → 끊김, 게이지 절반 반환 (상대가 잽 거리에서 끊는다)
        Position(-.7f, .2f); eh.ResetHealth(); ph.ResetHealth(); Ready();
        for (var w = Wait(.5f); w.MoveNext();) yield return null;
        Time.captureFramerate = 30; recording = true; Segment("break");
        for (var w = Wait(.3f); w.MoveNext();) yield return null;
        special.BeginCharge();
        for (var w = Until(() => special.CurrentPhase == SpecialMove.Phase.Charging, 3f); w.MoveNext();) yield return null;
        for (var w = Wait(.25f); w.MoveNext();) yield return null;
        enemyF.TryAttack(CombatMove.Jab);
        for (var w = Until(() => special.LastResult == SpecialMove.Result.Broken, 1.5f); w.MoveNext();) yield return null;
        if (special.LastResult != SpecialMove.Result.Broken) { ph.TakeDamage(3, Vector2.zero); yield return null; yield return null; }
        Check(special.LastResult == SpecialMove.Result.Broken && !special.IsPlaying, "hit while charging breaks it");
        Check(special.Charge == special.HitsToCharge / 2, "refund half the gauge: " + special.Charge);
        Check(Mathf.Approximately(Time.timeScale, 1f) && special.LastWave == null, "no wave and normal time after break");
        for (var w = Wait(.9f); w.MoveNext();) yield return null;
        Check(!playerF.IsSpecialActive && playerF.CanAct, "player can act again after break");
        recording = false; Time.captureFramerate = 0;

        // 5. 최대 시간 자동 발사
        Position(-1.25f, 1.25f); eh.ResetHealth(); Ready();
        for (var w = Wait(.4f); w.MoveNext();) yield return null;
        special.BeginCharge(); float began = Time.time;
        // batch frames can be slow (game time < real time), so allow a generous real-time limit
        for (var w = Until(() => special.LastWave != null, 15f); w.MoveNext();) yield return null;
        float firedAfter = Time.time - began;
        Check(special.LastWave != null && firedAfter >= special.MaxCharge && firedAfter < special.MaxCharge + .8f, "auto release at max charge after " + firedAfter.ToString("F2") + "s");
        for (var w = Until(() => !special.IsPlaying, 3f); w.MoveNext();) yield return null;
        Check(special.LastDamage > special.Damage, "full charge adds bonus damage: " + special.LastDamage);
        for (var w = Wait(.5f); w.MoveNext();) yield return null;

        // 6. 빗나감 → 무방비
        Position(-1.25f, 1.25f); eh.ResetHealth(); Ready();
        for (var w = Wait(.4f); w.MoveNext();) yield return null;
        playerF.Facing = -1; hp = eh.CurrentHealth; bool sawOpen = false;
        special.Activate();
        for (var w = Until(() => special.LastResult != SpecialMove.Result.None, 4f); w.MoveNext();) yield return null;
        // 기파가 사라진 뒤에도 whiffOpen(0.4초) 동안 필살기 잠금이 남아 아무 동작도 못 한다.
        float until = Time.realtimeSinceStartup + 2.5f, lockFrom = -1f;
        while (special.IsPlaying && Time.realtimeSinceStartup < until)
        {
            if (lockFrom < 0f && special.LastWave != null && special.LastWave.Finished) lockFrom = Time.realtimeSinceStartup;
            if (lockFrom >= 0f && playerF.CanAct) break;
            yield return null;
        }
        sawOpen = lockFrom >= 0f && Time.realtimeSinceStartup - lockFrom >= .3f;
        Check(special.LastResult == SpecialMove.Result.Whiffed && eh.CurrentHealth == hp && sawOpen, "whiff: no damage, short opening");
        playerF.Facing = 1;
        for (var w = Wait(.5f); w.MoveNext();) yield return null;

        // 7. 웹캠 입력 경로 (PlayerController.ExternalAction)
        Position(-1.25f, 1.25f); eh.ResetHealth(); Ready();
        for (var w = Wait(.4f); w.MoveNext();) yield return null;
        player.ExternalAction("SPECIAL_CHARGE");
        for (var w = Until(() => special.CurrentPhase == SpecialMove.Phase.Charging, 3f); w.MoveNext();) yield return null;
        Check(special.IsCharging, "webcam SPECIAL_CHARGE starts charging");
        player.ExternalAction("SPECIAL_CHARGE"); // repeated packets keep charging, no restart
        for (var w = Wait(.5f); w.MoveNext();) yield return null;
        player.ExternalAction("SPECIAL_RELEASE");
        for (var w = Until(() => special.LastWave != null, 2f); w.MoveNext();) yield return null;
        Check(special.LastWave != null, "webcam SPECIAL_RELEASE fires the wave");
        for (var w = Until(() => !special.IsPlaying, 3f); w.MoveNext();) yield return null;
        for (var w = Wait(.5f); w.MoveNext();) yield return null;

        // 8. 적 AI 가 기 모으기를 보고 방어
        Position(-1.25f, 1.25f); eh.ResetHealth(); ph.ResetHealth(); Ready();
        var so = new SerializedObject(enemy); so.FindProperty("specialGuardChance").floatValue = 1f; so.ApplyModifiedPropertiesWithoutUndo();
        for (var w = Wait(.4f); w.MoveNext();) yield return null;
        Time.captureFramerate = 30; recording = true; Segment("ai");
        for (var w = Wait(.3f); w.MoveNext();) yield return null;
        enemy.enabled = true; special.BeginCharge(); bool aiGuarded = false;
        until = Time.realtimeSinceStartup + 3f;
        while (special.IsCharging && Time.realtimeSinceStartup < until) { aiGuarded |= enemyF.IsGuarding; if (special.CurrentPhase == SpecialMove.Phase.Charging && aiGuarded) break; yield return null; }
        Check(aiGuarded, "enemy AI guards against the charge");
        special.Release();
        for (var w = Until(() => !special.IsPlaying, 4f); w.MoveNext();) yield return null;
        enemy.enabled = false; enemyF.CancelAttack();
        for (var w = Wait(.8f); w.MoveNext();) yield return null;
        recording = false; Time.captureFramerate = 0;

        // 9. 파운장으로 K.O. → 승리 처리 (녹화)
        Position(-1.25f, 1.25f); ph.ResetHealth(); eh.ResetHealth(); eh.TakeDamage(eh.MaxHealth - 12, Vector2.zero, false, true); Ready();
        for (var w = Wait(.8f); w.MoveNext();) yield return null;
        Time.captureFramerate = 30; recording = true; Segment("ko");
        for (var w = Wait(.3f); w.MoveNext();) yield return null;
        special.Activate();
        for (var w = Until(() => eh.IsDead, 4f); w.MoveNext();) yield return null;
        Check(eh.IsDead && special.LastResult == SpecialMove.Result.Hit, "special K.O.");
        for (var w = Wait(1.2f); w.MoveNext();) yield return null;
        Still("05_ko");
        recording = false; Time.captureFramerate = 0;
        float done = Time.realtimeSinceStartup + 10;
        while (RunState.Instance.HasPendingBattle && Time.realtimeSinceStartup < done) yield return null;
        Check(RunState.Instance.LastBattleWon && !RunState.Instance.HasPendingBattle, "victory resolves after special K.O.");
        Check(Mathf.Approximately(Time.timeScale, 1f), "time scale restored after K.O.");
    }
}
