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

    public const float SelectSeconds = 45f, AfterFirstLockSeconds = 15f, ConfirmSeconds = 1.6f;

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
    const int Columns = 4;
    const float CardW = 200, CardH = 228, Gap = 14, GridY = -40;
    static readonly Color[] SideColour = { new Color(.94f, .72f, .34f), new Color(.52f, .76f, 1f) };
    static readonly Color Ink = new Color(.045f, .06f, .065f, .96f), Dim = new Color(.74f, .73f, .68f);
    Canvas canvas;
    GameObject roomRoot, selectRoot, versusRoot;
    Text roomStatus, selectTimer, versusText;
    readonly RectTransform[] cards = new RectTransform[RedboolPvpRoster.All.Length];
    readonly Texture[] portraits = new Texture[RedboolPvpRoster.All.Length];
    readonly Rect[] cardCrop = new Rect[RedboolPvpRoster.All.Length], bigCrop = new Rect[RedboolPvpRoster.All.Length];
    readonly RectTransform[] frames = new RectTransform[2];
    readonly Image[][] frameBars = new Image[2][];
    readonly Text[] frameTags = new Text[2];
    // 좌우의 큰 소개 칸
    readonly RawImage[] bigPortrait = new RawImage[2];
    readonly Text[] bigName = new Text[2], bigTitle = new Text[2], bigLine = new Text[2], bigSpecial = new Text[2], bigSpecialLine = new Text[2], bigState = new Text[2];
    readonly int[] shownPick = { -1, -1 };
    readonly RedboolPvpTutorialCard[] tutorial = new RedboolPvpTutorialCard[2];
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
                if (game.BothPresent && game.ViewReady || RedboolDemoSettings.OperatorMode && !RedboolPauseMenu.BlocksStoryInput && Input.GetKeyDown(KeyCode.F9)) Enter(Stage.Select);
                break;
            case Stage.Select:
                // Selection uses real-time, but a lost camera/view must not silently
                // choose an avatar while a participant cannot see or control the room.
                if (!game.BothPresent || !game.ViewReady || RedboolPauseMenu.BlocksStoryInput) { confirmSince = -1; break; }
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
        for (int i = 0; i < 2; i++) { game.Practices[i].Suspended = stage != Stage.Done; Listen(i, selecting); }
        Debug.Log("[REDBOOL PVP LOBBY] stage=" + stage);
    }

    /// <summary>대전이 끝난 뒤 인물을 다시 고른다(호스트).</summary>
    public void Reopen()
    {
        if (RedboolPvp.IsClient || Current != Stage.Done || game == null || game.Fighting || !game.Paused) return;
        Enter(Stage.Select);
    }

    /// <summary>A new calibration/person may keep the selected card, but must confirm again.</summary>
    public void ParticipantChanged(int slot)
    {
        if (slot < 0 || slot > 1 || RedboolPvp.IsClient || Current != Stage.Select) return;
        Locked[slot] = false; confirmSince = -1; shortened = false;
        SelectRemaining = SelectSeconds; nextMove[slot] = 0; lastAction[slot] = "";
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
        if (Current != Stage.Select || RedboolPauseMenu.BlocksStoryInput) return;
        if (action == "UNAVAILABLE") { nextMove[slot] = 0; lastAction[slot] = ""; return; }
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
        if (!RedboolDemoSettings.OperatorMode || RedboolPauseMenu.BlocksStoryInput) return;
        if (Input.GetKeyDown(KeyCode.A)) Move(0, -1);
        if (Input.GetKeyDown(KeyCode.D)) Move(0, 1);
        if (Input.GetKeyDown(KeyCode.W)) Move(0, -Columns);
        if (Input.GetKeyDown(KeyCode.S)) Move(0, Columns);
        if (Input.GetKeyDown(KeyCode.F)) Lock(0, true);
        if (Input.GetKeyDown(KeyCode.G)) Lock(0, false);
        if (Input.GetKeyDown(KeyCode.LeftArrow)) Move(1, -1);
        if (Input.GetKeyDown(KeyCode.RightArrow)) Move(1, 1);
        if (Input.GetKeyDown(KeyCode.UpArrow)) Move(1, -Columns);
        if (Input.GetKeyDown(KeyCode.DownArrow)) Move(1, Columns);
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) Lock(1, true);
        if (Input.GetKeyDown(KeyCode.Backspace)) Lock(1, false);
    }

    public void Move(int slot, int delta)
    {
        if (slot < 0 || slot > 1 || RedboolPvp.IsClient || Current != Stage.Select || Locked[slot]) return;
        int count = RedboolPvpRoster.Count;
        Cursor[slot] = ((Cursor[slot] + delta) % count + count) % count;
    }

    public void Lock(int slot, bool value)
    {
        if (slot < 0 || slot > 1 || RedboolPvp.IsClient || Current != Stage.Select || Locked[slot] == value) return;
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

    // ── 화면 만들기 ──
    void BuildUi()
    {
        UIFactory.EnsureEventSystem();
        canvas = UIFactory.CreateCanvas("REDBOOL PvP Lobby", transform, 212);
        for (int i = 0; i < portraits.Length; i++)
        {
            var actor = RedboolPvpRoster.Actor(i);
            if (actor == null || actor.artwork == null) continue;
            portraits[i] = actor.artwork;
            cardCrop[i] = TopCrop(actor.artwork, actor.crop, (CardW - 16) / 160f);
            bigCrop[i] = TopCrop(actor.artwork, actor.crop, 390f / 350f);
        }
        BuildRoom();
        BuildSelect();
        // 수련 설명 카드 (화면 왼쪽 절반이 1P, 오른쪽 절반이 2P)
        for (int s = 0; s < 2; s++) tutorial[s] = RedboolPvpTutorialCard.Create(canvas.transform, s);
    }

    void BuildRoom()
    {
        var centre = new Vector2(.5f, .5f);
        var room = Plaque(canvas.transform, "Room", centre, centre, Vector2.zero, new Vector2(1040, 470), WuxiaTheme.Gold);
        roomRoot = room.gameObject;
        Label(room, "RoomTitle", "대 기 실", 50, TextAnchor.MiddleCenter, WuxiaTheme.Gold, new Vector2(.5f, 1), new Vector2(0, -62), new Vector2(900, 64));
        Label(room, "RoomSub", "두 사람이 모두 준비되면 인물을 고릅니다", 22, TextAnchor.MiddleCenter, Dim, new Vector2(.5f, 1), new Vector2(0, -112), new Vector2(900, 34));
        roomStatus = Label(room, "RoomStatus", "", 28, TextAnchor.MiddleCenter, WuxiaTheme.Paper, centre, new Vector2(0, -22), new Vector2(940, 200));
        roomStatus.lineSpacing = 1.35f;
        Label(room, "RoomHint", "어깨와 손목이 보이게 앉고 C 로 중앙 가드를 보정하세요", 19, TextAnchor.MiddleCenter, Dim, new Vector2(.5f, 0), new Vector2(0, 46), new Vector2(960, 36));
    }

    void BuildSelect()
    {
        var centre = new Vector2(.5f, .5f);
        var select = UIFactory.CreateImage("Select", canvas.transform, new Color(.012f, .02f, .024f, .90f));
        selectRoot = select.gameObject;
        UIFactory.Stretch(select.rectTransform);
        Label(select.transform, "SelectTitle", "인 물  선 택", 48, TextAnchor.MiddleCenter, WuxiaTheme.Gold, new Vector2(.5f, 1), new Vector2(0, -50), new Vector2(900, 64));
        Label(select.transform, "SelectSub", "각자 한 명을 정하면 수련(동작 익히기)으로 넘어갑니다", 22, TextAnchor.MiddleCenter, Dim, new Vector2(.5f, 1), new Vector2(0, -100), new Vector2(1100, 34));
        selectTimer = Label(select.transform, "Timer", "", 50, TextAnchor.MiddleCenter, WuxiaTheme.Paper, new Vector2(.5f, 1), new Vector2(0, -156), new Vector2(200, 64));
        selectTimer.font = WuxiaTheme.DisplayFont;

        // 가운데: 12명 (4 x 3)
        for (int i = 0; i < cards.Length; i++)
        {
            int column = i % Columns, row = i / Columns;
            float x = (column - (Columns - 1) * .5f) * (CardW + Gap), y = (1 - row) * (CardH + Gap) + GridY;
            var card = UIFactory.CreateImage("Card" + i, select.transform, new Color(.075f, .09f, .095f, .97f));
            cards[i] = card.rectTransform;
            UIFactory.Place(card.rectTransform, centre, centre, new Vector2(x, y), new Vector2(CardW, CardH));
            var who = RedboolPvpRoster.All[i];
            var portrait = Picture(card.transform, "Portrait", new Vector2(.5f, 1), new Vector2(0, -8), new Vector2(CardW - 16, 160));
            if (portraits[i] != null) { portrait.texture = portraits[i]; portrait.uvRect = cardCrop[i]; } else portrait.color = new Color(.16f, .18f, .2f);
            Label(card.transform, "Name", who.name, 22, TextAnchor.MiddleCenter, WuxiaTheme.Paper, new Vector2(.5f, 0), new Vector2(0, 40), new Vector2(CardW - 8, 30));
            Label(card.transform, "Style", who.style, 16, TextAnchor.MiddleCenter, new Color(.80f, .67f, .42f), new Vector2(.5f, 0), new Vector2(0, 16), new Vector2(CardW - 8, 24));
        }

        for (int s = 0; s < 2; s++)
        {
            // 고른 칸을 두르는 테두리: 1P가 안쪽, 2P가 바깥쪽이라 같은 인물 위에서도 둘 다 보인다.
            float grow = s == 0 ? 8 : 22, thick = 6;
            var frame = UIFactory.CreateRect("Cursor" + (s + 1), select.transform); frames[s] = frame;
            UIFactory.Place(frame, centre, centre, Vector2.zero, new Vector2(CardW + grow, CardH + grow));
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
            frameTags[s] = Label(frame, "Tag", (s + 1) + "P", 20, s == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight, SideColour[s], new Vector2(s == 0 ? 0 : 1, 1), new Vector2(s == 0 ? 6 : -6, 14), new Vector2(160, 26), new Vector2(s == 0 ? 0 : 1, .5f));

            // 좌우: 지금 고른 인물의 큰 소개
            var side = new Vector2(s == 0 ? 0 : 1, .5f);
            var panel = Plaque(select.transform, "Pick" + (s + 1), side, side, new Vector2(s == 0 ? 40 : -40, -20), new Vector2(430, 780), SideColour[s]);
            Label(panel, "Who", (s + 1) + "P", 34, TextAnchor.MiddleCenter, SideColour[s], new Vector2(.5f, 1), new Vector2(0, -36), new Vector2(380, 44));
            bigPortrait[s] = Picture(panel, "BigPortrait", new Vector2(.5f, 1), new Vector2(0, -62), new Vector2(390, 350));
            bigName[s] = Label(panel, "Name", "", 38, TextAnchor.MiddleCenter, WuxiaTheme.Paper, new Vector2(.5f, 1), new Vector2(0, -444), new Vector2(400, 48));
            bigTitle[s] = Label(panel, "Title", "", 20, TextAnchor.MiddleCenter, new Color(.84f, .70f, .44f), new Vector2(.5f, 1), new Vector2(0, -482), new Vector2(400, 28));
            bigLine[s] = Label(panel, "Line", "", 20, TextAnchor.UpperCenter, WuxiaTheme.Paper, new Vector2(.5f, 1), new Vector2(0, -510), new Vector2(380, 56));
            bigSpecial[s] = Label(panel, "Special", "", 25, TextAnchor.MiddleCenter, WuxiaTheme.Gold, new Vector2(.5f, 1), new Vector2(0, -598), new Vector2(400, 34));
            bigSpecialLine[s] = Label(panel, "SpecialLine", "", 18, TextAnchor.UpperCenter, Dim, new Vector2(.5f, 1), new Vector2(0, -624), new Vector2(384, 76));
            bigState[s] = Label(panel, "State", "", 28, TextAnchor.MiddleCenter, WuxiaTheme.Paper, new Vector2(.5f, 0), new Vector2(0, 38), new Vector2(400, 40));
        }

        // 둘 다 정했을 때 가운데에 뜨는 맞대결 표시. 인물 칸 위에 겹쳐 뜨므로 완전히 불투명하게 한다(조금만 비쳐도 뒤의 글이 읽힌다).
        var versus = Plaque(select.transform, "Versus", centre, centre, new Vector2(0, GridY), new Vector2(880, 210), WuxiaTheme.Vermilion, 1f);
        versusRoot = versus.gameObject;
        versusText = Label(versus, "VersusText", "", 40, TextAnchor.MiddleCenter, WuxiaTheme.Paper, centre, new Vector2(0, 18), new Vector2(840, 110));
        versusText.supportRichText = true;
        Label(versus, "VersusSub", "곧 수련을 시작합니다", 22, TextAnchor.MiddleCenter, Dim, new Vector2(.5f, 0), new Vector2(0, 38), new Vector2(800, 32));

        Label(select.transform, "Hint", "몸을 좌우로 기울여 고르기   ·   잽이나 스트레이트로 확정   ·   양손 막기로 취소", 19, TextAnchor.MiddleCenter, Dim, new Vector2(.5f, 0), new Vector2(0, 50), new Vector2(1500, 34));
    }

    // 먹빛 판에 색 테두리를 두른 칸 (메뉴의 PvP 칸과 같은 모양)
    static RectTransform Plaque(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size, Color frame, float opacity = -1f)
    {
        var root = UIFactory.CreateRect(name, parent);
        UIFactory.Place(root, anchor, pivot, position, size);
        var body = WuxiaTheme.Ornament("Ink", root, WuxiaGraphic.Shape.Plaque, opacity < 0f ? Ink : new Color(Ink.r, Ink.g, Ink.b, opacity)); body.raycastTarget = false; UIFactory.Stretch(body.rectTransform);
        var border = WuxiaTheme.Ornament("Frame", root, WuxiaGraphic.Shape.PlaqueFrame, new Color(frame.r, frame.g, frame.b, .9f)); border.raycastTarget = false; UIFactory.Stretch(border.rectTransform);
        return root;
    }

    static Text Label(Transform parent, string name, string value, int size, TextAnchor align, Color colour, Vector2 anchor, Vector2 position, Vector2 box, Vector2? pivot = null)
    {
        var text = WuxiaTheme.Text(name, parent, value, size, align, colour);
        text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Overflow; text.raycastTarget = false;
        UIFactory.Place(text.rectTransform, anchor, pivot ?? new Vector2(.5f, align == TextAnchor.UpperCenter ? 1 : .5f), position, box);
        return text;
    }

    static RawImage Picture(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 box)
    {
        var picture = new GameObject(name, typeof(RectTransform)).AddComponent<RawImage>();
        picture.transform.SetParent(parent, false); picture.raycastTarget = false;
        UIFactory.Place(picture.rectTransform, anchor, new Vector2(.5f, 1), position, box);
        return picture;
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

    // ── 화면 갱신 ──
    void Refresh(RedboolViewFrame frame = null)
    {
        if (canvas == null) return;
        bool client = RedboolPvp.IsClient;
        roomRoot.SetActive(Current == Stage.Room);
        selectRoot.SetActive(Current == Stage.Select);

        if (Current == Stage.Room)
        {
            if (client) roomStatus.text = frame == null ? Row("호스트 화면", false, "연결 대기 중") : Row("1P 카메라", frame.actors[0].live, null) + "\n" + Row("2P 카메라", frame.actors[1].live, null) + "\n" + Row("2P 화면", true, null);
            else
            {
                string view = RedboolPvp.RequireView ? "\n" + Row("2P 화면", game.ViewReady, "2P 노트북에서 대전 참가를 누르세요") : "";
                roomStatus.text = Row("1P 카메라", game.Receivers[0] != null && game.Receivers[0].PresenceReady, null) + "\n" + Row("2P 카메라", game.Receivers[1] != null && game.Receivers[1].PresenceReady, null) + view;
            }
        }
        else if (Current == Stage.Select)
        {
            bool both = Locked[0] && Locked[1];
            for (int s = 0; s < 2; s++)
            {
                var who = RedboolPvpRoster.Get(Cursor[s]);
                frames[s].anchoredPosition = cards[Cursor[s]].anchoredPosition;
                float pulse = Locked[s] ? 1f : .5f + .5f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 4f));
                foreach (var bar in frameBars[s]) bar.color = new Color(SideColour[s].r, SideColour[s].g, SideColour[s].b, pulse);
                frameTags[s].text = (s + 1) + "P" + (Locked[s] ? " 확정" : "");
                if (shownPick[s] != Cursor[s])
                {
                    shownPick[s] = Cursor[s];
                    bool has = portraits[Cursor[s]] != null;
                    bigPortrait[s].texture = has ? portraits[Cursor[s]] : null; bigPortrait[s].color = has ? Color.white : new Color(.16f, .18f, .2f);
                    if (has) bigPortrait[s].uvRect = bigCrop[Cursor[s]];
                    bigName[s].text = who.name;
                    bigTitle[s].text = who.title + "   ·   " + who.style;
                    bigLine[s].text = who.line;
                    bigSpecial[s].text = "필살기   " + who.signature;
                    bigSpecialLine[s].text = who.specialLine;
                }
                bigState[s].text = Locked[s] ? "확  정" : "고르는 중…";
                bigState[s].color = Locked[s] ? SideColour[s] : Dim;
            }
            selectTimer.text = Mathf.CeilToInt(SelectRemaining).ToString("D2");
            selectTimer.color = SelectRemaining <= 10f ? new Color(1f, .42f, .3f) : WuxiaTheme.Paper;
            if (versusRoot.activeSelf != both) versusRoot.SetActive(both);
            if (both) versusText.text = "<color=#F0B857>" + RedboolPvpRoster.Get(Cursor[0]).name + "</color>      대      <color=#85C2FF>" + RedboolPvpRoster.Get(Cursor[1]).name + "</color>";
        }

        // 수련 카드: 인물 선택이 끝나고, 그 사람의 수련이 남아 있고, 싸우는 중이 아닐 때
        for (int s = 0; s < 2; s++)
        {
            int step, hits, personal; bool live, fighting;
            if (client)
            {
                if (frame == null) { tutorial[s].Hide(); continue; }
                step = frame.actors[s].step; hits = frame.actors[s].hits; live = frame.actors[s].live; personal = frame.actors[s].personal;
                fighting = frame.world.fighting && !frame.world.paused || frame.world.resolved;
            }
            else
            {
                step = game.Practices[s].Step; hits = game.Practices[s].Hits; live = game.Receivers[s] != null && game.Receivers[s].LiveReady;
                string status = game.Receivers[s] != null ? game.Receivers[s].PersonalStatus : "";
                personal = status.Contains("PERSONAL RANGE") ? 2 : status.Contains("CYCLES") ? 1 : 0;
                fighting = game.Fighting && !game.Paused || game.Resolved;
            }
            if (Current != Stage.Done || fighting) tutorial[s].Hide();
            else tutorial[s].Show(step, hits, live, RedboolPvpRoster.Get(game.Characters[s]), game.SpecialName(s), personal);
            if (Current == Stage.Done && !fighting && step >= 2 && step < 8 && !tutorialCaptured) { tutorialCaptured = true; Capture("tutorial"); }
        }
        double now = Time.realtimeSinceStartupAsDouble;
        if (watchedStage != (int)Current) { watchedStage = (int)Current; stageSince = now; }
        if (capturedStage != (int)Current && now - stageSince > 1.0 && Current != Stage.Done) { capturedStage = (int)Current; Capture(Current.ToString().ToLowerInvariant()); }
    }

    /// <summary>진단용: 잠시 뒤 화면을 한 장 남긴다 (REDBOOL_VIEW_CAPTURE_DIR 을 준 게임 창에서만).</summary>
    public void CaptureAfter(string label, float delay)
    {
        if (string.IsNullOrEmpty(captureDirectory)) return;
        pendingCapture = label; pendingCaptureAt = Time.realtimeSinceStartupAsDouble + delay;
    }
    string pendingCapture; double pendingCaptureAt;

    void LateUpdate()
    {
        if (pendingCapture == null || Time.realtimeSinceStartupAsDouble < pendingCaptureAt) return;
        Capture(pendingCapture); pendingCapture = null;
    }

    void Capture(string label)
    {
        if (string.IsNullOrEmpty(captureDirectory)) return;
        ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(captureDirectory, (RedboolPvp.IsClient ? "guest" : "host") + "_lobby_" + label + ".png"));
    }

    static string Row(string who, bool ready, string waiting) =>
        (ready ? "<color=#7BD88F>●</color>   " : "<color=#8C8C86>○</color>   ") + who + "      " + (ready ? "준비됨" : waiting ?? "대기 중");

    /// <summary>아래쪽 안내 줄(RedboolPvp의 banner)에 쓸 글. 인물 선택이 끝났으면 null.</summary>
    public string BannerText => Current == Stage.Room ? "" : Current == Stage.Select ? "" : null;
}
