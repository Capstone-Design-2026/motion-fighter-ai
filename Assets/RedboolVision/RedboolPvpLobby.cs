using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// PvP에 들어온 뒤 싸우기 전까지의 흐름: 대기실 → 인물 선택 → (수련은 RedboolPvp가 그대로 진행).
///
/// 판단은 호스트만 한다. 손님 화면은 호스트가 보내 준 상태(RedboolViewWorld의 stage·cursor·locked)를 그대로 그린다.
/// 인물은 카메라 동작으로 고른다(몸을 좌우로 기울여 이동, 잽·스트레이트로 확정, 양손 막기로 취소).
/// 진행자는 호스트 키보드로도 고를 수 있다(1P: A/D·F·G, 2P: ←/→·Enter·Backspace).
///
/// 자동 검사에서는 켜지 않는다(-redbool-pvp-lobby 를 줄 때만). 그래야 기존 PvP 검사가 그대로 통한다.
/// 실제 실행에서 끄려면 --redbool-pvp-no-lobby.
/// </summary>
public sealed class RedboolPvpLobby : MonoBehaviour
{
    public enum Stage : byte { Room = 0, Select = 1, Done = 2 }

    public const float SelectSeconds = 45f, AfterFirstLockSeconds = 15f, ConfirmSeconds = 1.2f;

    public static bool Enabled
    {
        get
        {
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "--redbool-pvp-no-lobby") >= 0) return false;
            return !Application.isBatchMode || Array.IndexOf(args, "-redbool-pvp-lobby") >= 0;
        }
    }

    public Stage Current { get; private set; } = Stage.Room;
    public readonly int[] Cursor = { RedboolPvpRoster.Hero, RedboolPvpRoster.DefaultSecond };
    public readonly bool[] Locked = new bool[2];
    public float SelectRemaining { get; private set; } = SelectSeconds;
    /// <summary>인물 선택까지 끝나 수련·대전으로 넘어갔는가</summary>
    public bool Complete => Current == Stage.Done;

    RedboolPvp game;
    readonly Action<string, float, string, float>[] handlers = new Action<string, float, string, float>[2];
    readonly bool[] subscribed = new bool[2];
    readonly double[] nextMove = new double[2];
    readonly string[] lastAction = { "", "" };
    double enteredSelect, confirmSince = -1;
    bool shortened;

    // 화면
    Canvas canvas;
    GameObject roomRoot, selectRoot;
    Text roomStatus, selectTimer, selectHint;
    readonly RectTransform[] cards = new RectTransform[RedboolPvpRoster.All.Length];
    readonly RectTransform[] frames = new RectTransform[2];
    readonly Text[] frameTags = new Text[2], infoName = new Text[2], infoLine = new Text[2], infoState = new Text[2];
    readonly Image[][] frameBars = new Image[2][];
    readonly RedboolPvpTutorialCard[] tutorial = new RedboolPvpTutorialCard[2];
    static readonly Color[] SideColour = { new Color(.92f, .72f, .36f), new Color(.52f, .76f, 1f) };
    // 진단용: REDBOOL_VIEW_CAPTURE_DIR 을 준 실제 게임 창에서만, 단계마다 화면을 한 장씩 남긴다 (RedboolViewLink 의 대전 화면 캡처와 같은 폴더).
    string captureDirectory; int capturedStage = -1, watchedStage = -1; double stageSince; bool tutorialCaptured;

    public void Initialize(RedboolPvp owner)
    {
        game = owner;
        for (int i = 0; i < 2; i++) { int slot = i; handlers[i] = (action, confidence, movement, strength) => OnCamera(slot, action, confidence, movement, strength); }
        BuildUi();
        captureDirectory = Application.isBatchMode ? null : Environment.GetEnvironmentVariable("REDBOOL_VIEW_CAPTURE_DIR");
        Refresh();
    }

    void OnDestroy() { for (int i = 0; i < 2; i++) Listen(i, false); }

    // ── 호스트: 흐름 ──
    void Update()
    {
        if (game == null || !game.Configured || RedboolPvp.IsClient) return;
        double now = Time.realtimeSinceStartupAsDouble;
        switch (Current)
        {
            case Stage.Room:
                // 두 사람의 카메라가 잡히면(두 화면 대전이면 2P 화면도 연결되면) 인물 선택으로 넘어간다. F9: 진행자가 바로 넘기기.
                if (game.BothPresent && game.ViewReady || Input.GetKeyDown(KeyCode.F9)) Enter(Stage.Select);
                break;
            case Stage.Select:
                ReadKeys();
                SelectRemaining = Mathf.Max(0, SelectRemaining - Time.unscaledDeltaTime);
                if (SelectRemaining <= 0) { Locked[0] = Locked[1] = true; }
                // 한 사람이 먼저 정하면 남은 사람에게는 15초만 준다 (한 번만).
                if (!shortened && (Locked[0] || Locked[1])) { shortened = true; SelectRemaining = Mathf.Min(SelectRemaining, AfterFirstLockSeconds); }
                if (Locked[0] && Locked[1])
                {
                    if (confirmSince < 0) confirmSince = now;
                    else if (now - confirmSince >= ConfirmSeconds)
                    {
                        for (int i = 0; i < 2; i++) game.ApplyCharacter(i, Cursor[i]);
                        Debug.Log("[REDBOOL PVP LOBBY] characters 1P=" + RedboolPvpRoster.Get(Cursor[0]).name + " 2P=" + RedboolPvpRoster.Get(Cursor[1]).name);
                        Enter(Stage.Done);
                    }
                }
                else confirmSince = -1;
                break;
        }
        Refresh();
    }

    void Enter(Stage stage)
    {
        Current = stage;
        bool selecting = stage == Stage.Select;
        if (selecting)
        {
            SelectRemaining = SelectSeconds; Locked[0] = Locked[1] = false; confirmSince = -1; shortened = false;
            enteredSelect = Time.realtimeSinceStartupAsDouble; nextMove[0] = nextMove[1] = 0; lastAction[0] = lastAction[1] = "";
        }
        // 카메라 동작은 고르는 동안에만 듣는다. 듣는 동안에는 수신기가 인물을 움직이지 않는다(RedboolDemoReceiver.Practicing).
        for (int i = 0; i < 2; i++) Listen(i, selecting);
        Debug.Log("[REDBOOL PVP LOBBY] stage=" + stage);
    }

    /// <summary>대전이 끝난 뒤 인물을 다시 고른다(호스트).</summary>
    public void Reopen()
    {
        if (RedboolPvp.IsClient || Current != Stage.Done) return;
        Enter(Stage.Select);
    }

    void Listen(int slot, bool on)
    {
        var receiver = game != null ? game.Receivers[slot] : null;
        if (receiver == null || subscribed[slot] == on) return;
        if (on) receiver.PracticeAction += handlers[slot]; else receiver.PracticeAction -= handlers[slot];
        subscribed[slot] = on;
    }

    void OnCamera(int slot, string action, float confidence, string movement, float strength)
    {
        if (Current != Stage.Select || action == "UNAVAILABLE") return;
        double now = Time.realtimeSinceStartupAsDouble;
        // 좌우로 기울이면 한 칸, 계속 기울이고 있으면 0.6초마다 한 칸
        bool leaning = (movement == "LEFT" || movement == "RIGHT") && strength >= .25f;
        if (!leaning) nextMove[slot] = 0;
        else if (now >= nextMove[slot]) { Move(slot, movement == "LEFT" ? -1 : 1); nextMove[slot] = now + .6; }
        // 들어오자마자 잘못 확정되지 않게 조금 기다린다.
        bool settled = now - enteredSelect >= .8;
        bool sure = !float.IsNaN(confidence) && confidence >= .7f;
        if (settled && sure && (action == "JAB" || action == "STRAIGHT")) Lock(slot, true);
        if (action == "BLOCK" && lastAction[slot] != "BLOCK") Lock(slot, false);
        lastAction[slot] = action;
    }

    void ReadKeys()
    {
        if (Input.GetKeyDown(KeyCode.A)) Move(0, -1);
        if (Input.GetKeyDown(KeyCode.D)) Move(0, 1);
        if (Input.GetKeyDown(KeyCode.F)) Lock(0, true);
        if (Input.GetKeyDown(KeyCode.G)) Lock(0, false);
        if (Input.GetKeyDown(KeyCode.LeftArrow)) Move(1, -1);
        if (Input.GetKeyDown(KeyCode.RightArrow)) Move(1, 1);
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) Lock(1, true);
        if (Input.GetKeyDown(KeyCode.Backspace)) Lock(1, false);
    }

    public void Move(int slot, int delta)
    {
        if (Current != Stage.Select || Locked[slot]) return;
        int count = RedboolPvpRoster.Count;
        Cursor[slot] = ((Cursor[slot] + delta) % count + count) % count;
    }

    public void Lock(int slot, bool value)
    {
        if (Current != Stage.Select || Locked[slot] == value) return;
        if (!value && Locked[0] && Locked[1] && confirmSince >= 0 && Time.realtimeSinceStartupAsDouble - confirmSince > ConfirmSeconds * .5f) return;   // 넘어가기 직전에는 취소하지 않는다
        Locked[slot] = value;
    }

    // ── 손님: 호스트가 보낸 상태를 그대로 받는다 ──
    public void ShowRemote(RedboolViewFrame frame)
    {
        if (frame == null) { Refresh(); return; }
        Current = (Stage)Mathf.Clamp(frame.world.stage, 0, 2);
        for (int i = 0; i < 2; i++)
        {
            Cursor[i] = RedboolPvpRoster.Clamp(i == 0 ? frame.world.cursor0 : frame.world.cursor1);
            Locked[i] = (frame.world.locked & (1 << i)) != 0;
        }
        SelectRemaining = frame.world.selectSeconds;
        Refresh(frame);
    }

    // ── 화면 ──
    void BuildUi()
    {
        UIFactory.EnsureEventSystem();
        canvas = UIFactory.CreateCanvas("REDBOOL PvP Lobby", transform, 212);
        var centre = new Vector2(.5f, .5f);

        // 대기실
        var room = UIFactory.CreateImage("Room", canvas.transform, new Color(.02f, .035f, .045f, .92f));
        roomRoot = room.gameObject;
        UIFactory.Place(room.rectTransform, centre, centre, Vector2.zero, new Vector2(980, 420));
        Label(room.transform, "RoomTitle", "대기실", 46, TextAnchor.MiddleCenter, WuxiaTheme.Gold, new Vector2(.5f, 1), new Vector2(0, -52), new Vector2(900, 60));
        roomStatus = Label(room.transform, "RoomStatus", "", 27, TextAnchor.MiddleCenter, WuxiaTheme.Paper, centre, new Vector2(0, 0), new Vector2(900, 190));
        Label(room.transform, "RoomHint", "두 사람이 모두 준비되면 인물 선택으로 넘어갑니다   ·   진행자: F9로 바로 넘기기", 20, TextAnchor.MiddleCenter, new Color(.75f, .75f, .7f), new Vector2(.5f, 0), new Vector2(0, 40), new Vector2(940, 40));

        // 인물 선택
        var select = UIFactory.CreateImage("Select", canvas.transform, new Color(.015f, .025f, .03f, .86f));
        selectRoot = select.gameObject;
        UIFactory.Stretch(select.rectTransform);
        Label(select.transform, "SelectTitle", "대기실  ·  인물 선택", 46, TextAnchor.MiddleCenter, WuxiaTheme.Gold, new Vector2(.5f, 1), new Vector2(0, -58), new Vector2(1200, 64));
        Label(select.transform, "SelectSub", "두 사람이 각자 인물을 정하면 수련(동작 익히기)으로 넘어갑니다", 24, TextAnchor.MiddleCenter, WuxiaTheme.Paper, new Vector2(.5f, 1), new Vector2(0, -112), new Vector2(1400, 40));
        const float cardW = 236, cardH = 292, gap = 16; const int columns = 6;
        for (int i = 0; i < cards.Length; i++)
        {
            int column = i % columns, row = i / columns;
            float x = (column - (columns - 1) * .5f) * (cardW + gap), y = row == 0 ? 150 : 150 - (cardH + gap);
            var card = UIFactory.CreateImage("Card" + i, select.transform, new Color(.07f, .09f, .10f, .96f));
            cards[i] = card.rectTransform;
            UIFactory.Place(card.rectTransform, centre, centre, new Vector2(x, y), new Vector2(cardW, cardH));
            var who = RedboolPvpRoster.All[i]; var actor = RedboolPvpRoster.Actor(i);
            var portrait = new GameObject("Portrait", typeof(RectTransform)).AddComponent<RawImage>();
            portrait.transform.SetParent(card.transform, false); portrait.raycastTarget = false;
            UIFactory.Place(portrait.rectTransform, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -8), new Vector2(cardW - 16, 204));
            if (actor != null && actor.artwork != null) { portrait.texture = actor.artwork; portrait.uvRect = TopCrop(actor.artwork, actor.crop, (cardW - 16) / 204f); }
            else portrait.color = new Color(.16f, .18f, .2f);
            Label(card.transform, "Name", who.name, 24, TextAnchor.MiddleCenter, WuxiaTheme.Paper, new Vector2(.5f, 0), new Vector2(0, 52), new Vector2(cardW - 8, 34));
            Label(card.transform, "Signature", who.signature, 18, TextAnchor.MiddleCenter, new Color(.78f, .66f, .42f), new Vector2(.5f, 0), new Vector2(0, 22), new Vector2(cardW - 8, 28));
        }
        for (int s = 0; s < 2; s++)
        {
            // 고른 칸을 두르는 테두리: 1P가 안쪽, 2P가 바깥쪽이라 같은 인물 위에서도 둘 다 보인다.
            float grow = s == 0 ? 10 : 26, thick = 6;
            var frame = UIFactory.CreateRect("Cursor" + (s + 1), select.transform); frames[s] = frame;
            UIFactory.Place(frame, centre, centre, Vector2.zero, new Vector2(cardW + grow, cardH + grow));
            frameBars[s] = new Image[4];
            for (int b = 0; b < 4; b++)
            {
                var bar = UIFactory.CreateImage("Bar" + b, frame, SideColour[s]); frameBars[s][b] = bar;
                bool horizontal = b < 2;
                bar.rectTransform.anchorMin = horizontal ? new Vector2(0, b == 0 ? 1 : 0) : new Vector2(b == 2 ? 0 : 1, 0);
                bar.rectTransform.anchorMax = horizontal ? new Vector2(1, b == 0 ? 1 : 0) : new Vector2(b == 2 ? 0 : 1, 1);
                bar.rectTransform.pivot = horizontal ? new Vector2(.5f, b == 0 ? 1 : 0) : new Vector2(b == 2 ? 0 : 1, .5f);
                bar.rectTransform.anchoredPosition = Vector2.zero;
                bar.rectTransform.sizeDelta = horizontal ? new Vector2(0, thick) : new Vector2(thick, 0);
            }
            frameTags[s] = Label(frame, "Tag", (s + 1) + "P", 22, s == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight, SideColour[s], new Vector2(s == 0 ? 0 : 1, 1), new Vector2(s == 0 ? 6 : -6, 16), new Vector2(200, 30), new Vector2(s == 0 ? 0 : 1, .5f));

            // 아래 좌우: 지금 고른 인물
            float side = s == 0 ? -1 : 1;
            infoName[s] = Label(select.transform, "InfoName" + s, "", 34, TextAnchor.MiddleCenter, SideColour[s], new Vector2(.5f, 0), new Vector2(side * 560, 178), new Vector2(620, 46));
            infoLine[s] = Label(select.transform, "InfoLine" + s, "", 21, TextAnchor.MiddleCenter, WuxiaTheme.Paper, new Vector2(.5f, 0), new Vector2(side * 560, 132), new Vector2(680, 60));
            infoState[s] = Label(select.transform, "InfoState" + s, "", 24, TextAnchor.MiddleCenter, WuxiaTheme.Paper, new Vector2(.5f, 0), new Vector2(side * 560, 88), new Vector2(620, 36));
        }
        selectTimer = Label(select.transform, "Timer", "", 40, TextAnchor.MiddleCenter, WuxiaTheme.Gold, new Vector2(.5f, 0), new Vector2(0, 150), new Vector2(240, 60));
        selectHint = Label(select.transform, "Hint", "몸을 좌우로 기울여 고르기   ·   잽이나 스트레이트로 확정   ·   양손 막기로 취소\n진행자 키보드   1P: A / D · F 확정 · G 취소      2P: ← / → · Enter 확정 · Backspace 취소", 20, TextAnchor.MiddleCenter, new Color(.78f, .78f, .72f), new Vector2(.5f, 0), new Vector2(0, 34), new Vector2(1700, 64));

        // 수련 설명 카드 (화면 왼쪽 절반이 1P, 오른쪽 절반이 2P)
        for (int s = 0; s < 2; s++) tutorial[s] = RedboolPvpTutorialCard.Create(canvas.transform, s);
    }

    static Text Label(Transform parent, string name, string value, int size, TextAnchor align, Color colour, Vector2 anchor, Vector2 position, Vector2 box, Vector2? pivot = null)
    {
        var text = WuxiaTheme.Text(name, parent, value, size, align, colour);
        text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Overflow; text.raycastTarget = false;
        UIFactory.Place(text.rectTransform, anchor, pivot ?? new Vector2(.5f, .5f), position, box);
        return text;
    }

    // 그림에서 그 인물이 있는 부분(crop) 안의 위쪽(얼굴과 상체)을 칸 비율에 맞게 잘라 보여 준다.
    static Rect TopCrop(Texture texture, Rect crop, float aspect)
    {
        if (crop.width <= 0 || crop.height <= 0) crop = new Rect(0, 0, 1, 1);
        float own = texture.width * crop.width / Mathf.Max(1f, texture.height * crop.height);
        if (own >= aspect) { float w = crop.width * aspect / own; return new Rect(crop.x + (crop.width - w) * .5f, crop.y, w, crop.height); }
        float h = crop.height * own / aspect;
        return new Rect(crop.x, crop.yMax - h, crop.width, h);
    }

    void Refresh(RedboolViewFrame frame = null)
    {
        if (canvas == null) return;
        bool client = RedboolPvp.IsClient;
        roomRoot.SetActive(Current == Stage.Room);
        selectRoot.SetActive(Current == Stage.Select);

        if (Current == Stage.Room)
        {
            if (client) roomStatus.text = frame == null ? "호스트 화면 연결 대기 중" : Line("1P 카메라", frame.actors[0].live) + "\n" + Line("2P 카메라", frame.actors[1].live) + "\n2P 화면   연결됨";
            else
            {
                string view = RedboolPvp.RequireView ? "\n2P 화면   " + (game.ViewReady ? "연결됨" : "연결 대기 · 2P 노트북에서 대전 참가를 누르세요") : "";
                roomStatus.text = Line("1P 카메라", game.Receivers[0] != null && game.Receivers[0].PresenceReady) + "\n" + Line("2P 카메라", game.Receivers[1] != null && game.Receivers[1].PresenceReady) + view;
            }
        }
        else if (Current == Stage.Select)
        {
            for (int s = 0; s < 2; s++)
            {
                var who = RedboolPvpRoster.Get(Cursor[s]);
                frames[s].anchoredPosition = cards[Cursor[s]].anchoredPosition;
                float pulse = Locked[s] ? 1f : .55f + .45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 4f));
                foreach (var bar in frameBars[s]) bar.color = new Color(SideColour[s].r, SideColour[s].g, SideColour[s].b, pulse);
                frameTags[s].text = (s + 1) + "P" + (Locked[s] ? " 확정" : "");
                infoName[s].text = (s + 1) + "P   " + who.name;
                infoLine[s].text = who.line + "\n고유 필살기  " + who.signature + (Cursor[s] == RedboolPvpRoster.Hero ? "" : "  (대전에서는 아직 파운장으로 나갑니다)");
                infoState[s].text = Locked[s] ? "확정!  " + (Locked[1 - s] ? "곧 시작합니다" : "상대를 기다리는 중") : "고르는 중…";
                infoState[s].color = Locked[s] ? SideColour[s] : WuxiaTheme.Paper;
            }
            selectTimer.text = Mathf.CeilToInt(SelectRemaining).ToString();
        }

        // 수련 카드: 인물 선택이 끝나고, 그 사람의 수련이 남아 있고, 싸우는 중이 아닐 때
        for (int s = 0; s < 2; s++)
        {
            int step, hits; bool live, fighting;
            if (client)
            {
                if (frame == null) { tutorial[s].Hide(); continue; }
                step = frame.actors[s].step; hits = frame.actors[s].hits; live = frame.actors[s].live;
                fighting = frame.world.fighting && !frame.world.paused || frame.world.resolved;
            }
            else
            {
                step = game.Practices[s].Step; hits = game.Practices[s].Hits; live = game.Receivers[s] != null && game.Receivers[s].LiveReady;
                fighting = game.Fighting && !game.Paused || game.Resolved;
            }
            if (Current != Stage.Done || fighting) tutorial[s].Hide();
            else tutorial[s].Show(step, hits, live);
            if (Current == Stage.Done && !fighting && step >= 2 && step < 8 && !tutorialCaptured) { tutorialCaptured = true; Capture("tutorial"); }
        }
        double now = Time.realtimeSinceStartupAsDouble;
        if (watchedStage != (int)Current) { watchedStage = (int)Current; stageSince = now; }
        if (capturedStage != (int)Current && now - stageSince > 1.0 && Current != Stage.Done) { capturedStage = (int)Current; Capture(Current.ToString().ToLowerInvariant()); }
    }

    void Capture(string label)
    {
        if (string.IsNullOrEmpty(captureDirectory)) return;
        ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(captureDirectory, (RedboolPvp.IsClient ? "guest" : "host") + "_lobby_" + label + ".png"));
    }

    static string Line(string who, bool ready) => who + "   " + (ready ? "준비됨" : "대기 중 · 카메라 앞에 서서 C로 중앙 가드 보정");

    /// <summary>아래쪽 안내 줄(RedboolPvp의 banner)에 쓸 글. 인물 선택이 끝났으면 null.</summary>
    public string BannerText => Current == Stage.Room ? "대기실 · 두 사람의 준비를 기다리는 중 · Esc: 메뉴" : Current == Stage.Select ? "" : null;
}
