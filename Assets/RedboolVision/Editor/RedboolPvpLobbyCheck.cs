using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// PvP 대기실 → 인물 선택 → 수련 → 대전 시작까지를 가짜 카메라 신호(UDP 5065/5066)로 끝까지 돌려 본다.
/// 실제 카메라는 열지 않는다. 신호를 보내는 방식은 RedboolPvpPlayCheck 와 같다.
///
///   Unity -batchmode -executeMethod RedboolPvpLobbyCheck.Run -redbool-pvp-lobby
///   환경변수 REDBOOL_LOBBY_CHECK_OUTPUT=&lt;빈 폴더&gt;  → checks.txt, result.txt, 단계별 화면(png)
/// </summary>
[InitializeOnLoad]
public static class RedboolPvpLobbyCheck
{
    const string Active = "RedboolPvpLobbyCheck.Active", SaveKey = "VisionStriker.Run";
    static IEnumerator flow; static int frame = -1;
    static readonly UdpClient[] clients = new UdpClient[2];
    static readonly long[] sequences = new long[2]; static readonly double[] nextSync = new double[2];
    static readonly bool[] block = { false, false }, send = { true, true };
    static readonly string[] movement = { "NEUTRAL", "NEUTRAL" };
    static readonly List<string> checks = new List<string>();
    static string Output => Environment.GetEnvironmentVariable("REDBOOL_LOBBY_CHECK_OUTPUT");
    // 빌드한 게임의 경로를 주면 그 게임을 2P 화면(손님)으로 띄워 두 화면 대전으로 돌린다.
    static string GuestBinary => Environment.GetEnvironmentVariable("REDBOOL_DUAL_CLIENT");
    static System.Diagnostics.Process guest;
    static void StartGuest()
    {
        var info = new System.Diagnostics.ProcessStartInfo(GuestBinary, "--redbool-pvp-client 127.0.0.1 -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile \"" + Path.Combine(Output, "guest.log") + "\"") { UseShellExecute = false };
        info.EnvironmentVariables["REDBOOL_VIEW_CAPTURE_DIR"] = Output;
        guest = System.Diagnostics.Process.Start(info);
    }
    static void StopGuest() { if (guest != null) { if (!guest.HasExited) { guest.Kill(); guest.WaitForExit(5000); } guest.Dispose(); guest = null; } }
    static RedboolPvp Game => RedboolPvp.Instance;
    static long Now { get { long t = System.Diagnostics.Stopwatch.GetTimestamp(), f = System.Diagnostics.Stopwatch.Frequency; return t / f * 1000000000L + (long)((t % f) * (1000000000.0 / f)); } }

    [Serializable] class Packet
    {
        public int version = 2, ttl_ms = 150, calibration_version = 1, calibration_generation = 1;
        public string kind, stream_id, actor_id, capture_epoch, sync_id = "lobby-check", event_id, side, gesture, fsm_state = "READY", tracking = "VALID", movement = "NEUTRAL", personal_status = "BASELINE V26";
        public long sequence, sent_ns, receiver_offset_ns, clock_uncertainty_ns, event_expires_ns;
        public bool calibrated = true, block_active; public float movement_strength;
    }

    static RedboolPvpLobbyCheck() { if (SessionState.GetBool(Active, false)) Subscribe(); }

    public static void Run()
    {
        if (!Application.isBatchMode || string.IsNullOrEmpty(Output)) throw new Exception("Isolated batch output required");
        if (!RedboolPvpLobby.Enabled) throw new Exception("Pass -redbool-pvp-lobby so the lobby is on in batch mode");
        Directory.CreateDirectory(Output);
        SessionState.SetBool(Active + ".HadSave", PlayerPrefs.HasKey(SaveKey)); SessionState.SetString(Active + ".Save", PlayerPrefs.GetString(SaveKey, ""));
        SessionState.SetFloat(Active + ".Start", (float)EditorApplication.timeSinceStartup);
        EditorSceneManager.OpenScene("Assets/Scenes/StartScene.unity"); SessionState.SetBool(Active, true); Subscribe();
        EditorApplication.delayCall += () => EditorApplication.EnterPlaymode();
    }

    static void Subscribe() { EditorApplication.update -= Tick; EditorApplication.update += Tick; }

    static void Tick()
    {
        if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Active + ".Start", 0) > 900) { Finish("Timeout"); return; }
        if (!Application.isPlaying || EditorApplication.isCompiling) return;
        EditorApplication.isPaused = false; EditorApplication.QueuePlayerLoopUpdate();
        if (frame == Time.frameCount) return; frame = Time.frameCount;
        try { if (flow == null) flow = Tests(); if (!flow.MoveNext()) Finish(null); } catch (Exception e) { Finish(e.ToString()); }
    }

    static void Check(bool ok, string message)
    {
        checks.Add((ok ? "PASS " : "FAIL ") + message); File.WriteAllLines(Path.Combine(Output, "checks.txt"), checks);
        if (!ok) throw new Exception(message);
    }

    static void Send(int slot, Packet p)
    {
        p.actor_id = "P" + (slot + 1); p.stream_id = "lobby-test-" + slot; p.capture_epoch = p.stream_id; p.sequence = ++sequences[slot]; p.sent_ns = Now;
        var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(p)); clients[slot].Send(bytes, bytes.Length);
    }

    static void Drain() { for (int i = 0; i < 2; i++) while (clients[i] != null && clients[i].Available > 0) { var peer = new IPEndPoint(IPAddress.Any, 0); clients[i].Receive(ref peer); } }

    // 두 사람의 카메라가 보내는 상태를 seconds 동안 매 프레임 흉내 낸다.
    static IEnumerator Pump(float seconds)
    {
        double end = Time.realtimeSinceStartupAsDouble + seconds;
        do
        {
            for (int i = 0; i < 2; i++) if (send[i])
            {
                if (Time.realtimeSinceStartupAsDouble >= nextSync[i]) { Send(i, new Packet { kind = "SYNC" }); nextSync[i] = Time.realtimeSinceStartupAsDouble + 1; }
                Send(i, new Packet { kind = "STATE", block_active = block[i], movement = movement[i], movement_strength = movement[i] == "NEUTRAL" ? 0 : .8f });
            }
            Drain(); yield return null;
        } while (Time.realtimeSinceStartupAsDouble < end);
        Drain();
    }

    static void Gesture(int slot, string gesture, string id)
    {
        Send(slot, new Packet { kind = "GESTURE", gesture = gesture, event_id = id, side = gesture == "SPECIAL" ? "BOTH" : gesture.StartsWith("LEFT") ? "LEFT" : "RIGHT", event_expires_ns = Now + 150000000L });
    }

    static void Place(int slot, float x)
    {
        var p = Game.Players[slot]; p.transform.position = new Vector3(x, p.transform.position.y, 0);
        var body = p.GetComponent<Rigidbody2D>(); body.position = p.transform.position; body.linearVelocity = Vector2.zero;
    }

    static void Shot(string name, int width = 1920, int height = 1080)
    {
        var camera = Camera.main; var rt = new RenderTexture(width, height, 24);
        // 캔버스가 이 그림 크기(1920x1080)에 맞춰 배치되게 카메라의 출력을 먼저 바꿔 둔다.
        var previous = camera.targetTexture; camera.targetTexture = rt;
        var overlays = new List<Canvas>();
        foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            if (c.renderMode == RenderMode.ScreenSpaceOverlay) { overlays.Add(c); c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = camera; c.planeDistance = 1; }
        Canvas.ForceUpdateCanvases();
        UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera, new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = rt });
        camera.targetTexture = previous;
        var old = RenderTexture.active; RenderTexture.active = rt;
        var image = new Texture2D(width, height, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
        File.WriteAllBytes(Path.Combine(Output, name + ".png"), image.EncodeToPNG());
        RenderTexture.active = old; Object.Destroy(image); rt.Release(); Object.Destroy(rt);
        foreach (var c in overlays) { c.renderMode = RenderMode.ScreenSpaceOverlay; c.worldCamera = null; }
    }

    static string VisualName(int slot) => Game.Players[slot].GetComponent<Fighter3DView>().visualRoot.name;

    static IEnumerator Wait(float seconds) { for (var w = Pump(seconds); w.MoveNext();) yield return null; }

    static IEnumerator Tests()
    {
        yield return null; yield return null;
        bool dual = !string.IsNullOrEmpty(GuestBinary);
        if (dual) { RedboolPvp.HostDual(); StartGuest(); } else RedboolPvp.Begin();
        yield return null; yield return null; yield return null;
        Check(Game != null && Game.Lobby != null, "PvP에 들어오면 대기실이 생긴다");
        var lobby = Game.Lobby;
        Check(lobby.Current == RedboolPvpLobby.Stage.Room, "처음 단계는 대기실이다");
        Check(RedboolPvpRoster.Count == 12, "고를 수 있는 인물은 12명이다");
        for (int i = 0; i < RedboolPvpRoster.Count; i++)
        {
            Check(RedboolPvpRoster.Actor(i) != null && RedboolPvpRoster.Actor(i).artwork != null, "초상 그림이 있다: " + RedboolPvpRoster.Get(i).name);
            if (i != RedboolPvpRoster.Hero) Check(RedboolPvpRoster.Visual(i) != null, "겉모습이 있다: " + RedboolPvpRoster.Get(i).name);
        }
        string heroName = VisualName(0);
        // 전투 효과 준비(RedboolRenderWarmup)는 이 검사와 무관하게 가끔 끝나지 않는다(대기실을 넣기 전에도 6번 중 1번). 그 경우를 먼저 가려낸다.
        double warmupUntil = Time.realtimeSinceStartupAsDouble + 5;
        while (Game.Gate == 1 && Time.realtimeSinceStartupAsDouble < warmupUntil) yield return null;
        Check(Game.Gate != 1, "전투 효과 준비가 끝난다 (끝나지 않으면 대기실과 무관한 기존 문제이니 다시 돌린다)");
        for (int i = 0; i < 2; i++) { clients[i] = new UdpClient(); clients[i].Connect(IPAddress.Loopback, 5065 + i); }
        send[0] = send[1] = false;
        for (var w = Wait(.8f); w.MoveNext();) yield return null;
        Check(lobby.Current == RedboolPvpLobby.Stage.Room, "카메라가 없으면 대기실에 머문다");
        Shot("1_room");
        if (dual)
        {
            // 손님 게임이 켜지고 화면 연결이 될 때까지 기다린다 (처음에는 셰이더 준비로 몇 초 걸린다).
            double viewUntil = Time.realtimeSinceStartupAsDouble + 40;
            while (!Game.ViewReady && Time.realtimeSinceStartupAsDouble < viewUntil) yield return null;
            Check(Game.ViewReady, "2P 화면(손님 게임)이 연결된다");
            for (var w = Wait(1.5f); w.MoveNext();) yield return null;
            Check(lobby.Current == RedboolPvpLobby.Stage.Room, "2P 화면이 연결돼도 카메라가 없으면 대기실에 머문다");
        }
        send[0] = true;
        for (var w = Wait(.6f); w.MoveNext();) yield return null;
        Check(lobby.Current == RedboolPvpLobby.Stage.Room, "한 사람의 카메라만 잡히면 대기실에 머문다");
        send[1] = true;
        for (var w = Wait(.7f); w.MoveNext();) yield return null;
        Check(lobby.Current == RedboolPvpLobby.Stage.Select, "두 사람의 카메라가 잡히면 인물 선택으로 넘어간다");
        Check(lobby.Cursor[0] == RedboolPvpRoster.Hero && lobby.Cursor[1] == RedboolPvpRoster.DefaultSecond, "처음 고른 칸은 1P 청운, 2P 철문이다");
        Vector3 start0 = Game.Players[0].transform.position, start1 = Game.Players[1].transform.position;
        for (var w = Wait(dual ? 2.2f : .9f); w.MoveNext();) yield return null;   // 두 화면일 때는 손님 쪽이 선택 화면을 한 장 남길 시간을 준다
        Shot("2_select");

        // 몸을 기울여 고르기
        movement[0] = "RIGHT"; for (var w = Wait(.15f); w.MoveNext();) yield return null;
        movement[0] = "NEUTRAL"; for (var w = Wait(.25f); w.MoveNext();) yield return null;
        Check(lobby.Cursor[0] == 1, "1P가 오른쪽으로 기울이면 한 칸 옮겨 간다 (칸 " + lobby.Cursor[0] + ")");
        movement[0] = "RIGHT"; for (var w = Wait(.8f); w.MoveNext();) yield return null;
        movement[0] = "NEUTRAL"; for (var w = Wait(.25f); w.MoveNext();) yield return null;
        Check(lobby.Cursor[0] == 3, "계속 기울이고 있으면 0.6초마다 한 칸씩 간다 (칸 " + lobby.Cursor[0] + ")");
        movement[1] = "LEFT"; for (var w = Wait(.15f); w.MoveNext();) yield return null;
        movement[1] = "NEUTRAL"; for (var w = Wait(.25f); w.MoveNext();) yield return null;
        Check(lobby.Cursor[1] == 4, "2P가 왼쪽으로 기울이면 한 칸 옮겨 간다 (칸 " + lobby.Cursor[1] + ")");
        Check((Game.Players[0].transform.position - start0).magnitude < .02f && (Game.Players[1].transform.position - start1).magnitude < .02f, "고르는 동안에는 인물이 걸어가지 않는다");

        // 확정·취소
        Gesture(0, "LEFT_PUNCH", "select-jab-0"); for (var w = Wait(.3f); w.MoveNext();) yield return null;
        Check(lobby.Locked[0] && !lobby.Locked[1], "1P가 잽을 하면 확정된다");
        Check(Game.Practices[0].Step == 0 && Game.Practices[0].Hits == 0, "고르는 동안의 잽은 수련으로 세지 않는다");
        Check(lobby.SelectRemaining <= RedboolPvpLobby.AfterFirstLockSeconds + .01f, "한 사람이 확정하면 남은 시간이 15초로 줄어든다 (" + lobby.SelectRemaining.ToString("F1") + ")");
        movement[0] = "RIGHT"; for (var w = Wait(.15f); w.MoveNext();) yield return null;
        movement[0] = "NEUTRAL"; for (var w = Wait(.2f); w.MoveNext();) yield return null;
        Check(lobby.Cursor[0] == 3, "확정한 뒤에는 기울여도 칸이 바뀌지 않는다");
        Shot("3_select_first_locked");
        block[0] = true; for (var w = Wait(.2f); w.MoveNext();) yield return null;
        block[0] = false; for (var w = Wait(.25f); w.MoveNext();) yield return null;
        Check(!lobby.Locked[0], "양손 막기로 확정을 취소한다");
        Gesture(0, "RIGHT_PUNCH", "select-straight-0"); for (var w = Wait(.3f); w.MoveNext();) yield return null;
        Check(lobby.Locked[0], "스트레이트로도 확정된다");
        Gesture(1, "LEFT_PUNCH", "select-jab-1"); for (var w = Wait(.3f); w.MoveNext();) yield return null;
        Check(lobby.Locked[1], "2P가 잽을 하면 확정된다");
        Shot("3b_select_both_locked");
        for (var w = Wait(RedboolPvpLobby.ConfirmSeconds + .4f); w.MoveNext();) yield return null;
        Check(lobby.Complete, "두 사람이 모두 확정하면 인물 선택이 끝난다");
        Check(Game.Characters[0] == 3 && Game.Characters[1] == 4, "고른 인물이 정해진다 (1P " + RedboolPvpRoster.Get(Game.Characters[0]).name + ", 2P " + RedboolPvpRoster.Get(Game.Characters[1]).name + ")");
        Check(VisualName(0).Contains("HiredBandit") && VisualName(1).Contains("FerryRaider"), "두 사람의 겉모습이 고른 인물로 바뀐다 (" + VisualName(0) + ", " + VisualName(1) + ")");
        for (int i = 0; i < 2; i++)
        {
            var view = Game.Players[i].GetComponent<Fighter3DView>();
            Check(view.modelAnimator != null && view.modelAnimator.isHuman && view.modelAnimator.runtimeAnimatorController != null, (i + 1) + "P의 새 겉모습이 Humanoid 동작 세트를 갖고 있다");
        }
        for (var w = Wait(.6f); w.MoveNext();) yield return null;
        Check(Game.Practices[0].Step == 0 && Game.Practices[1].Step == 0 && !Game.Fighting, "인물 선택이 끝나면 수련 첫 동작부터 시작한다");
        Shot("4_tutorial_start");

        // 수련 8동작 (두 사람이 같이)
        string[] gestures = { "LEFT_PUNCH", "RIGHT_PUNCH", "LEFT_RAISE", "RIGHT_RAISE" };
        for (int step = 0; step < 8; step++)
        {
            for (var w = Wait(.4f); w.MoveNext();) yield return null;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                for (int i = 0; i < 2; i++)
                {
                    if (step < 4) Gesture(i, gestures[step], "practice-" + i + "-" + step + "-" + attempt);
                    else if (step == 4) block[i] = true;
                    else if (step < 7) movement[i] = step == 5 ? "LEFT" : "RIGHT";
                    else Gesture(i, "SPECIAL", "practice-x-" + i + "-" + attempt);
                }
                for (var w = Wait(.15f); w.MoveNext();) yield return null;
                for (int i = 0; i < 2; i++) { block[i] = false; movement[i] = "NEUTRAL"; }
                if (step == 2 && attempt == 0) Shot("5_tutorial_step3_first_hit");
                for (var w = Wait(.5f); w.MoveNext();) yield return null;
            }
            for (int i = 0; i < 2; i++) Check(Game.Practices[i].Step == step + 1, (i + 1) + "P 수련 " + (step + 1) + "번째 동작(" + RedboolPvpTutorialCard.Names[step] + ")을 마친다");
            if (step == 6) Shot("6_tutorial_last_step");
        }
        for (var w = Wait(1.6f); w.MoveNext();) yield return null;
        Check(Game.Fighting && !Game.Paused, "두 사람이 수련을 마치면 대전이 시작된다");
        Shot("7_fight");

        // 무대와 HUD
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        Check(scene == RedboolPvp.ArenaScene, "PvP 무대가 지하 동굴 투기장이다 (" + scene + ")");
        var hud = Object.FindFirstObjectByType<CombatHUD>(FindObjectsInactive.Include);
        Check(hud != null && hud.ExternalVisible, "스토리와 같은 HUD(체력 막대·필살기 게이지·시계)가 켜져 있다");
        Check(GameObject.Find("PlayerPanel0") == null && GameObject.Find("PlayerPanel1") == null && GameObject.Find("RoundClock") == null, "예전 PvP 체력 패널과 시계는 감춰진다");
        Check(hud.ExternalSummary(true).Contains("1P") && hud.ExternalSummary(true).Contains(RedboolPvpRoster.Get(3).name) && hud.ExternalSummary(true).Contains(RedboolPvpRoster.Get(3).signature), "왼쪽 막대에 1P의 인물 이름과 필살기 이름이 나온다 (" + hud.ExternalSummary(true) + ")");
        Check(hud.ExternalSummary(false).Contains("2P") && hud.ExternalSummary(false).Contains(RedboolPvpRoster.Get(4).name) && hud.ExternalSummary(false).Contains(RedboolPvpRoster.Get(4).signature), "오른쪽 막대에 2P의 인물 이름과 필살기 이름이 나온다 (" + hud.ExternalSummary(false) + ")");
        Check(int.TryParse(hud.ExternalClockText, out int clock) && clock > 100 && clock <= 120, "시계가 남은 시간을 보여 준다 (" + hud.ExternalClockText + ")");
        for (int i = 0; i < 2; i++)
        {
            var mine = Game.Players[i].GetComponent<EnemySpecial>(); var palm = Game.Players[i].GetComponent<SpecialMove>();
            Check(mine != null && mine.enabled && mine.PlayerOwned && Game.SpecialName(i) == RedboolPvpRoster.Get(Game.Characters[i]).signature, (i + 1) + "P의 필살기가 고른 인물의 기술이다 (" + Game.SpecialName(i) + ")");
            Check(palm == null || !palm.enabled, (i + 1) + "P의 파운장은 꺼진다");
        }

        // 대전 중: 고른 인물로 움직이고 때린다
        float x0 = Game.Players[0].transform.position.x;
        movement[0] = "RIGHT"; for (var w = Wait(.6f); w.MoveNext();) yield return null;
        movement[0] = "NEUTRAL"; for (var w = Wait(.2f); w.MoveNext();) yield return null;
        Check(Game.Players[0].transform.position.x > x0 + .1f, "대전에서는 기울이면 인물이 걷는다 (" + (Game.Players[0].transform.position.x - x0).ToString("F2") + " m)");
        Gesture(0, "LEFT_PUNCH", "fight-jab-0"); for (var w = Wait(.25f); w.MoveNext();) yield return null;
        Check(Game.Players[0].GetComponent<Fighter>().IsAttacking || Game.Players[0].GetComponent<Fighter3DView>().CurrentState == "JAB", "대전에서 잽이 나간다 (state " + Game.Players[0].GetComponent<Fighter3DView>().CurrentState + ")");
        for (var w = Wait(1.2f); w.MoveNext();) yield return null;
        Shot("8_fight_moved");

        // 공격을 맞히면 그 인물의 필살기 게이지가 찬다
        var remaining = typeof(RedboolPvp).GetProperty("Remaining");
        {
            var mine = Game.Players[0].GetComponent<EnemySpecial>(); int before = mine.Charge;
            Place(0, -.45f); Place(1, .45f);
            for (var w = Wait(.2f); w.MoveNext();) yield return null;
            Gesture(0, "LEFT_PUNCH", "charge-jab-0"); for (var w = Wait(.8f); w.MoveNext();) yield return null;
            Check(mine.Charge == before + 1, "잽을 맞히면 필살기 게이지가 한 칸 찬다 (" + before + " → " + mine.Charge + ")");
            Check(hud.ExternalSummary(true).Contains(mine.Charge + " / " + mine.HitsToCharge), "게이지 숫자가 HUD에 나온다 (" + hud.ExternalSummary(true) + ")");
        }

        // 인물 11명의 필살기를 양쪽 자리에서 한 번씩 써 본다 (1P는 왼쪽, 2P는 오른쪽). 상대는 가만히 서 있는 청운이다.
        for (int index = 1; index < RedboolPvpRoster.Count; index++)
            for (int side = 0; side < 2; side++)
            {
                var who = RedboolPvpRoster.Get(index);
                string label = (side + 1) + "P " + who.name + " · " + who.signature;
                Game.ApplyCharacter(side, index); Game.ApplyCharacter(1 - side, RedboolPvpRoster.Hero);
                yield return null;
                for (int i = 0; i < 2; i++) { var h = Game.Players[i].GetComponent<Health>(); h.ResetHealth(); h.ClearStun(); }
                Place(0, start0.x); Place(1, start1.x);
                remaining.SetValue(Game, 120f);
                for (var w = Wait(.4f); w.MoveNext();) yield return null;
                var own = Game.Players[side].GetComponent<EnemySpecial>(); var targetHp = Game.Players[1 - side].GetComponent<Health>();
                Check(own != null && own.enabled && own.PlayerOwned && own.MoveName == who.signature && Game.SpecialName(side) == who.signature, label + ": 그 인물의 기술이 맡겨진다");
                Check(Game.SpecialName(1 - side) == "파운장" && Game.Players[1 - side].GetComponent<SpecialMove>().enabled, label + ": 상대(청운)는 파운장을 쓴다");
                Gesture(side, "SPECIAL", "sweep-empty-" + index + "-" + side); for (var w = Wait(.3f); w.MoveNext();) yield return null;
                Check(!own.IsPlaying, label + ": 게이지가 비어 있으면 나가지 않는다");
                own.AddCharge(own.HitsToCharge);
                int serial = own.FireSerial;
                for (var w = Wait(.25f); w.MoveNext();) yield return null;
                Gesture(side, "SPECIAL", "sweep-" + index + "-" + side);
                double until = Time.realtimeSinceStartupAsDouble + 1.5;
                while (own.FireSerial == serial && Time.realtimeSinceStartupAsDouble < until) for (var w = Wait(0f); w.MoveNext();) yield return null;
                Check(own.FireSerial == serial + 1 && own.IsPlaying, label + ": 가슴 앞 X자 동작으로 나간다");
                until = Time.realtimeSinceStartupAsDouble + 16; double actedAt = -1; bool pictured = false;
                while (own.IsPlaying && Time.realtimeSinceStartupAsDouble < until)
                {
                    if (actedAt < 0 && own.CurrentPhase == EnemySpecial.Phase.Acting) actedAt = Time.realtimeSinceStartupAsDouble;
                    if (!pictured && actedAt >= 0 && Time.realtimeSinceStartupAsDouble - actedAt > .45) { pictured = true; Shot("special_" + index.ToString("D2") + "_" + (side + 1) + "p", 960, 540); }
                    for (var w = Wait(0f); w.MoveNext();) yield return null;
                }
                Check(!own.IsPlaying && actedAt >= 0, label + ": 끝까지 진행된다");
                Check(own.LastDamage > 0 && targetHp.CurrentHealth < targetHp.MaxHealth, label + ": 상대에게 피해가 들어간다 (피해 " + own.LastDamage + ", 상대 체력 " + targetHp.CurrentHealth + ")");
                for (var w = Wait(.5f); w.MoveNext();) yield return null;
                Check(Mathf.Approximately(Time.timeScale, 1f) && Game.Fighting && !Game.Resolved, label + ": 끝난 뒤 시간과 대전 상태가 그대로다 (timeScale " + Time.timeScale + ")");
                Check(own.Charge == 0 && !own.Used && Game.Players[side].GetComponent<Fighter>().CanAttack, label + ": 쓰고 나면 게이지가 비고 다시 모을 수 있다");
            }

        // 승표탈기는 상대가 모아 둔 게이지를 비운다 (상대가 인물 고유 기술을 쓸 때도)
        {
            Game.ApplyCharacter(0, 4); Game.ApplyCharacter(1, 5); yield return null;
            for (int i = 0; i < 2; i++) { var h = Game.Players[i].GetComponent<Health>(); h.ResetHealth(); h.ClearStun(); }
            Place(0, start0.x); Place(1, start1.x); remaining.SetValue(Game, 120f);
            for (var w = Wait(.4f); w.MoveNext();) yield return null;
            var thief = Game.Players[0].GetComponent<EnemySpecial>(); var victim = Game.Players[1].GetComponent<EnemySpecial>();
            victim.AddCharge(3); thief.AddCharge(thief.HitsToCharge);
            for (var w = Wait(.25f); w.MoveNext();) yield return null;
            Check(victim.Charge == 3, "철문이 게이지를 3칸 모아 두었다");
            Gesture(0, "SPECIAL", "drain-0");
            double until = Time.realtimeSinceStartupAsDouble + 16; bool began = false;
            while ((!began || thief.IsPlaying) && Time.realtimeSinceStartupAsDouble < until) { began |= thief.IsPlaying; for (var w = Wait(0f); w.MoveNext();) yield return null; }
            Check(began && !thief.IsPlaying && thief.LastDamage > 0 && victim.Charge == 0, "승표탈기가 맞으면 상대의 필살기 게이지가 비워진다 (남은 게이지 " + victim.Charge + ")");
            for (var w = Wait(.5f); w.MoveNext();) yield return null;
        }

        if (dual)
        {
            // 손님 게임이 남긴 기록에서, 호스트가 정한 인물이 손님 화면에도 입혀졌는지 본다.
            for (var w = Wait(1.0f); w.MoveNext();) yield return null;
            StopGuest();
            string log = File.ReadAllText(Path.Combine(Output, "guest.log"));
            Check(log.Contains("1P character: " + RedboolPvpRoster.Get(3).name) && log.Contains("2P character: " + RedboolPvpRoster.Get(4).name), "손님 화면에도 두 사람이 고른 인물이 입혀진다");
            Check(!log.Contains("Exception"), "손님 게임에 실행 오류가 없다");
            Check(File.Exists(Path.Combine(Output, "guest_lobby_select.png")), "손님 화면에 인물 선택 화면이 떴다 (guest_lobby_select.png)");
            Check(File.Exists(Path.Combine(Output, "guest_lobby_tutorial.png")), "손님 화면에 수련 설명 카드가 떴다 (guest_lobby_tutorial.png)");
        }
        // 주인공으로 되돌릴 수 있다 (다시 고르기에서 쓰는 경로)
        Game.ApplyCharacter(0, RedboolPvpRoster.Hero); yield return null;
        Check(VisualName(0) == heroName && Game.Characters[0] == RedboolPvpRoster.Hero, "주인공을 고르면 원래 겉모습으로 돌아온다 (" + VisualName(0) + ")");
        for (var w = Wait(.4f); w.MoveNext();) yield return null;
    }

    static void Finish(string failure)
    {
        StopGuest(); SessionState.SetBool(Active, false); EditorApplication.update -= Tick;
        foreach (var c in clients) c?.Close();
        if (SessionState.GetBool(Active + ".HadSave", false)) PlayerPrefs.SetString(SaveKey, SessionState.GetString(Active + ".Save", "")); else PlayerPrefs.DeleteKey(SaveKey);
        PlayerPrefs.Save();
        File.WriteAllText(Path.Combine(Output, "result.txt"), (failure == null ? "PASS\n" : "FAIL\n" + failure + "\n") + checks.Count + " checks");
        EditorApplication.Exit(failure == null ? 0 : 1);
    }
}
