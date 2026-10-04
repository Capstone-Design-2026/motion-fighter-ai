using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 산속 수련장 배경과 왼쪽 메뉴를 조합한 시작 화면.
/// Sprite 필드는 기본 배경/로고/버튼을 교체할 때 사용한다.
/// </summary>
public class StartSceneUI : MonoBehaviour
{
    [Header("디자인 (비워두면 무협 기본 배경)")]
    [SerializeField] private Sprite backgroundSprite;
    [SerializeField] private Sprite logoSprite;
    [SerializeField] private Sprite buttonSprite;

    [Header("색")]
    [SerializeField] private Color buttonColor     = new Color(0.12f, 0.17f, 0.18f, 0.85f);

    [Header("문구")]
    [SerializeField] private string titleText    = "VISION  STRIKER";
    [SerializeField] private string subtitleText = "동작으로 싸우는 격투 게임";

    [Header("배치")]
    [SerializeField] private Vector2 buttonSize = new Vector2(420f, 84f);
    [SerializeField] private float buttonSpacing = 20f;
    [SerializeField] private int buttonFontSize = 32;

    [Header("배경 움직임")]
    [SerializeField] private bool animateBackground = true;
    [Range(0,2)] [SerializeField] private float backgroundMotion = 1f;

    [SerializeField] private bool showClouds = true;
    [Range(0,2)] [SerializeField] private float cloudSpeed = 1f;

    private Canvas canvas;
    private Button continueButton;
    private Text messageText;
    private GameObject helpPanel;

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

        var eyebrow=WuxiaTheme.Text("Eyebrow",canvas.transform,"몸으로 펼치는 무예의 여정",22,TextAnchor.MiddleLeft,WuxiaTheme.Gold);
        UIFactory.Place(eyebrow.rectTransform,new Vector2(0,.5f),new Vector2(0,.5f),new Vector2(140,360),new Vector2(650,42));
        if(logoSprite!=null)
        {
            var logo=UIFactory.CreateImage("Logo",canvas.transform,Color.white);logo.sprite=logoSprite;logo.preserveAspect=true;
            UIFactory.Place(logo.rectTransform,new Vector2(0,.5f),new Vector2(0,.5f),new Vector2(135,235),new Vector2(650,200));
        }
        else
        {
            var title=WuxiaTheme.Text("Title",canvas.transform,titleText.Replace("  ","\n"),76,TextAnchor.MiddleLeft,WuxiaTheme.Paper);
            title.font=WuxiaTheme.DisplayFont;title.fontStyle=FontStyle.Normal;title.lineSpacing=.96f;
            var shadow=title.gameObject.AddComponent<Shadow>();shadow.effectColor=new Color(0,0,0,.65f);shadow.effectDistance=new Vector2(2,-3);
            UIFactory.Place(title.rectTransform,new Vector2(0,.5f),new Vector2(0,.5f),new Vector2(135,235),new Vector2(690,210));
        }
        var sub=WuxiaTheme.Text("Subtitle",canvas.transform,subtitleText,24,TextAnchor.MiddleLeft,new Color(.76f,.77f,.72f));
        UIFactory.Place(sub.rectTransform,new Vector2(0,.5f),new Vector2(0,.5f),new Vector2(140,100),new Vector2(700,50));
        float y=0;
        MakeButton("NewGameButton","새 게임",ref y,OnNewGame);
        continueButton=MakeButton("ContinueButton","이어하기",ref y,OnContinue);
        MakeButton("HelpButton","조작 안내",ref y,OnHelp);
        MakeButton("QuitButton","게임 종료",ref y,OnQuit);

        messageText=WuxiaTheme.Text("Message",canvas.transform,"",24,TextAnchor.MiddleLeft,WuxiaTheme.Paper);
        UIFactory.Place(messageText.rectTransform,new Vector2(0,0),new Vector2(0,0),new Vector2(140,35),new Vector2(1400,50));
        var edition=WuxiaTheme.Text("Edition",canvas.transform,"VISION STRIKER  /  무예의 길",18,TextAnchor.MiddleRight,new Color(.85f,.8f,.68f,.75f));
        UIFactory.Place(edition.rectTransform,new Vector2(1,0),new Vector2(1,0),new Vector2(-55,30),new Vector2(530,35));

        BuildHelpPanel();
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
        UIFactory.Place(button.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                        new Vector2(140f, y), buttonSize);
        y -= buttonSize.y + buttonSpacing;
        return button;
    }

    private void BuildHelpPanel()
    {
        var panel = UIFactory.CreateImage("HelpPanel", canvas.transform, new Color(0f, 0f, 0f, 0.85f), true);
        UIFactory.Stretch(panel.rectTransform);
        helpPanel = panel.gameObject;

        var text = WuxiaTheme.Text("HelpText", panel.transform,
            "조작 (키보드 테스트용)\n\n" +
            "A / D : 이동      W : 점프      S : 앉기\n" +
            "P : 공격          U : 막기 (누르고 있는 동안)\n\n" +
            "동작 인식 서버가 붙으면 키보드 대신 카메라 입력으로 바뀝니다.\n\n" +
            "아무 곳이나 클릭하면 닫힙니다.",
            32, TextAnchor.MiddleCenter, WuxiaTheme.Paper);
        UIFactory.Stretch(text.rectTransform);

        var close = panel.gameObject.AddComponent<Button>();
        close.targetGraphic = panel;
        close.onClick.AddListener(() => helpPanel.SetActive(false));

        helpPanel.SetActive(false);
    }

    // ─────────────────────── 동작 ───────────────────────

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
        if (RunState.Instance.LoadRun()) SceneRoutes.LoadMap();
        else RefreshContinue();
    }

    private void OnHelp()
    {
        if (helpPanel != null) helpPanel.SetActive(true);
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
