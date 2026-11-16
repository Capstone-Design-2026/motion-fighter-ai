using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Online room entry only; recognition and combat stay with their existing owners.</summary>
public sealed class RedboolOnlineMenu : MonoBehaviour
{
    RedboolPhotonSession session;
    Func<bool> entryBlocked;
    Action close;
    InputField codeInput;
    Button createButton, joinButton, cancelButton;
    Text status;
    bool initialized, requestActive, enteringGame;
    string localMessage;

    public void Initialize(Func<bool> blocked, Action closeMenu)
    {
        if(initialized)return;
        initialized=true;entryBlocked=blocked;close=closeMenu;
        Label("OnlineHelp","다른 장소에서도 각자의 화면과 카메라로 대전합니다.\n방 코드를 친구에게 알려주세요.",23,0,0,560,68);
        createButton=Button("CreateOnlineRoom","비공개 방 만들기",28,0,88,560,66,CreateRoom);
        Label("OnlineJoinCaption","친구의 방 코드로 참가",23,0,184,560,38,WuxiaTheme.Gold);
        var image=UIFactory.CreateImage("OnlineRoomCode",transform,new Color(.04f,.065f,.055f,.95f),true);
        Place(image.rectTransform,0,234,560,60);
        var frame=WuxiaTheme.Ornament("InputFrame",image.transform,WuxiaGraphic.Shape.PlaqueFrame,WuxiaTheme.Gold);
        UIFactory.Stretch(frame.rectTransform);
        codeInput=image.gameObject.AddComponent<InputField>();
        var value=WuxiaTheme.Text("Value",image.transform,"",28,TextAnchor.MiddleCenter,WuxiaTheme.Paper);
        UIFactory.Stretch(value.rectTransform,12,5,12,5);
        var hint=WuxiaTheme.Text("Hint",image.transform,"방 코드 입력 · KR-XXXXXX",23,TextAnchor.MiddleCenter,new Color(.68f,.68f,.60f));
        UIFactory.Stretch(hint.rectTransform,12,5,12,5);
        codeInput.targetGraphic=image;codeInput.textComponent=value;codeInput.placeholder=hint;
        codeInput.characterLimit=9;codeInput.lineType=InputField.LineType.SingleLine;
        codeInput.onValidateInput=(text,index,added)=>
        {
            char upper=char.ToUpperInvariant(added);
            return upper=='-'||upper>='A'&&upper<='Z'||upper>='0'&&upper<='9'?upper:'\0';
        };
        codeInput.onValueChanged.AddListener(_=>{if(!requestActive)localMessage=null;});
        joinButton=Button("JoinOnlineRoom","코드로 참가",28,0,316,560,66,JoinRoom);
        status=Label("OnlineRoomStatus","",22,0,404,560,74);
        cancelButton=Button("CancelOnlineRoom","연결 취소",21,360,442,200,44,CancelPending);
        cancelButton.gameObject.SetActive(false);
        // Edit-mode menu checks can build the hierarchy without creating a
        // persistent networking owner. Runtime setup still performs no connect.
        if(Application.isPlaying)EnsureSession();
        Refresh();
    }

    void EnsureSession()
    {
        if(session!=null)return;
        session=RedboolPhotonSession.Ensure();
        session.RoomEntered+=RoomEntered;
        session.RoomLeft+=RoomLeft;
    }

    void CreateRoom()
    {
        if(requestActive||enteringGame||entryBlocked!=null&&entryBlocked())return;
        EnsureSession();localMessage=null;requestActive=true;
        session.CreateRoom();Refresh();
    }

    void JoinRoom()
    {
        if(requestActive||enteringGame||entryBlocked!=null&&entryBlocked())return;
        string code=(codeInput.text??"").Trim().ToUpperInvariant();
        if(code.Length==0) {localMessage="친구에게 받은 방 코드를 입력해주세요.";Refresh();codeInput.ActivateInputField();return;}
        EnsureSession();localMessage=null;requestActive=true;
        session.JoinRoom(code);Refresh();
    }

    void RoomEntered()
    {
        // Cancellation and unrelated session events cannot load the arena.
        if(!requestActive||enteringGame||session==null||!session.InRoom)return;
        if(entryBlocked!=null&&entryBlocked())
        {
            requestActive=false;localMessage="기록 저장이 끝나면 다시 방을 만들거나 참가해주세요.";
            session.Leave();Refresh();return;
        }
        requestActive=false;enteringGame=true;
        RedboolOnlineRoomStrip.Ensure();
        if(session.IsHost)RedboolPvp.HostOnline();else RedboolPvp.JoinOnline();
    }

    void RoomLeft()
    {
        if(enteringGame)return;
        requestActive=false;Refresh();
    }

    public void CancelPending()
    {
        // Destroying this menu during the intended scene change must retain the room.
        if(enteringGame||!requestActive)return;
        requestActive=false;localMessage="연결을 취소했어요. 다시 방을 만들거나 참가할 수 있어요.";
        if(session!=null)session.Leave();
        Refresh();
    }

    void Update()
    {
        if(!initialized)return;
        // The shared menu handles Escape first. Close that newly opened overlay
        // and cancel this entry flow, without also closing a settings dialog.
        if(Input.GetKeyDown(KeyCode.Escape)&&RedboolPauseMenu.IsOpen)
        {
            RedboolPauseMenu.Close();CancelPending();RedboolPauseMenu.ConsumeClosingInput();close?.Invoke();return;
        }
        Refresh();
    }

    void Refresh()
    {
        if(!initialized||status==null)return;
        if(session!=null&&!string.IsNullOrEmpty(session.Error))requestActive=false;
        bool supported=session!=null&&session.Supported;
        createButton.interactable=joinButton.interactable=supported&&!requestActive&&!enteringGame;
        codeInput.interactable=!requestActive&&!enteringGame;
        cancelButton.gameObject.SetActive(requestActive);
        status.rectTransform.sizeDelta=new Vector2(560,requestActive?38:84);
        if(!string.IsNullOrEmpty(localMessage))status.text=localMessage;
        else if(session!=null&&!string.IsNullOrEmpty(session.Error))status.text=PlayerFacingError(session.Error);
        else if(!supported)status.text="이 게임의 온라인 연결 준비가 아직 끝나지 않았어요.\n온라인 지원 버전을 받거나 같은 네트워크 · LAN을 이용해주세요.";
        else if(requestActive)status.text=string.IsNullOrEmpty(session.Status)?"친구와 연결할 방을 준비하고 있어요…":session.Status;
        else status.text="한 명이 방을 만들고, 다른 한 명이 방 코드를 입력하면 됩니다.";
    }

    static string PlayerFacingError(string error)
    {
        if(error.IndexOf("SDK",StringComparison.OrdinalIgnoreCase)>=0)
            return "이 게임은 아직 온라인 연결을 지원하지 않아요.\n온라인 지원 버전을 받거나 같은 네트워크 · LAN을 이용해주세요.";
        if(error.IndexOf("App ID",StringComparison.OrdinalIgnoreCase)>=0)
            return error.Contains("초과")
                ? "온라인 서버 연결 시간이 초과됐어요.\n인터넷 상태를 확인하고 다시 참가해주세요."
                : "이 게임의 온라인 연결 설정이 아직 준비되지 않았어요.\n설정을 마친 버전을 받거나 같은 네트워크 · LAN을 이용해주세요.";
        return error;
    }

    void OnDisable() {CancelPending();}
    void OnDestroy()
    {
        CancelPending();
        if(session!=null) {session.RoomEntered-=RoomEntered;session.RoomLeft-=RoomLeft;}
    }

    Text Label(string name,string text,int size,float x,float y,float width,float height,Color? colour=null)
    {
        var label=WuxiaTheme.Text(name,transform,text,size,TextAnchor.UpperLeft,colour??WuxiaTheme.Paper);
        label.horizontalOverflow=HorizontalWrapMode.Wrap;label.verticalOverflow=VerticalWrapMode.Truncate;
        Place(label.rectTransform,x,y,width,height);return label;
    }
    Button Button(string name,string text,int size,float x,float y,float width,float height,UnityEngine.Events.UnityAction action)
    {
        var button=UIFactory.CreateButton(name,transform,text,size,WuxiaTheme.Ink,action);
        WuxiaTheme.StyleButton(button);Place(button.GetComponent<RectTransform>(),x,y,width,height);
        var mark=button.transform.Find("MenuMark");if(mark!=null)mark.gameObject.SetActive(false);
        return button;
    }
    static void Place(RectTransform rect,float x,float y,float width,float height)
    {
        UIFactory.Place(rect,new Vector2(0,1),new Vector2(0,1),new Vector2(x,-y),new Vector2(width,height));
    }
}

/// <summary>Keep the invitation available after the room loads its arena.</summary>
public sealed class RedboolOnlineRoomStrip : MonoBehaviour
{
    static RedboolOnlineRoomStrip instance;
    Canvas canvas;
    GraphicRaycaster raycaster;
    Text room, connection, copyLabel;
    Button copy;
    float copiedUntil;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState() {instance=null;}
    public static void Ensure()
    {
        if(instance!=null)return;
        var root=new GameObject("REDBOOL Online Room");
        instance=root.AddComponent<RedboolOnlineRoomStrip>();DontDestroyOnLoad(root);
    }
    void Awake()
    {
        if(instance!=null&&instance!=this) {Destroy(gameObject);return;}
        instance=this;DontDestroyOnLoad(gameObject);
        canvas=UIFactory.CreateCanvas("OnlineRoomCanvas",transform,60);
        raycaster=canvas.GetComponent<GraphicRaycaster>();
        var panel=UIFactory.CreateImage("OnlineRoomStrip",canvas.transform,new Color(.025f,.035f,.03f,.88f),true);
        // The lobby title starts 30px below the top edge; the combat clock starts
        // 35px below it. This strip also avoids the camera panels and bottom hints.
        UIFactory.Place(panel.rectTransform,new Vector2(.5f,1),new Vector2(.5f,1),Vector2.zero,new Vector2(760,28));
        room=WuxiaTheme.Text("RoomCode",panel.transform,"",18,TextAnchor.MiddleLeft,WuxiaTheme.Gold);
        UIFactory.Place(room.rectTransform,new Vector2(0,.5f),new Vector2(0,.5f),new Vector2(20,0),new Vector2(260,26));
        connection=WuxiaTheme.Text("PeerStatus",panel.transform,"",17,TextAnchor.MiddleLeft,WuxiaTheme.Paper);
        UIFactory.Place(connection.rectTransform,new Vector2(0,.5f),new Vector2(0,.5f),new Vector2(280,0),new Vector2(220,26));
        copy=MakeButton(panel.transform,"CopyRoomCode","코드 복사",510,CopyCode);
        copyLabel=copy.GetComponentInChildren<Text>();
        MakeButton(panel.transform,"LeaveOnlineRoom","나가기",640,LeaveRoom);
        raycaster.enabled=canvas.enabled=false;
    }
    void Update()
    {
        var session=RedboolPhotonSession.Instance;
        bool visible=session!=null&&session.InRoom&&RedboolPvp.Active&&!RedboolPauseMenu.IsOpen&&!RedboolSettingsPanel.IsOpen;
        raycaster.enabled=canvas.enabled=visible;
        if(!visible)return;
        room.text="방 코드  "+session.RoomCode;
        connection.text=session.PeerPresent?"친구 연결됨":"친구를 기다리고 있어요";
        copyLabel.text=Time.unscaledTime<copiedUntil?"복사했어요":"코드 복사";
        copy.interactable=!string.IsNullOrEmpty(session.RoomCode);
    }
    void CopyCode()
    {
        var session=RedboolPhotonSession.Instance;
        if(session==null||!session.InRoom||string.IsNullOrEmpty(session.RoomCode))return;
        GUIUtility.systemCopyBuffer=session.RoomCode;copiedUntil=Time.unscaledTime+1.5f;
    }
    void LeaveRoom()
    {
        RedboolPauseMenu.ConsumeClosingInput();
        if(RedboolPvp.Active&&RedboolPvp.Instance!=null)RedboolPvp.Instance.Leave(true);
        else {RedboolPhotonSession.Instance?.Leave();SceneRoutes.LoadStart();}
    }
    static Button MakeButton(Transform parent,string name,string text,float x,UnityEngine.Events.UnityAction action)
    {
        var button=UIFactory.CreateButton(name,parent,text,17,WuxiaTheme.Ink,action);
        UIFactory.Place(button.GetComponent<RectTransform>(),new Vector2(0,.5f),new Vector2(0,.5f),new Vector2(x,0),new Vector2(110,24));
        WuxiaTheme.StyleButton(button);
        var mark=button.transform.Find("MenuMark");if(mark!=null)mark.gameObject.SetActive(false);
        return button;
    }
    void OnDestroy() {if(instance==this)instance=null;}
}
