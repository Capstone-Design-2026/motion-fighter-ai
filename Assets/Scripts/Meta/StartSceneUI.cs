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
        float y=5;
        MakeButton("NewGameButton","새 게임",ref y,OnNewGame);
        continueButton=MakeButton("ContinueButton","이어하기",ref y,OnContinue);
        MakeButton("DuelTestButton","바로 결투 · 테스트",ref y,RedboolDuelTest.Begin);
        MakeButton("QuitButton","게임 종료",ref y,OnQuit);

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
