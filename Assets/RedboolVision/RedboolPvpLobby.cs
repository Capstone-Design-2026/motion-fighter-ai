using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// PvP에 들어온 뒤 싸우기 전까지의 흐름: 대기실 → 인물 선택 → (수련은 RedboolPvp가 그대로 진행).
///
/// 판단은 호스트만 한다. 손님 화면은 호스트가 보내 준 상태(RedboolViewWorld의 stage·cursor·locked)를 그대로 그린다.
/// 각자 키보드나 마우스로 고른 뒤 명시적으로 확정한다. 카메라는 선택을 바꾸지 않는다.
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
    readonly RedboolPvpSelection selection=new RedboolPvpSelection();
    public int[] Cursor => selection.Cursor;
    public bool[] Locked => selection.Locked;
    public Guid SelectionRun => selection.Run;
    public uint[] SelectionRevision => selection.Revision;
    public float SelectRemaining { get; private set; } = SelectSeconds;
    /// <summary>인물 선택까지 끝나 수련·대전으로 넘어갔는가</summary>
    public bool Complete => Current == Stage.Done;

    RedboolPvp game;
    double confirmSince = -1;
    readonly Button[] pickButtons=new Button[RedboolPvpRoster.All.Length],confirmButtons=new Button[2];
    Text selectionHint;
    int localPickerSlot;
    public int LocalSlot => RedboolPvp.IsClient?1:RedboolPvp.RequireView?0:localPickerSlot;
    bool SelectionConnected => game!=null && (RedboolPvp.IsClient
        ? game.ViewLink!=null&&game.ViewLink.Fresh : game.ViewReady);
    bool SelectionInteractive => Current==Stage.Select&&SelectionConnected&&!RedboolPauseMenu.BlocksStoryInput
        && (!RedboolPvp.IsClient||game.ViewLink!=null&&!game.ViewLink.SelectionPending);

    // 화면
    const int Columns = 4;
    static readonly KeyCode[] SelectionKeys={KeyCode.LeftArrow,KeyCode.RightArrow,KeyCode.UpArrow,KeyCode.DownArrow,
        KeyCode.A,KeyCode.D,KeyCode.W,KeyCode.S,KeyCode.Return,KeyCode.KeypadEnter,KeyCode.Backspace,KeyCode.Tab};
    const float CardW = 200, CardH = 228, Gap = 14, GridY = -40;
    static readonly Color[] SideColour = { new Color(.94f, .72f, .34f), new Color(.52f, .76f, 1f) };
    static readonly Color Ink = new Color(.045f, .06f, .065f, .96f), Dim = new Color(.74f, .73f, .68f);
    Canvas canvas;
    GameObject roomRoot, selectRoot, versusRoot;
    Text roomStatus, selectTimer, versusText,versusSub;
    GameObject roomShareRoot;
    Text roomShareStatus, roomAddressEmpty, roomAddressNotice;
    Button roomAddressRefresh, roomAddressNext;
    string[] addressCandidates = Array.Empty<string>(), addressCandidateLabels = Array.Empty<string>();
    int addressPage;
    readonly GameObject[] roomAddressRows = new GameObject[3];
    readonly Text[] roomAddressValues = new Text[3], roomAddressLabels = new Text[3];
    readonly Button[] roomAddressCopies = new Button[3];
    readonly string[] roomAddresses = new string[3];
    sealed class AddressResult { public RedboolLanAddress.Snapshot snapshot; public bool failed; }
    Task<AddressResult> addressTask;
    double nextAddressRefresh, addressCopiedUntil;
    string addressNotice = "같은 공유기에 연결한 상대에게 주소를 알려주세요", addressCopied;
    bool? roomStatusSharedLayout;
    bool AddressSharingVisible => Current == Stage.Room && !RedboolPvp.IsClient && RedboolPvp.RequireView && !RedboolPvp.Online;
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
        BuildUi();
        captureDirectory = Application.isBatchMode ? null : Environment.GetEnvironmentVariable("REDBOOL_VIEW_CAPTURE_DIR");
        Refresh();
        if (AddressSharingVisible) RequestRoomAddresses();
    }


    // ── 호스트: 흐름 ──
    void Update()
    {
        if (game == null || !game.Configured) return;
        if (RedboolPvp.IsClient) { if(Current==Stage.Select) { ReadKeys();RefreshSelectionControls(); } return; }
        double now = Time.realtimeSinceStartupAsDouble;
        TickRoomAddresses(now);
        switch (Current)
        {
            case Stage.Room:
                // 두 사람의 카메라가 잡히면(두 화면 대전이면 2P 화면도 연결되면) 인물 선택으로 넘어간다. F9: 진행자가 바로 넘기기.
                if (game.BothPresent && game.ViewReady || RedboolDemoSettings.OperatorMode && !RedboolPauseMenu.BlocksStoryInput && Input.GetKeyDown(KeyCode.F9)) Enter(Stage.Select);
                break;
            case Stage.Select:
                // Selection uses real-time, but a lost camera/view must not silently
                // choose an avatar while a participant cannot see or control the room.
                ReadKeys();
                if (!game.BothPresent || !game.ViewReady || RedboolPauseMenu.BlocksStoryInput) { confirmSince = -1; break; }
                // No timer may select or confirm on a participant's behalf.
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
            SelectRemaining = SelectSeconds; selection.Begin();confirmSince = -1;
        }
        // Camera practice remains suspended during the picker; it never moves
        // or confirms cards. Existing practice callbacks still consume inputs.
        for (int i = 0; i < 2; i++) game.Practices[i].Suspended = stage != Stage.Done;
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
        selection.ParticipantChanged(slot);confirmSince=-1;
    }

    // Kept as an explicit no-op seam for regression tests: body gestures are
    // exclusively calibration/practice/combat input, never picker controls.
    void OnCamera(int slot,string action,float confidence,string movement,float strength) { }

    public bool ApplySelection(int slot,Guid run,uint revision,int character,bool confirmed)
    {
        if(RedboolPvp.IsClient||Current!=Stage.Select||!SelectionConnected||RedboolPauseMenu.BlocksStoryInput)return false;
        bool applied=selection.Apply(slot,run,revision,character,confirmed,RedboolPvpRoster.Count);
        if(applied&&!confirmed)confirmSince=-1;
        return applied;
    }
    public bool ChooseLocal(int character)
    {
        if(!SelectionInteractive||Locked[LocalSlot])return false;
        return RedboolPvp.IsClient?game.ViewLink.RequestSelection(character,false)
            :ApplySelection(LocalSlot,SelectionRun,SelectionRevision[LocalSlot],character,false);
    }
    public bool ConfirmLocal(bool confirmed)
    {
        if(!SelectionInteractive)return false;
        return RedboolPvp.IsClient?game.ViewLink.RequestSelection(Cursor[LocalSlot],confirmed)
            :ApplySelection(LocalSlot,SelectionRun,SelectionRevision[LocalSlot],Cursor[LocalSlot],confirmed);
    }
    public bool SelectLocalSlot(int slot)
    {
        if(RedboolPvp.IsClient||RedboolPvp.RequireView||Current!=Stage.Select||slot<0||slot>1)return false;
        localPickerSlot=slot;RefreshSelectionControls();return true;
    }
    public bool SelectKey(KeyCode key)
    {
        if(!SelectionInteractive)return false;
        if(key==KeyCode.Tab)return SelectLocalSlot(1-LocalSlot);
        int delta=key==KeyCode.LeftArrow||key==KeyCode.A?-1:key==KeyCode.RightArrow||key==KeyCode.D?1
            :key==KeyCode.UpArrow||key==KeyCode.W?-Columns:key==KeyCode.DownArrow||key==KeyCode.S?Columns:0;
        if(delta!=0)return ChooseLocal(((Cursor[LocalSlot]+delta)%RedboolPvpRoster.Count+RedboolPvpRoster.Count)%RedboolPvpRoster.Count);
        if(key==KeyCode.Return||key==KeyCode.KeypadEnter)return ConfirmLocal(true);
        if(key==KeyCode.Backspace)return ConfirmLocal(false);
        return false;
    }
    void ReadKeys()
    {
        if(!SelectionInteractive)return;
        foreach(var key in SelectionKeys)
            if(Input.GetKeyDown(key))SelectKey(key);
    }
    public void Move(int slot,int delta)
    {
        if(slot<0||slot>1||Locked[slot])return;
        int count=RedboolPvpRoster.Count;
        ApplySelection(slot,SelectionRun,SelectionRevision[slot],((Cursor[slot]+delta)%count+count)%count,false);
    }
    public void Lock(int slot,bool value)
    {
        if(slot<0||slot>1)return;
        ApplySelection(slot,SelectionRun,SelectionRevision[slot],Cursor[slot],value);
    }

    // ── 손님: 호스트가 보낸 상태를 그대로 받는다 ──
    public void ShowRemote(RedboolViewFrame frame)
    {
        if (frame == null) { Refresh(); return; }
        Current = (Stage)Mathf.Clamp(frame.world.stage, 0, 2);
        selection.Remote(frame.world.selectionRun,frame.world.selectionRevision0,frame.world.selectionRevision1,
            RedboolPvpRoster.Clamp(frame.world.cursor0),RedboolPvpRoster.Clamp(frame.world.cursor1),
            (frame.world.locked&1)!=0,(frame.world.locked&2)!=0);
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
        BuildRoomAddresses(room);
        Label(room, "RoomHint", "어깨와 손목이 보이게 앉고 C 로 중앙 가드를 보정하세요", 19, TextAnchor.MiddleCenter, Dim, new Vector2(.5f, 0), new Vector2(0, 46), new Vector2(960, 36));
    }

    void BuildRoomAddresses(Transform room)
    {
        var root = UIFactory.CreateRect("HostJoinAddresses", room); roomShareRoot = root.gameObject;
        UIFactory.Place(root, new Vector2(.5f, .5f), new Vector2(.5f, 1), new Vector2(-235, 75), new Vector2(470, 240));
        Label(root, "AddressTitle", "상대가 입력할 주소", 24, TextAnchor.MiddleLeft, WuxiaTheme.Gold,
            new Vector2(0, 1), new Vector2(0, -14), new Vector2(245, 32), new Vector2(0, .5f));
        roomAddressNext = UIFactory.CreateButton("NextAddresses", root, "다른 주소", 18, new Color(.16f, .18f, .17f, 1f), NextRoomAddresses);
        UIFactory.Place(roomAddressNext.GetComponent<RectTransform>(), new Vector2(1, 1), new Vector2(1, .5f), new Vector2(-115, -14), new Vector2(90, 34));
        roomAddressNext.gameObject.SetActive(false);
        roomAddressRefresh = UIFactory.CreateButton("RefreshAddresses", root, "새로고침", 18, new Color(.16f, .18f, .17f, 1f), RequestRoomAddresses);
        UIFactory.Place(roomAddressRefresh.GetComponent<RectTransform>(), new Vector2(1, 1), new Vector2(1, .5f), new Vector2(0, -14), new Vector2(105, 34));
        roomShareStatus = Label(root, "ListenStatus", "방을 여는 중…", 18, TextAnchor.MiddleLeft, Dim,
            new Vector2(0, 1), new Vector2(0, -47), new Vector2(470, 28), new Vector2(0, .5f));
        for (int i = 0; i < roomAddressRows.Length; i++)
        {
            int slot = i;
            var row = UIFactory.CreateRect("Address" + i, root); roomAddressRows[i] = row.gameObject;
            UIFactory.Place(row, new Vector2(.5f, 1), new Vector2(.5f, .5f), new Vector2(0, -86 - i * 44), new Vector2(470, 42));
            roomAddressValues[i] = Label(row, "IPv4", "", 27, TextAnchor.MiddleLeft, WuxiaTheme.Paper,
                new Vector2(0, .5f), new Vector2(0, 0), new Vector2(245, 38), new Vector2(0, .5f));
            roomAddressValues[i].supportRichText = false;
            roomAddressLabels[i] = Label(row, "Interface", "", 16, TextAnchor.MiddleLeft, Dim,
                new Vector2(0, .5f), new Vector2(250, 0), new Vector2(125, 36), new Vector2(0, .5f));
            roomAddressLabels[i].supportRichText = false;
            roomAddressCopies[i] = UIFactory.CreateButton("Copy", row, "복사", 19, new Color(.22f, .20f, .13f, 1f), () => CopyRoomAddress(slot));
            UIFactory.Place(roomAddressCopies[i].GetComponent<RectTransform>(), new Vector2(1, .5f), new Vector2(1, .5f), Vector2.zero, new Vector2(80, 36));
            row.gameObject.SetActive(false);
        }
        roomAddressEmpty = Label(root, "NoAddress", "주소를 확인하고 있어요…", 22, TextAnchor.MiddleLeft, WuxiaTheme.Paper,
            new Vector2(0, 1), new Vector2(0, -128), new Vector2(470, 124), new Vector2(0, .5f));
        roomAddressNotice = Label(root, "AddressNotice", addressNotice, 17, TextAnchor.MiddleLeft, Dim,
            new Vector2(0, 1), new Vector2(0, -221), new Vector2(470, 36), new Vector2(0, .5f));
    }

    static AddressResult CaptureRoomAddresses()
    {
        // Background-only BCL enumeration. Never reads Unity state or touches UI.
        try { return new AddressResult { snapshot = RedboolLanAddress.Capture() }; }
        catch (Exception) { return new AddressResult { failed = true }; }
    }
    void RequestRoomAddresses()
    {
        if (!AddressSharingVisible || addressTask != null) return;
        nextAddressRefresh = Time.realtimeSinceStartupAsDouble + 5;
        addressTask = Task.Run(CaptureRoomAddresses);
        roomAddressRefresh.interactable = false; roomAddressNext.interactable = false;
        foreach (var button in roomAddressCopies) button.interactable = false;
    }
    void TickRoomAddresses(double now)
    {
        if (!AddressSharingVisible) return;
        if (addressTask != null && addressTask.IsCompleted)
        {
            // CaptureRoomAddresses handles errors, including after this object is destroyed.
            AddressResult result = addressTask.Result; addressTask = null;
            ApplyRoomAddresses(result.snapshot, result.failed);
        }
        if (addressTask == null && now >= nextAddressRefresh) RequestRoomAddresses();
    }
    void ApplyRoomAddresses(RedboolLanAddress.Snapshot snapshot, bool failed)
    {
        // Snapshot exposes defensive copies; read each array once per completed scan.
        var addresses = snapshot != null ? snapshot.Addresses : Array.Empty<string>();
        var labels = snapshot != null ? snapshot.Labels : Array.Empty<string>();
        addresses = addresses ?? Array.Empty<string>(); labels = labels ?? Array.Empty<string>();
        bool same = addresses.Length == addressCandidates.Length;
        if (same) for (int i = 0; i < addresses.Length; i++) if (addresses[i] != addressCandidates[i]) { same = false; break; }
        if (!same) { addressPage = 0; addressCopiedUntil = 0; }
        addressCandidates = addresses; addressCandidateLabels = labels;
        roomAddressEmpty.text = failed || snapshot != null && !string.IsNullOrEmpty(snapshot.Error)
            ? "주소를 확인하지 못했어요.\n연결을 확인하고 새로고침을 눌러주세요."
            : "공유할 IPv4 주소를 찾지 못했어요.\n같은 공유기나 Windows 핫스팟에 연결해주세요.";
        ShowRoomAddressPage();
        roomAddressRefresh.interactable = true; roomAddressNext.interactable = true;
    }
    void NextRoomAddresses()
    {
        if (!AddressSharingVisible || addressTask != null || addressCandidates.Length <= roomAddresses.Length) return;
        int pages = (addressCandidates.Length + roomAddresses.Length - 1) / roomAddresses.Length;
        addressPage = (addressPage + 1) % pages; addressCopiedUntil = 0;
        ShowRoomAddressPage(); RefreshRoomAddresses();
    }
    void ShowRoomAddressPage()
    {
        int start = addressPage * roomAddresses.Length;
        int count = Mathf.Min(roomAddresses.Length, Mathf.Max(0, addressCandidates.Length - start));
        for (int i = 0; i < roomAddresses.Length; i++)
        {
            int index = start + i;
            roomAddresses[i] = i < count ? addressCandidates[index] : null;
            roomAddressRows[i].SetActive(i < count);
            roomAddressValues[i].text = i < count ? addressCandidates[index] : "";
            string label = i < count && index < addressCandidateLabels.Length ? addressCandidateLabels[index] ?? "" : "";
            roomAddressLabels[i].text = label.Length > 16 ? label.Substring(0, 15) + "…" : label;
            roomAddressCopies[i].interactable = i < count;
        }
        roomAddressEmpty.gameObject.SetActive(count == 0);
        bool paged = addressCandidates.Length > roomAddresses.Length;
        roomAddressNext.gameObject.SetActive(paged);
        addressNotice = paged ? "주소 " + (start + 1) + (count > 1 ? "–" + (start + count) : "") + " / " + addressCandidates.Length + " · 같은 공유기 주소를 사용하세요"
            : "같은 공유기에 연결한 상대에게 주소를 알려주세요";
    }
    void CopyRoomAddress(int slot)
    {
        if (!AddressSharingVisible || addressTask != null || slot < 0 || slot >= roomAddresses.Length || string.IsNullOrEmpty(roomAddresses[slot])) return;
        GUIUtility.systemCopyBuffer = roomAddresses[slot];
        addressCopied = roomAddresses[slot] + " 복사됨"; addressCopiedUntil = Time.realtimeSinceStartupAsDouble + 2.5;
        RefreshRoomAddresses();
    }
    void RefreshRoomAddresses()
    {
        bool show = AddressSharingVisible;
        if (roomShareRoot.activeSelf != show) roomShareRoot.SetActive(show);
        if (!show) return;
        var link = game != null ? game.ViewLink : null;
        string status = link == null || !link.Bound
            ? link != null && link.PortConflict ? "방을 열지 못했어요 · 다른 게임을 닫아주세요" : "방을 여는 중…"
            : game.ViewReady ? "상대 화면이 연결됐어요" : "상대의 참가를 기다리고 있어요";
        if (roomShareStatus.text != status) roomShareStatus.text = status;
        string notice = Time.realtimeSinceStartupAsDouble < addressCopiedUntil ? addressCopied : addressNotice;
        if (roomAddressNotice.text != notice) roomAddressNotice.text = notice;
    }

    void BuildSelect()
    {
        var centre = new Vector2(.5f, .5f);
        var select = UIFactory.CreateImage("Select", canvas.transform, new Color(.012f, .02f, .024f, .90f));
        selectRoot = select.gameObject;
        UIFactory.Stretch(select.rectTransform);
        Label(select.transform, "SelectTitle", "인 물  선 택", 48, TextAnchor.MiddleCenter, WuxiaTheme.Gold, new Vector2(.5f, 1), new Vector2(0, -50), new Vector2(900, 64));
        Label(select.transform, "SelectSub", "각자 한 명을 정하면 수련(동작 익히기)으로 넘어갑니다", 22, TextAnchor.MiddleCenter, Dim, new Vector2(.5f, 1), new Vector2(0, -100), new Vector2(1100, 34));
        selectTimer = Label(select.transform, "Timer", "선택한 인물을 확인하고 선택 완료를 누르세요", 22, TextAnchor.MiddleCenter, WuxiaTheme.Paper, new Vector2(.5f, 1), new Vector2(0, -156), new Vector2(1050, 44));

        // 가운데: 12명 (4 x 3)
        for (int i = 0; i < cards.Length; i++)
        {
            int column = i % Columns, row = i / Columns;
            float x = (column - (Columns - 1) * .5f) * (CardW + Gap), y = (1 - row) * (CardH + Gap) + GridY;
            var card = UIFactory.CreateImage("Card" + i, select.transform, new Color(.075f, .09f, .095f, .97f));
            cards[i] = card.rectTransform;
            int character=i;
            card.raycastTarget=true;pickButtons[i]=card.gameObject.AddComponent<RedboolPvpSelectionButton>();
            pickButtons[i].targetGraphic=card;pickButtons[i].onClick.AddListener(()=>ChooseLocal(character));
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
            bigState[s] = Label(panel, "State", "", 22, TextAnchor.MiddleCenter, WuxiaTheme.Paper, new Vector2(.5f, 0), new Vector2(0, 78), new Vector2(400, 32));
            int slot=s;
            var chooseSide=bigPortrait[s].gameObject.AddComponent<RedboolPvpSelectionButton>();
            bigPortrait[s].raycastTarget=true;chooseSide.targetGraphic=bigPortrait[s];
            chooseSide.onClick.AddListener(()=>SelectLocalSlot(slot));
            confirmButtons[s]=CreateSelectionButton("ConfirmSelection"+(s+1),panel,"선택 완료",24,WuxiaTheme.Ink,
                ()=> { if(!RedboolPvp.IsClient&&!RedboolPvp.RequireView)SelectLocalSlot(slot); if(slot==LocalSlot)ConfirmLocal(!Locked[slot]); });
            UIFactory.Place(confirmButtons[s].GetComponent<RectTransform>(),new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,12),new Vector2(340,46));
            WuxiaTheme.StyleButton(confirmButtons[s]);
        }

        // 둘 다 정했을 때 가운데에 뜨는 맞대결 표시. 인물 칸 위에 겹쳐 뜨므로 완전히 불투명하게 한다(조금만 비쳐도 뒤의 글이 읽힌다).
        var versus = Plaque(select.transform, "Versus", centre, centre, new Vector2(0, GridY), new Vector2(880, 210), WuxiaTheme.Vermilion, 1f);
        versusRoot = versus.gameObject;
        versusText = Label(versus, "VersusText", "", 40, TextAnchor.MiddleCenter, WuxiaTheme.Paper, centre, new Vector2(0, 18), new Vector2(840, 110));
        versusText.supportRichText = true;
        versusSub=Label(versus, "VersusSub", "곧 수련을 시작합니다", 22, TextAnchor.MiddleCenter, Dim, new Vector2(.5f, 0), new Vector2(0, 38), new Vector2(800, 32));

        selectionHint=Label(select.transform, "Hint", "", 20, TextAnchor.MiddleCenter, Dim, new Vector2(.5f, 0), new Vector2(0, 50), new Vector2(1500, 34));
    }

    static Button CreateSelectionButton(string name,Transform parent,string label,int size,Color colour,UnityEngine.Events.UnityAction click)
    {
        var image=UIFactory.CreateImage(name,parent,colour,true);
        var button=image.gameObject.AddComponent<RedboolPvpSelectionButton>();button.targetGraphic=image;
        var text=WuxiaTheme.Text("Label",image.transform,label,size,TextAnchor.MiddleCenter,WuxiaTheme.Paper);
        UIFactory.Stretch(text.rectTransform);text.raycastTarget=false;button.onClick.AddListener(click);
        return button;
    }

    void RefreshSelectionControls()
    {
        bool localLocked=Locked[LocalSlot],available=SelectionInteractive;
        foreach(var button in pickButtons)if(button!=null)button.interactable=available&&!localLocked;
        for(int slot=0;slot<2;slot++)if(confirmButtons[slot]!=null)
        {
            confirmButtons[slot].gameObject.SetActive(slot==LocalSlot||!RedboolPvp.IsClient&&!RedboolPvp.RequireView);
            confirmButtons[slot].interactable=available;
            confirmButtons[slot].GetComponentInChildren<Text>().text=Locked[slot]?"다시 고르기":"선택 완료";
        }
        if(selectionHint!=null)selectionHint.text=!SelectionConnected?"연결을 기다리고 있어요 · 선택은 그대로 유지됩니다"
            :RedboolPvp.IsClient&&game.ViewLink!=null&&game.ViewLink.SelectionPending?"선택을 적용하고 있어요"
            :!RedboolPvp.IsClient&&!RedboolPvp.RequireView?(LocalSlot+1)+"P 선택 중 · 그림 또는 Tab으로 사람 바꾸기 · 방향키 / 마우스 · 선택 완료 / Enter"
            :"방향키 또는 마우스로 고르기   ·   선택 완료 / Enter   ·   다시 고르기 / Backspace";
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

        RefreshRoomAddresses();
        if (Current == Stage.Room)
        {
            bool sharing = AddressSharingVisible;
            if (!roomStatusSharedLayout.HasValue || roomStatusSharedLayout.Value != sharing)
            {
                roomStatusSharedLayout = sharing; roomStatus.fontSize = sharing ? 24 : 28;
                UIFactory.Place(roomStatus.rectTransform, new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                    new Vector2(sharing ? 255 : 0, -22), new Vector2(sharing ? 440 : 940, 200));
            }
            if (client) roomStatus.text = frame == null ? Row("호스트 화면", false, "연결 대기 중") : Row("1P 카메라", frame.actors[0].live, null) + "\n" + Row("2P 카메라", frame.actors[1].live, null) + "\n" + Row("2P 화면", true, null);
            else
            {
                string view = RedboolPvp.RequireView ? "\n" + Row("2P 화면", game.ViewReady, "대전 참가 대기") : "";
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
            selectTimer.text = both?"두 사람의 선택을 확인했어요":"각자 선택 완료를 누르면 수련을 시작합니다";
            RefreshSelectionControls();
            if (versusRoot.activeSelf != both) versusRoot.SetActive(both);
            if(both)versusSub.text=client?(frame!=null&&frame.actors[0].live&&frame.actors[1].live?"곧 수련을 시작합니다":"두 사람의 손목과 팔꿈치를 카메라에 보여주세요")
                :game.BothPresent&&game.ViewReady?"곧 수련을 시작합니다":"두 사람의 손목과 팔꿈치를 카메라에 보여주세요";
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
            else {
                bool bothComplete=client?frame.actors[0].step>=8&&frame.actors[1].step>=8:game.Practices[0].Complete&&game.Practices[1].Complete;
                byte gate=client?frame.world.gate:game.Gate;
                float countdown=client?frame.world.countdown:game.Countdown;
                tutorial[s].Show(step,hits,live,RedboolPvpRoster.Get(game.Characters[s]),game.SpecialName(s),personal,
                    RedboolPvp.PracticeMessage(step,hits,bothComplete,gate,countdown,live));
            }
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
        pendingCaptures.Add(new System.Collections.Generic.KeyValuePair<string, double>(label, Time.realtimeSinceStartupAsDouble + delay));
    }
    // 필살기 한 번에 여러 장(시작·발동·적중·끝)을 남길 수 있게 예약을 줄로 둔다. 한 프레임에 한 장만 찍는다.
    readonly System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, double>> pendingCaptures = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, double>>();

    void LateUpdate()
    {
        double now = Time.realtimeSinceStartupAsDouble;
        for (int i = 0; i < pendingCaptures.Count; i++)
            if (now >= pendingCaptures[i].Value) { Capture(pendingCaptures[i].Key); pendingCaptures.RemoveAt(i); return; }
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
