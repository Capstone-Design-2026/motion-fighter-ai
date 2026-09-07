using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 시작 화면. 지금은 배치만 코드로 잡아두고, 색/스프라이트는 인스펙터에서 갈아끼울 수 있게 열어뒀다.
/// 디자인이 나오면 아래 Sprite 필드에 이미지를 넣기만 하면 된다. 코드는 안 고쳐도 된다.
/// </summary>
public class StartSceneUI : MonoBehaviour
{
    [Header("디자인 (비워두면 단색으로 그려진다)")]
    [SerializeField] private Sprite backgroundSprite;
    [SerializeField] private Sprite logoSprite;
    [SerializeField] private Sprite buttonSprite;

    [Header("색")]
    [SerializeField] private Color backgroundColor = new Color(0.06f, 0.07f, 0.11f);
    [SerializeField] private Color buttonColor     = new Color(0.16f, 0.18f, 0.26f);
    [SerializeField] private Color accentColor     = new Color(0.95f, 0.75f, 0.25f);

    [Header("문구")]
    [SerializeField] private string titleText    = "VISION  STRIKER";
    [SerializeField] private string subtitleText = "동작으로 싸우는 격투 게임";

    [Header("배치")]
    [SerializeField] private Vector2 buttonSize = new Vector2(420f, 84f);
    [SerializeField] private float buttonSpacing = 20f;
    [SerializeField] private int buttonFontSize = 32;

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

        // 배경
        var bg = UIFactory.CreateImage("Background", canvas.transform, backgroundColor);
        if (backgroundSprite != null)
        {
            bg.sprite = backgroundSprite;
            bg.color = Color.white;
            bg.preserveAspect = false;
        }
        UIFactory.Stretch(bg.rectTransform);

        // 타이틀
        if (logoSprite != null)
        {
            var logo = UIFactory.CreateImage("Logo", canvas.transform, Color.white);
            logo.sprite = logoSprite;
            logo.preserveAspect = true;
            UIFactory.Place(logo.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                            new Vector2(0f, 280f), new Vector2(900f, 260f));
        }
        else
        {
            var title = UIFactory.CreateText("Title", canvas.transform, titleText, 96,
                                             TextAnchor.MiddleCenter, accentColor);
            UIFactory.Place(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                            new Vector2(0f, 300f), new Vector2(1400f, 140f));

            var sub = UIFactory.CreateText("Subtitle", canvas.transform, subtitleText, 30,
                                           TextAnchor.MiddleCenter, new Color(0.75f, 0.78f, 0.85f));
            UIFactory.Place(sub.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                            new Vector2(0f, 220f), new Vector2(1400f, 60f));
        }

        // 버튼 묶음
        float y = 40f;
        MakeButton("NewGameButton",  "새 게임",   ref y, OnNewGame);
        continueButton =
        MakeButton("ContinueButton", "이어하기",  ref y, OnContinue);
        MakeButton("HelpButton",     "조작 안내", ref y, OnHelp);
        MakeButton("QuitButton",     "게임 종료", ref y, OnQuit);

        // 안내 문구 (직전 판 결과 등)
        messageText = UIFactory.CreateText("Message", canvas.transform, "", 26,
                                           TextAnchor.MiddleCenter, new Color(0.9f, 0.85f, 0.6f));
        UIFactory.Place(messageText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                        new Vector2(0f, 60f), new Vector2(1400f, 50f));

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
        UIFactory.Place(button.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                        new Vector2(0f, y), buttonSize);
        y -= buttonSize.y + buttonSpacing;
        return button;
    }

    private void BuildHelpPanel()
    {
        var panel = UIFactory.CreateImage("HelpPanel", canvas.transform, new Color(0f, 0f, 0f, 0.85f), true);
        UIFactory.Stretch(panel.rectTransform);
        helpPanel = panel.gameObject;

        var text = UIFactory.CreateText("HelpText", panel.transform,
            "조작 (키보드 테스트용)\n\n" +
            "A / D : 이동      W : 점프      S : 앉기\n" +
            "P : 공격          U : 막기 (누르고 있는 동안)\n\n" +
            "동작 인식 서버가 붙으면 키보드 대신 카메라 입력으로 바뀝니다.\n\n" +
            "아무 곳이나 클릭하면 닫힙니다.",
            32, TextAnchor.MiddleCenter);
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
