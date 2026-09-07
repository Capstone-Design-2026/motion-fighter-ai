using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 전투 HUD. 캔버스와 체력바를 코드로 생성하므로 씬에 UI 를 수동으로 만들 필요가 없다.
/// 플레이 모드에 들어가면 좌/우 상단에 체력바가 생기고, K.O. 시 안내 문구가 뜬다.
/// </summary>
public class CombatHUD : MonoBehaviour
{
    [Header("대상 (비워두면 씬에서 자동으로 찾음)")]
    [SerializeField] private Health playerHealth;
    [SerializeField] private Health enemyHealth;

    [Header("체력바 모양")]
    [SerializeField] private Vector2 barSize = new Vector2(620f, 46f);
    [SerializeField] private Vector2 barMargin = new Vector2(60f, 40f);
    [SerializeField] private Color playerColor = new Color(0.25f, 0.85f, 0.35f);
    [SerializeField] private Color enemyColor = new Color(0.95f, 0.35f, 0.30f);
    [SerializeField] private Color chipColor = new Color(1f, 0.85f, 0.25f);
    [SerializeField] private float chipSpeed = 0.6f;   // 노란 잔량바가 따라오는 속도

    [Header("라운드")]
    [Tooltip("K.O. 후 이 키로 다시 시작. 지도를 진행 중이면 BattleSceneBridge 가 자동으로 꺼버린다.")]
    [SerializeField] private bool allowRestart = true;
    [SerializeField] private KeyCode restartKey = KeyCode.R;

    /// <summary>
    /// 지도(런) 진행 중에는 false 가 된다.
    /// 이기면 알아서 지도로 돌아가고, 지면 판이 끝나야 하므로 그 자리에서 되살아나면 안 된다.
    /// GameScene 을 단독으로 실행할 때는 true 로 남아서 예전처럼 R 키 재시작이 된다.
    /// </summary>
    public bool AllowRestart { get { return allowRestart; } set { allowRestart = value; } }

    private Canvas canvas;
    private Font font;

    private Bar playerBar;
    private Bar enemyBar;
    private Text koText;

    private Vector3 playerStartPos;
    private Vector3 enemyStartPos;
    private bool matchOver;

    /// <summary>체력바 한 세트 (배경 / 잔량 / 실제 HP / 라벨)</summary>
    private class Bar
    {
        public RectTransform fill;
        public RectTransform chip;
        public Text label;
        public Health health;
        public bool leftSide;
        public float targetRatio = 1f;
        public float chipRatio = 1f;
    }

    void Awake()
    {
        if (playerHealth == null)
        {
            var player = FindAnyObjectByType<PlayerController>();
            if (player != null) playerHealth = player.GetComponent<Health>();
        }
        if (enemyHealth == null)
        {
            var enemy = FindAnyObjectByType<EnemyAI>();
            if (enemy != null) enemyHealth = enemy.GetComponent<Health>();
        }

        font = LoadFont();
        BuildCanvas();

        if (playerHealth != null)
        {
            playerBar = BuildBar(playerHealth, playerColor, true);
            playerStartPos = playerHealth.transform.position;
            playerHealth.OnHealthChanged += (c, m) => OnChanged(playerBar, c, m);
            playerHealth.OnDeath += OnDeath;
        }
        else Debug.LogWarning("[CombatHUD] Player 의 Health 를 찾지 못했습니다.");

        if (enemyHealth != null)
        {
            enemyBar = BuildBar(enemyHealth, enemyColor, false);
            enemyStartPos = enemyHealth.transform.position;
            enemyHealth.OnHealthChanged += (c, m) => OnChanged(enemyBar, c, m);
            enemyHealth.OnDeath += OnDeath;
        }
        else Debug.LogWarning("[CombatHUD] Enemy 의 Health 를 찾지 못했습니다.");
    }

    void Update()
    {
        UpdateChip(playerBar);
        UpdateChip(enemyBar);

        if (matchOver && allowRestart && Input.GetKeyDown(restartKey))
            RestartRound();
    }

    private void OnChanged(Bar bar, int current, int max)
    {
        if (bar == null) return;

        bar.targetRatio = max > 0 ? (float)current / max : 0f;
        ApplyRatio(bar.fill, bar.targetRatio, bar.leftSide);

        // 회복하거나 라운드가 리셋되면 잔량바도 바로 따라 올린다.
        if (bar.targetRatio > bar.chipRatio)
        {
            bar.chipRatio = bar.targetRatio;
            ApplyRatio(bar.chip, bar.chipRatio, bar.leftSide);
        }

        if (bar.label != null) bar.label.text = bar.health.DisplayName + "   " + current + " / " + max;
    }

    /// <summary>
    /// 앵커를 직접 움직여서 바 길이를 조절한다.
    /// Image.Type.Filled 는 스프라이트가 없으면 fillAmount 가 무시되므로 이 방식을 쓴다.
    /// 플레이어 바는 왼쪽 끝에, 적 바는 오른쪽 끝에 남은 체력이 붙는다.
    /// </summary>
    private static void ApplyRatio(RectTransform rect, float ratio, bool leftSide)
    {
        if (rect == null) return;
        ratio = Mathf.Clamp01(ratio);

        Vector2 min = rect.anchorMin;
        Vector2 max = rect.anchorMax;
        if (leftSide) { min.x = 0f;         max.x = ratio; }
        else          { min.x = 1f - ratio; max.x = 1f;    }
        rect.anchorMin = min;
        rect.anchorMax = max;

        rect.offsetMin = new Vector2(0f, 0f);
        rect.offsetMax = new Vector2(0f, 0f);
    }

    private void UpdateChip(Bar bar)
    {
        if (bar == null || bar.chip == null) return;
        if (Mathf.Approximately(bar.chipRatio, bar.targetRatio)) return;

        bar.chipRatio = Mathf.MoveTowards(bar.chipRatio, bar.targetRatio, chipSpeed * Time.deltaTime);
        ApplyRatio(bar.chip, bar.chipRatio, bar.leftSide);
    }

    private void OnDeath(Health dead)
    {
        matchOver = true;
        bool playerLost = (dead == playerHealth);
        koText.text = (playerLost ? "YOU LOSE" : "K.O.  YOU WIN")
                    + (allowRestart ? "\n[" + restartKey + "] 키로 다시 시작" : "");
        koText.color = playerLost ? new Color(1f, 0.45f, 0.45f) : new Color(1f, 0.9f, 0.4f);
        koText.gameObject.SetActive(true);
    }

    private void RestartRound()
    {
        matchOver = false;
        koText.gameObject.SetActive(false);

        RestoreFighter(playerHealth, playerStartPos);
        RestoreFighter(enemyHealth, enemyStartPos);

        if (playerBar != null) playerBar.chipRatio = 1f;
        if (enemyBar != null) enemyBar.chipRatio = 1f;
    }

    private void RestoreFighter(Health health, Vector3 position)
    {
        if (health == null) return;
        health.transform.position = position;
        var rb = health.GetComponent<Rigidbody2D>();
        if (rb != null) rb.linearVelocity = Vector2.zero;
        health.ResetHealth();
    }

    // ────────────────────────── UI 생성 ──────────────────────────

    private void BuildCanvas()
    {
        var go = new GameObject("CombatCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.transform.SetParent(transform, false);

        canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        koText = CreateText("KOText", canvas.transform, 72, TextAnchor.MiddleCenter);
        var koRect = koText.rectTransform;
        koRect.anchorMin = new Vector2(0.5f, 0.5f);
        koRect.anchorMax = new Vector2(0.5f, 0.5f);
        koRect.pivot = new Vector2(0.5f, 0.5f);
        koRect.anchoredPosition = new Vector2(0f, 60f);
        koRect.sizeDelta = new Vector2(1200f, 300f);
        koText.gameObject.SetActive(false);
    }

    private Bar BuildBar(Health health, Color color, bool leftSide)
    {
        var bar = new Bar { health = health, leftSide = leftSide };

        // 루트 (검은 배경 테두리)
        var root = CreateImage("Bar_" + health.DisplayName, canvas.transform, new Color(0f, 0f, 0f, 0.65f));
        var rect = root.rectTransform;
        rect.anchorMin = new Vector2(leftSide ? 0f : 1f, 1f);
        rect.anchorMax = rect.anchorMin;
        rect.pivot = new Vector2(leftSide ? 0f : 1f, 1f);
        rect.sizeDelta = barSize;
        rect.anchoredPosition = new Vector2(leftSide ? barMargin.x : -barMargin.x, -barMargin.y);

        // 테두리 4px 를 남기는 안쪽 영역. 바 길이는 이 영역 기준으로 계산된다.
        var inner = CreateRect("Inner", root.transform);
        inner.anchorMin = Vector2.zero;
        inner.anchorMax = Vector2.one;
        inner.offsetMin = new Vector2(4f, 4f);
        inner.offsetMax = new Vector2(-4f, -4f);

        // 잔량바(노랑)를 먼저 만들어 뒤에 깔고, 그 위에 체력바(색)를 얹는다.
        bar.chip = CreateFill("Chip", inner, chipColor, leftSide);
        bar.fill = CreateFill("Fill", inner, color, leftSide);

        // 라벨은 바 아래쪽에
        bar.label = CreateText("Label", root.transform, 26,
            leftSide ? TextAnchor.UpperLeft : TextAnchor.UpperRight);
        var lrect = bar.label.rectTransform;
        lrect.anchorMin = new Vector2(0f, 0f);
        lrect.anchorMax = new Vector2(1f, 0f);
        lrect.pivot = new Vector2(0.5f, 1f);
        lrect.offsetMin = new Vector2(4f, -40f);
        lrect.offsetMax = new Vector2(-4f, -6f);

        bar.label.text = health.DisplayName + "   " + health.CurrentHealth + " / " + health.MaxHealth;
        return bar;
    }

    private RectTransform CreateFill(string name, Transform parent, Color color, bool leftSide)
    {
        var img = CreateImage(name, parent, color);
        var rect = img.rectTransform;
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(leftSide ? 0f : 1f, 0.5f);
        ApplyRatio(rect, 1f, leftSide);
        return rect;
    }

    private RectTransform CreateRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    private Image CreateImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private Text CreateText(string name, Transform parent, int size, TextAnchor anchor)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.alignment = anchor;
        text.color = Color.white;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    private Font LoadFont()
    {
        Font f = null;
        try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
        if (f == null)
        {
            try { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }
        }
        if (f == null)
        {
            try { f = Font.CreateDynamicFontFromOSFont("Malgun Gothic", 24); } catch { }
        }
        if (f == null)
            Debug.LogWarning("[CombatHUD] 폰트를 찾지 못했습니다. 텍스트가 안 보일 수 있습니다.");
        return f;
    }
}
