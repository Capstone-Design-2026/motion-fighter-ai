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

    private Canvas canvas;
    private Button continueButton;
    private Text messageText;

    void Start()
    {
        UIFactory.EnsureEventSystem();
        Build();
        RefreshContinue();
        ShowLastMessage();
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
        MakeButton("DuelTestButton","바로 결투 · 테스트",ref y,RedboolDuelTest.Begin);
        MakeButton("PvpButton","2인 대전 · 카메라 연결",ref y,RedboolPvp.Begin);
        MakeButton("QuitButton","게임 종료",ref y,OnQuit);
        BuildLanMenu();

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
        var panel=UIFactory.CreateRect("TwoScreenMenu",canvas.transform);
        UIFactory.Place(panel,new Vector2(.75f,.5f),new Vector2(.5f,.5f),new Vector2(0,-40),new Vector2(500,340));
        var title=WuxiaTheme.Text("LanTitle",panel,"노트북 두 대 · 같은 결투장",28,TextAnchor.MiddleCenter,WuxiaTheme.Gold);
        UIFactory.Place(title.rectTransform,new Vector2(.5f,1),new Vector2(.5f,1),Vector2.zero,new Vector2(500,60));
        var host=UIFactory.CreateButton("Host",panel,"1P · 두 화면 대전 열기",25,buttonColor,RedboolPvp.HostDual);
        UIFactory.Place(host.GetComponent<RectTransform>(),new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-70),new Vector2(460,65));WuxiaTheme.StyleButton(host);
        var inputImage=UIFactory.CreateImage("HostIP",panel,new Color(.1f,.13f,.15f,.95f),true);
        UIFactory.Place(inputImage.rectTransform,new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-150),new Vector2(460,60));
        var input=inputImage.gameObject.AddComponent<InputField>();var value=WuxiaTheme.Text("Value",inputImage.transform,"",24,TextAnchor.MiddleCenter,WuxiaTheme.Paper);
        UIFactory.Place(value.rectTransform,new Vector2(.5f,.5f),new Vector2(.5f,.5f),Vector2.zero,new Vector2(440,50));
        var hint=WuxiaTheme.Text("Hint",inputImage.transform,"1P 노트북 IPv4 입력",23,TextAnchor.MiddleCenter,Color.gray);
        UIFactory.Place(hint.rectTransform,new Vector2(.5f,.5f),new Vector2(.5f,.5f),Vector2.zero,new Vector2(440,50));input.textComponent=value;input.placeholder=hint;input.characterLimit=15;
        var join=UIFactory.CreateButton("Join",panel,"2P · 입력한 주소로 참가",25,buttonColor,()=> {
            if(System.Net.IPAddress.TryParse(input.text.Trim(),out var ip) &&ip.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork) RedboolPvp.Join(ip.ToString());
            else messageText.text="1P의 IPv4 주소를 확인해주세요. 예: 192.168.0.10";
        });
        UIFactory.Place(join.GetComponent<RectTransform>(),new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-225),new Vector2(460,65));WuxiaTheme.StyleButton(join);
    }

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
        if (RunState.Instance == null) return;
        RunState.Instance.StartNewRun();
        SceneRoutes.LoadMap();
    }

    private void OnContinue()
    {
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

    private void OnQuit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
