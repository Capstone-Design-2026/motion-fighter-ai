using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 산속 수련장 배경과 왼쪽 메뉴를 조합한 시작 화면.
/// Sprite 필드는 기본 배경/버튼을 교체할 때 사용한다.
/// </summary>
public class StartSceneUI : MonoBehaviour
{
    [Header("디자인 (비워두면 무협 기본 배경)")]
    [SerializeField] private Sprite backgroundSprite;
    [SerializeField] private Sprite buttonSprite;

    [Header("색")]
    [SerializeField] private Color buttonColor     = new Color(0.12f, 0.17f, 0.18f, 0.85f);

    [Header("배치")]
    [SerializeField] private Vector2 buttonSize = new Vector2(360f, 72f);
    [SerializeField] private float buttonSpacing = 18f;
    [SerializeField] private int buttonFontSize = 28;

    [Header("배경 움직임")]
    [SerializeField] private bool animateBackground = true;
    [Range(0,2)] [SerializeField] private float backgroundMotion = 1f;

    [SerializeField] private bool showClouds = true;
    [Range(0,2)] [SerializeField] private float cloudSpeed = 1f;

    [Header("음악 (RunConfig.titleMusic)")]
    [Range(0f, 1f)] [SerializeField] private float musicVolume = 0.5f;
    [Tooltip("시작할 때 음악이 커지는 시간(초).")]
    [Min(0f)] [SerializeField] private float musicFadeIn = 2f;

    private Canvas canvas;
    private Button continueButton;
    private Text messageText;
    private RectTransform pvpPanel;
    private InputField hostInput;
    private Text pvpMessage;
    private Button findRoomButton;
    private Coroutine roomSearch;
    private UdpClient discoverySocket;

    [Serializable]
    private sealed class RoomReply
    {
        public string service;
        public int view_version, view_port, p2_port;
    }
    private AudioSource music;

    void Start()
    {
        UIFactory.EnsureEventSystem();
        Build();
        RefreshContinue();
        ShowLastMessage();
        PlayMusic();
    }

    void Update()
    {
        if (music != null && music.volume < musicVolume)
            music.volume = musicFadeIn > 0f ? Mathf.MoveTowards(music.volume, musicVolume, musicVolume / musicFadeIn * Time.unscaledDeltaTime) : musicVolume;
    }

    private void PlayMusic()
    {
        var config = RunState.Instance != null ? RunState.Instance.Config : null;
        if (config == null || config.titleMusic == null) return;
        music = gameObject.AddComponent<AudioSource>();
        music.clip = config.titleMusic;
        music.loop = true;
        music.playOnAwake = false;
        music.spatialBlend = 0f;
        music.volume = musicFadeIn > 0f ? 0f : musicVolume;
        music.Play();
    }

    // ─────────────────────── 화면 구성 ───────────────────────

    private void Build()
    {
        canvas = UIFactory.CreateCanvas("StartCanvas", transform, 0);

        var backdrop=WuxiaBackdrop.Create(canvas.transform,backgroundSprite,false);
        backdrop.animate=animateBackground;
        backdrop.motionAmount=backgroundMotion;
        backdrop.showClouds=showClouds;backdrop.cloudSpeed=cloudSpeed;

        var titleTexture=Resources.Load<Texture2D>("UI/Wuxia/HeukUnHyeopTitle");
        if(titleTexture!=null)
        {
            var titleRect=UIFactory.CreateRect("TitleLogo",canvas.transform);
            UIFactory.Place(titleRect,new Vector2(0,.5f),new Vector2(0,.5f),new Vector2(100,190),new Vector2(480,200));
            var image=titleRect.gameObject.AddComponent<RawImage>();
            image.texture=titleTexture;image.raycastTarget=false;
            float ratio=(float)titleTexture.width/titleTexture.height;
            titleRect.sizeDelta=new Vector2(480,480/ratio);
        }
        else
        {
            var title=WuxiaTheme.Text("Title",canvas.transform,"黑雲峽",88,TextAnchor.MiddleLeft,WuxiaTheme.Gold);
            title.font=WuxiaTheme.MenuFont;
            UIFactory.Place(title.rectTransform,new Vector2(0,.5f),new Vector2(0,.5f),new Vector2(100,190),new Vector2(480,150));
        }
        float y=40;
        MakeButton("NewGameButton","새 게임",ref y,OnNewGame);
        continueButton=MakeButton("ContinueButton","이어하기",ref y,OnContinue);
        MakeButton("DuelTestButton","빠른 대전",ref y,OnQuickDuel);
        var pvp=MakeButton("PvpButton","PvP 대전",ref y,TogglePvpMenu);
        var pvpIcon=pvp.transform.Find("MenuIcon");
        if(pvpIcon!=null) pvpIcon.GetComponent<WuxiaGraphic>().shape=WuxiaGraphic.Shape.Swords;
        MakeButton("CreditsButton","제작진",ref y,OnCredits);
        MakeButton("QuitButton","게임 종료",ref y,OnQuit);
        BuildLanMenu();

        var settings = UIFactory.CreateButton("SettingsButton", canvas.transform, "설정", 25, buttonColor, RedboolSettingsPanel.Show);
        WuxiaTheme.StyleButton(settings);
        UIFactory.Place(settings.GetComponent<RectTransform>(), Vector2.one, Vector2.one, new Vector2(-35, -30), new Vector2(170, 58));
        var version = WuxiaTheme.Text("ReleaseVersion", canvas.transform, "REDBOOL " + RedboolDemoSettings.Release, 20, TextAnchor.MiddleRight, WuxiaTheme.Paper);
        UIFactory.Place(version.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-35, 85), new Vector2(250, 32));
        var quickHelp = WuxiaTheme.Text("QuickDuelHelp", canvas.transform, "빠른 대전은 혼자 즐기는 대결입니다. 스토리 진행은 저장되지 않아요.", 21, TextAnchor.MiddleLeft, WuxiaTheme.Paper);
        UIFactory.Place(quickHelp.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(600, 80), new Vector2(1100, 38));

        messageText=WuxiaTheme.Text("Message",canvas.transform,"",24,TextAnchor.MiddleLeft,WuxiaTheme.Paper);
        UIFactory.Place(messageText.rectTransform,new Vector2(0,0),new Vector2(0,0),new Vector2(140,35),new Vector2(1400,50));
    }

    private Button MakeButton(string name, string label, ref float y, UnityEngine.Events.UnityAction action)
    {
        var button = UIFactory.CreateButton(name, canvas.transform, label, buttonFontSize, buttonColor, action);
        if (buttonSprite != null)
        {
            var img = button.GetComponent<Image>();
            img.sprite = buttonSprite;
            img.type = Image.Type.Sliced;
            img.color = Color.white;
        }
        if(buttonSprite==null) WuxiaTheme.StyleMenuButton(button,name=="NewGameButton");
        else WuxiaTheme.StyleButton(button);
        var labelText = button.GetComponentInChildren<Text>();
        if (labelText != null) { labelText.fontSize = buttonFontSize; labelText.fontStyle = FontStyle.Normal; }
        UIFactory.Place(button.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                        new Vector2(160f, y), buttonSize);
        y -= buttonSize.y + buttonSpacing;
        return button;
    }

    // ─────────────────────── 동작 ───────────────────────

    void BuildLanMenu()
    {
        pvpPanel=UIFactory.CreateRect("TwoScreenMenu",canvas.transform);
        UIFactory.Place(pvpPanel,new Vector2(.74f,.5f),new Vector2(.5f,.5f),new Vector2(0,-20),new Vector2(640,620));
        var body=WuxiaTheme.Ornament("LobbyInk",pvpPanel,WuxiaGraphic.Shape.Plaque,new Color(.08f,.105f,.095f,.96f));
        UIFactory.Stretch(body.rectTransform);
        var border=WuxiaTheme.Ornament("LobbyGold",pvpPanel,WuxiaGraphic.Shape.PlaqueFrame,WuxiaTheme.Gold);
        UIFactory.Stretch(border.rectTransform);
        var title=WuxiaTheme.Text("LanTitle",pvpPanel,"PvP 대전",36,TextAnchor.MiddleLeft,WuxiaTheme.Gold);
        title.font=WuxiaTheme.MenuFont;
        UIFactory.Place(title.rectTransform,new Vector2(0,1),new Vector2(0,1),new Vector2(40,-30),new Vector2(430,58));
        var close=UIFactory.CreateButton("CloseLobby",pvpPanel,"닫기",21,buttonColor,HidePvpMenu);
        UIFactory.Place(close.GetComponent<RectTransform>(),new Vector2(1,1),new Vector2(1,1),new Vector2(-32,-36),new Vector2(92,46));
        WuxiaTheme.StyleButton(close);
        // The short close button needs no decorative leaf beside its label.
        var closeMark=close.transform.Find("MenuMark");if(closeMark!=null) closeMark.gameObject.SetActive(false);
        var help=WuxiaTheme.Text("LanHelp",pvpPanel,"같은 Wi-Fi 또는 유선망의 두 노트북에서\n각자의 화면과 카메라로 대전합니다.",23,TextAnchor.UpperLeft,WuxiaTheme.Paper);
        help.horizontalOverflow=HorizontalWrapMode.Wrap;
        UIFactory.Place(help.rectTransform,new Vector2(0,1),new Vector2(0,1),new Vector2(40,-100),new Vector2(560,64));
        var host=UIFactory.CreateButton("Host",pvpPanel,"방 만들기",28,buttonColor,HostRoom);
        UIFactory.Place(host.GetComponent<RectTransform>(),new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-182),new Vector2(560,66));
        WuxiaTheme.StyleButton(host);
        var separator=WuxiaTheme.Text("JoinCaption",pvpPanel,"친구가 만든 방에 참가",23,TextAnchor.MiddleLeft,WuxiaTheme.Gold);
        UIFactory.Place(separator.rectTransform,new Vector2(0,1),new Vector2(0,1),new Vector2(40,-278),new Vector2(560,38));
        var inputImage=UIFactory.CreateImage("HostIP",pvpPanel,new Color(.04f,.065f,.055f,.95f),true);
        UIFactory.Place(inputImage.rectTransform,new Vector2(0,1),new Vector2(0,1),new Vector2(40,-326),new Vector2(390,60));
        var inputBorder=WuxiaTheme.Ornament("InputFrame",inputImage.transform,WuxiaGraphic.Shape.PlaqueFrame,new Color(WuxiaTheme.Gold.r,WuxiaTheme.Gold.g,WuxiaTheme.Gold.b,.6f));
        UIFactory.Stretch(inputBorder.rectTransform);
        hostInput=inputImage.gameObject.AddComponent<InputField>();
        var value=WuxiaTheme.Text("Value",inputImage.transform,"",25,TextAnchor.MiddleCenter,WuxiaTheme.Paper);
        UIFactory.Stretch(value.rectTransform,12,5,12,5);
        var hint=WuxiaTheme.Text("Hint",inputImage.transform,"방을 만든 노트북의 IPv4",21,TextAnchor.MiddleCenter,new Color(.68f,.68f,.60f));
        UIFactory.Stretch(hint.rectTransform,10,5,10,5);
        hostInput.targetGraphic=inputImage;hostInput.textComponent=value;hostInput.placeholder=hint;
        hostInput.characterLimit=15;hostInput.lineType=InputField.LineType.SingleLine;
        hostInput.onValidateInput=(text,index,added)=>added=='.'||added>='0'&&added<='9'?added:'\0';
        findRoomButton=UIFactory.CreateButton("FindRoom",pvpPanel,"방 찾기",24,buttonColor,FindRooms);
        UIFactory.Place(findRoomButton.GetComponent<RectTransform>(),new Vector2(1,1),new Vector2(1,1),new Vector2(-40,-326),new Vector2(150,60));
        WuxiaTheme.StyleButton(findRoomButton);
        var findMark=findRoomButton.transform.Find("MenuMark");if(findMark!=null) findMark.gameObject.SetActive(false);
        var join=UIFactory.CreateButton("Join",pvpPanel,"대전 참가",28,buttonColor,JoinRoom);
        UIFactory.Place(join.GetComponent<RectTransform>(),new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-410),new Vector2(560,66));
        WuxiaTheme.StyleButton(join);
        pvpMessage=WuxiaTheme.Text("LobbyMessage",pvpPanel,"한 명은 방 만들기, 다른 한 명은 방 찾기 후 참가를 누르세요.",22,TextAnchor.UpperLeft,WuxiaTheme.Paper);
        pvpMessage.horizontalOverflow=HorizontalWrapMode.Wrap;
        UIFactory.Place(pvpMessage.rectTransform,new Vector2(0,1),new Vector2(0,1),new Vector2(40,-500),new Vector2(560,90));
        pvpPanel.gameObject.SetActive(false);
    }

    private void TogglePvpMenu()
    {
        if(pvpPanel.gameObject.activeSelf) HidePvpMenu();
        else if(!EntryBlockedByExport())pvpPanel.gameObject.SetActive(true);
    }

    private void HidePvpMenu()
    {
        StopRoomSearch();
        if(pvpPanel!=null) pvpPanel.gameObject.SetActive(false);
    }

    private void HostRoom()
    {
        if(EntryBlockedByExport())return;
        StopRoomSearch();
        RedboolPvp.HostDual();
    }

    private void JoinRoom()
    {
        if(EntryBlockedByExport())return;
        string value=hostInput.text.Trim();
        if(IPAddress.TryParse(value,out var address) && address.AddressFamily==AddressFamily.InterNetwork
            && address.ToString()==value && !address.Equals(IPAddress.Any) && !address.Equals(IPAddress.Broadcast))
        {
            StopRoomSearch();
            RedboolPvp.Join(address.ToString());
        }
        else pvpMessage.text="방을 만든 노트북의 IPv4를 입력해주세요. 예: 192.168.0.10";
    }

    private void FindRooms()
    {
        if(roomSearch!=null) return;
        try
        {
            discoverySocket=new UdpClient(AddressFamily.InterNetwork);
            RedboolSocketOwnership.PreventInheritance(discoverySocket.Client,"UDP room discovery 0.0.0.0:0");
            discoverySocket.Client.Bind(new IPEndPoint(IPAddress.Any,0));
            discoverySocket.EnableBroadcast=true;
            discoverySocket.Client.Blocking=false;
        }
        catch(Exception e) when(e is SocketException || e is System.Security.SecurityException || e is System.ComponentModel.Win32Exception)
        {
            StopRoomSearch();
            Debug.LogWarning("[REDBOOL DISCOVERY] Cannot open room search: "+e.Message);
            pvpMessage.text="방 찾기를 사용할 수 없어요. 친구의 IPv4를 직접 입력해주세요.";
            return;
        }
        findRoomButton.interactable=false;
        pvpMessage.text="같은 네트워크에서 방을 찾고 있어요…";
        roomSearch=StartCoroutine(DiscoverRooms(hostInput.text));
    }

    private IEnumerator DiscoverRooms(string originalInput)
    {
        // Discovery only fills an address. It never joins, pairs a player or
        // sends camera/game commands; all work is bounded and nonblocking.
        yield return null;
        var found=new SortedSet<string>(StringComparer.Ordinal);
        byte[] query=Encoding.ASCII.GetBytes("REDBOOL_DISCOVER_V1");
        double started=Time.realtimeSinceStartupAsDouble,nextSend=started;
        int received=0;
        try
        {
            while(discoverySocket!=null && Time.realtimeSinceStartupAsDouble-started<1.2 && received<128)
            {
                double now=Time.realtimeSinceStartupAsDouble;
                if(now>=nextSend)
                {
                    nextSend=now+.4;
                    try { discoverySocket.Send(query,query.Length,new IPEndPoint(IPAddress.Broadcast,5067)); }
                    catch(SocketException) { break; }
                }
                // Limit datagrams per rendered frame as well as per search.
                for(int i=0;i<16 && received<128;i++)
                {
                    byte[] data;var from=new IPEndPoint(IPAddress.Any,0);
                    try { data=discoverySocket.Receive(ref from); }
                    catch(SocketException) { break; }
                    received++;
                    if(TryReadRoom(data,from,out var address) && found.Count<8) found.Add(address);
                }
                yield return null;
            }
        }
        finally
        {
            if(discoverySocket!=null) { discoverySocket.Close();discoverySocket=null; }
            roomSearch=null;
            if(findRoomButton!=null) findRoomButton.interactable=true;
        }
        if(pvpMessage==null || pvpPanel==null || !pvpPanel.gameObject.activeSelf) yield break;
        if(found.Count==1)
        {
            foreach(string address in found)
            {
                if(hostInput.text==originalInput) hostInput.text=address;
                pvpMessage.text="방을 찾았어요: "+address+"\n주소를 확인하고 대전 참가를 누르세요.";
            }
        }
        else if(found.Count>1)
        {
            var shown=new List<string>(found);if(shown.Count>4) shown.RemoveRange(4,shown.Count-4);
            pvpMessage.text="여러 방을 찾았어요. 친구의 주소를 골라 입력해주세요.\n"+string.Join("  ·  ",shown);
        }
        else pvpMessage.text="방을 찾지 못했어요. 친구가 방을 만들었는지 확인하거나 IPv4를 직접 입력해주세요.";
    }

    private static bool TryReadRoom(byte[] data,IPEndPoint from,out string address)
    {
        address=null;
        if(data==null || data.Length==0 || data.Length>512 || from==null || from.Port!=5067
            || from.AddressFamily!=AddressFamily.InterNetwork) return false;
        try
        {
            var reply=JsonUtility.FromJson<RoomReply>(Encoding.UTF8.GetString(data));
            if(reply==null || reply.service!="REDBOOL" || reply.view_version!=RedboolViewProtocol.Version
                || reply.p2_port!=5066 || reply.view_port!=5067) return false;
            address=from.Address.ToString();return true;
        }
        catch(ArgumentException) { return false; }
    }

    private void StopRoomSearch()
    {
        bool cancelled=roomSearch!=null;
        if(roomSearch!=null) { StopCoroutine(roomSearch);roomSearch=null; }
        if(discoverySocket!=null) { discoverySocket.Close();discoverySocket=null; }
        if(findRoomButton!=null) findRoomButton.interactable=true;
        if(cancelled && pvpMessage!=null)
            pvpMessage.text="방 찾기를 다시 누르거나 친구의 IPv4를 직접 입력해주세요.";
    }

    private void OnDisable() { StopRoomSearch(); }

    private void RefreshContinue()
    {
        bool canContinue = RunState.Instance != null && RunState.Instance.HasSave();
        if (continueButton != null) continueButton.interactable = canContinue;
    }

    private void ShowLastMessage()
    {
        if (messageText == null || RunState.Instance == null) return;
        messageText.text = RunState.Instance.LastMessage ?? "";
        RunState.Instance.LastMessage = null;
    }

    private void OnNewGame()
    {
        if(EntryBlockedByExport())return;
        if (RunState.Instance == null) return;
        if (RunState.Instance.HasSave())
        {
            HidePvpMenu();
            RedboolNewGameConfirmation.Show(BeginNewGame);
            return;
        }
        BeginNewGame();
    }

    private void BeginNewGame()
    {
        if(EntryBlockedByExport())return;
        if (RunState.Instance == null) return;
        RunState.Instance.StartNewRun();
        SceneRoutes.LoadMap();
    }

    private void OnContinue()
    {
        if(EntryBlockedByExport())return;
        if (RunState.Instance == null) return;
        if (RunState.Instance.LoadRun())
        {
            // A saved in-progress battle must resume that scene. Returning to
            // the map with pendingNodeId still set prevents selecting any node.
            if (RunState.Instance.HasPendingBattle) SceneRoutes.LoadBattle();
            else SceneRoutes.LoadMap();
        }
        else RefreshContinue();
    }

    private bool EntryBlockedByExport()
    {
        if(!RedboolPackageDiagnostics.IsExporting)return false;
        if(messageText!=null)messageText.text="기록 저장이 끝나면 시작해주세요";
        if(pvpMessage!=null)pvpMessage.text="기록 저장이 끝나면 시작해주세요";
        return true;
    }

    private void OnQuickDuel()
    {
        if(!EntryBlockedByExport())RedboolDuelTest.Begin();
    }

    private void OnCredits()
    {
        EndingSceneUI.OpenedFromMenu = true;
        SceneRoutes.LoadEnding();
    }

    private void OnQuit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
