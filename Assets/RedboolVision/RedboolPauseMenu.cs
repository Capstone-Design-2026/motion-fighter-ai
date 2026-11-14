using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Shared navigation overlay. Simulation and camera input keep running;
/// it deliberately does not take ownership of combat or cutscene time.</summary>
[DefaultExecutionOrder(-20000)]
public sealed class RedboolPauseMenu : MonoBehaviour
{
    static RedboolPauseMenu instance;
    static int consumedInputFrame = -1;
    public static bool IsOpen { get; private set; }
    // Closing with Enter/Space must not advance the story beneath the same click.
    public static bool BlocksStoryInput => IsOpen || consumedInputFrame == Time.frameCount;
    Canvas canvas;
    GraphicRaycaster raycaster;
    Text explanation;
    Button continueButton, menuButton;
    GameObject previousSelection;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState() { instance = null; IsOpen = false; consumedInputFrame = -1; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install() { EnsureInstance(); }

    static void EnsureInstance()
    {
        if (instance != null) return;
        var go = new GameObject("REDBOOL Game Menu");
        instance = go.AddComponent<RedboolPauseMenu>();
        DontDestroyOnLoad(go);
    }

    void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        DontDestroyOnLoad(gameObject);
        Build();
        SceneManager.sceneLoaded += SceneChanged;
    }

    void Build()
    {
        canvas = UIFactory.CreateCanvas("GameMenuCanvas", transform, 1000);
        raycaster = canvas.GetComponent<GraphicRaycaster>();
        var shade = UIFactory.CreateImage("GameMenuModal", canvas.transform, new Color(0, 0, 0, .72f), true);
        UIFactory.Stretch(shade.rectTransform);
        var panel = UIFactory.CreateImage("GameMenuPanel", shade.transform, new Color(.045f, .055f, .055f, .98f), true);
        UIFactory.Place(panel.rectTransform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(700, 340));
        var outline = panel.gameObject.AddComponent<Outline>();
        outline.effectColor = WuxiaTheme.Gold; outline.effectDistance = new Vector2(1, -1);
        var title = WuxiaTheme.Text("GameMenuTitle", panel.transform, "게임 메뉴", 34, TextAnchor.MiddleCenter, WuxiaTheme.Gold);
        UIFactory.Place(title.rectTransform, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -24), new Vector2(640, 50));
        explanation = WuxiaTheme.Text("GameMenuExplanation", panel.transform, "", 23, TextAnchor.MiddleCenter, WuxiaTheme.Paper);
        explanation.horizontalOverflow = HorizontalWrapMode.Wrap;
        UIFactory.Place(explanation.rectTransform, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -94), new Vector2(640, 100));
        continueButton = UIFactory.CreateButton("GameMenuContinue", panel.transform, "계속하기", 25, WuxiaTheme.Ink, Close);
        UIFactory.Place(continueButton.GetComponent<RectTransform>(), new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(-150, 48), new Vector2(260, 58));
        WuxiaTheme.StyleButton(continueButton);
        menuButton = UIFactory.CreateButton("GameMenuReturn", panel.transform, "메인 메뉴로", 25, WuxiaTheme.Ink, ReturnToMainMenu);
        UIFactory.Place(menuButton.GetComponent<RectTransform>(), new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(150, 48), new Vector2(260, 58));
        WuxiaTheme.StyleButton(menuButton);
        raycaster.enabled = canvas.enabled = false;
    }

    void Update()
    {
        if (!Input.GetKeyDown(KeyCode.Escape)) return;
        if (IsOpen) Close(); else Open();
    }

    public static void Open()
    {
        EnsureInstance();
        if (IsOpen) return;
        UIFactory.EnsureEventSystem();
        instance.previousSelection = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        IsOpen = true; consumedInputFrame = Time.frameCount;
        instance.raycaster.enabled = instance.canvas.enabled = true;
        instance.RefreshExplanation();
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(instance.continueButton.gameObject);
    }

    public static void Close()
    {
        if (!IsOpen) return;
        IsOpen = false; consumedInputFrame = Time.frameCount;
        if (instance == null) return;
        instance.raycaster.enabled = instance.canvas.enabled = false;
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(instance.previousSelection != null && instance.previousSelection.activeInHierarchy ? instance.previousSelection : null);
        instance.previousSelection = null;
    }

    public static void ReturnToMainMenu()
    {
        Close();
        // These routes already protect campaign saves and stop the camera on menu entry.
        if (RedboolPvp.Active && RedboolPvp.Instance != null) { RedboolPvp.Instance.Leave(true); return; }
        if (RedboolDuelTest.Active) { RedboolDuelTest.ReturnToMenu(); return; }
        if (SceneManager.GetActiveScene().name == SceneRoutes.Start) return;
        // A final-story coroutine runs on persistent RunState; do not let it load
        // the ending after the user has explicitly returned to the menu.
        if (RunState.Instance != null) RunState.Instance.StopAllCoroutines();
        HitImpact.ReleaseTimeControl();
        Time.timeScale = 1f; Time.fixedDeltaTime = .02f;
        SceneRoutes.LoadStart();
    }

    void RefreshExplanation()
    {
        bool atMenu = SceneManager.GetActiveScene().name == SceneRoutes.Start;
        menuButton.interactable = !atMenu;
        explanation.text = atMenu ? "현재 화면으로 돌아가려면 계속하기를 누르세요.\nEsc로도 이 창을 닫을 수 있어요."
            : "메뉴를 연 동안에도 게임은 계속 진행됩니다.\n" + (RedboolPvp.Active ? "메인 메뉴로 돌아가면 대전 연결이 종료됩니다."
                : RedboolDuelTest.Active ? "결투 테스트 결과는 저장되지 않습니다." : "스토리는 마지막 자동 저장 지점부터 이어집니다.");
    }

    void SceneChanged(Scene scene, LoadSceneMode mode)
    {
        previousSelection = null;
        if (IsOpen)
        {
            UIFactory.EnsureEventSystem(); RefreshExplanation();
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(continueButton.gameObject);
        }
    }
    void OnDestroy()
    {
        SceneManager.sceneLoaded -= SceneChanged;
        if (instance == this) { instance = null; IsOpen = false; }
    }
}
