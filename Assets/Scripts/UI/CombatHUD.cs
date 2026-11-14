using System.Collections.Generic;
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
    [SerializeField] private Vector2 barSize = new Vector2(788f, 44f);
    [SerializeField] private Vector2 barMargin = new Vector2(72f, 62f);
    [SerializeField] private Color playerColor = new Color(.94f, .61f, .16f);
    [SerializeField] private Color enemyColor = new Color(.68f, .12f, .10f);
    [SerializeField] private Color chipColor = new Color(1f, 0.85f, 0.25f);
    [SerializeField] private float chipSpeed = 0.6f;   // 노란 잔량바가 따라오는 속도

    [Header("라운드")]
    [Tooltip("K.O. 후 이 키로 다시 시작. 지도를 진행 중이면 BattleSceneBridge 가 자동으로 꺼버린다.")]
    [SerializeField] private bool allowRestart = true;
    [SerializeField] private KeyCode restartKey = KeyCode.R;

    [Header("필살기 게이지")]
    [SerializeField] private Color gaugeFillColor = new Color(.17f, .59f, .77f);
    [SerializeField] private Color gaugeReadyColor = new Color(.32f, .81f, 1f);
    [Tooltip("피격 직후 잔량이 남아 있는 시간. 전투 속도와 무관한 표시 전용 값.")]
    [SerializeField] private float damageTrailDelay = .25f;

    /// <summary>
    /// 지도(런) 진행 중에는 false 가 된다.
    /// 이기면 알아서 지도로 돌아가고, 지면 판이 끝나야 하므로 그 자리에서 되살아나면 안 된다.
    /// GameScene 을 단독으로 실행할 때는 true 로 남아서 예전처럼 R 키 재시작이 된다.
    /// </summary>
    public bool AllowRestart { get { return allowRestart; } set { allowRestart = value; } }

    private Canvas canvas;
    private Font font;
    private RectTransform topRail;
    private float railWidth = -1f;

    private Bar playerBar;
    private Bar enemyBar;
    private Text koText;
    private Text timerText;
    private BattleSceneBridge battle;

    private Vector3 playerStartPos;
    private Vector3 enemyStartPos;
    private bool matchOver;

    private PlayerController player;
    private SpecialMove special;

    private class Bar
    {
        public WuxiaCombatBar graphic, gauge;
        public Text label, amount, gaugeLabel;
        public Health health;
        public SpecialMove resource;
        public EnemySpecial foeResource;   // 적 필살기 게이지(적 쪽 막대에만 있다)
        public bool leftSide;
        public float targetRatio, chipRatio, trailUntil;
        public System.Action<int, int> healthChanged, chargeChanged;
    }

    // HUD가 생성되기 전에 새 프리팹의 Health로 연결한다.
    public void SetTargets(Health player, Health enemy)
    {
        playerHealth = player;
        enemyHealth = enemy;
    }

    void Awake()
    {
        // PvP: 판정과 두 화면 동기화는 RedboolPvp 가 한다. 여기서는 값만 받아 그리는 방식으로 대기한다(SetExternalVisible 로 켠다).
        if (RedboolPvp.Active) { external = true; enabled = false; return; }
        player = FindAnyObjectByType<PlayerController>();
        if (playerHealth == null && player != null)
            playerHealth = player.GetComponent<Health>();
        if (player != null) special = player.GetComponent<SpecialMove>();

        if (enemyHealth == null)
        {
            var enemy = FindAnyObjectByType<EnemyAI>();
            if (enemy != null) enemyHealth = enemy.GetComponent<Health>();
        }

        font = LoadFont();
        BuildCanvas();
        battle = FindAnyObjectByType<BattleSceneBridge>();
        BuildTimer();

        if (playerHealth != null)
        {
            playerBar = BuildBar(playerHealth, playerColor, true);
            playerStartPos = playerHealth.transform.position;
            Bind(playerBar);
        }
        else Debug.LogWarning("[CombatHUD] Player 의 Health 를 찾지 못했습니다.");

        if (enemyHealth != null)
        {
            enemyBar = BuildBar(enemyHealth, enemyColor, false);
            enemyStartPos = enemyHealth.transform.position;
            Bind(enemyBar);
        }
        else Debug.LogWarning("[CombatHUD] Enemy 의 Health 를 찾지 못했습니다.");
    }

    void Update()
    {
        UpdateChip(playerBar);
        UpdateChip(enemyBar);
        if (external) return;
        UpdateTimer();

        if (matchOver && allowRestart && Input.GetKeyDown(restartKey))
            RestartRound();
    }

    void LateUpdate()
    {
        if (canvas == null || topRail == null) return;
        float width = ((RectTransform)canvas.transform).rect.width;
        if (Mathf.Approximately(width, railWidth)) return;
        railWidth = width;
        topRail.localScale = Vector3.one * (width / 1920f);
    }

    private void Bind(Bar bar)
    {
        bar.healthChanged = (current, max) => OnChanged(bar, current, max);
        bar.health.OnHealthChanged += bar.healthChanged;
        bar.health.OnDeath += OnDeath;
        bar.chipRatio = bar.health.MaxHealth > 0 ? (float)bar.health.CurrentHealth / bar.health.MaxHealth : 0;
        OnChanged(bar, bar.health.CurrentHealth, bar.health.MaxHealth);
        if (bar.resource != null)
        {
            bar.chargeChanged = (current, max) => OnChargeChanged(bar, current, max);
            bar.resource.OnChargeChanged += bar.chargeChanged;
            OnChargeChanged(bar, bar.resource.Charge, bar.resource.HitsToCharge);
        }
        else if (bar.foeResource != null && bar.foeResource.Available)
        {
            bar.chargeChanged = (current, max) => OnFoeChargeChanged(bar, current, max);
            bar.foeResource.OnChargeChanged += bar.chargeChanged;
            OnFoeChargeChanged(bar, bar.foeResource.Charge, bar.foeResource.HitsToCharge);
        }
    }

    void OnDestroy()
    {
        Unbind(playerBar); Unbind(enemyBar);
    }

    // ─────────────────────── PvP: 값만 받아서 그린다 ───────────────────────
    // 스토리와 같은 체력 막대·필살기 게이지·시계를 쓰되, 체력·게이지·시간은 RedboolPvp 가 넣어 준다.
    // (손님 화면에는 실제 Health 가 없고 호스트가 보낸 숫자만 있기 때문이다.)
    private bool external, externalBuilt;

    public void SetExternalVisible(bool visible)
    {
        if (!external) return;
        if (visible && !externalBuilt)
        {
            externalBuilt = true;
            font = LoadFont();
            BuildCanvas();
            BuildTimer();
            playerBar = BuildBar(null, playerColor, true);
            enemyBar = BuildBar(null, enemyColor, false);
        }
        if (canvas != null && canvas.gameObject.activeSelf != visible) canvas.gameObject.SetActive(visible);
        enabled = visible;
    }

    public void ShowExternalFighter(bool left, string name, int current, int max)
    {
        var bar = left ? playerBar : enemyBar;
        if (!external || bar == null) return;
        float next = max > 0 ? Mathf.Clamp01((float)current / max) : 0;
        if (next < bar.targetRatio) bar.trailUntil = Time.unscaledTime + damageTrailDelay;
        else if (next > bar.targetRatio) bar.chipRatio = next;
        bar.targetRatio = next;
        bar.graphic.SetValues(next, bar.chipRatio);
        bar.label.text = name;
        bar.amount.text = current + " / " + max;
    }

    /// <summary>필살기 게이지. firing: 지금 그 기술이 나가는 중.</summary>
    public void ShowExternalGauge(bool left, string move, int current, int max, bool firing)
    {
        var bar = left ? playerBar : enemyBar;
        if (!external || bar == null || bar.gauge == null) return;
        bool ready = max > 0 && current >= max, lit = ready || firing;
        bar.gauge.fillColor = lit ? gaugeReadyColor : gaugeFillColor;
        bar.gauge.SetValues(firing ? 1f : max > 0 ? Mathf.Clamp01((float)current / max) : 0f, 0, lit);
        bar.gaugeLabel.text = firing ? move + "  발동" : ready ? move + "  준비 · 가슴 앞 X자" : move + "  " + current + " / " + max;
        bar.gaugeLabel.color = lit ? new Color(1f, .84f, .62f) : new Color(.83f, .88f, .88f);
    }

    public void ShowExternalClock(float seconds)
    {
        if (!external || timerText == null) return;
        timerText.text = Mathf.CeilToInt(Mathf.Max(0f, seconds)).ToString("D2");
        timerText.color = seconds <= 10f ? new Color(1f, .36f, .25f) : new Color(1f, .93f, .77f);
    }

    // 검사용: 지금 화면에 나와 있는 글
    public bool ExternalVisible => external && externalBuilt && canvas != null && canvas.gameObject.activeSelf;
    public string ExternalClockText => timerText != null ? timerText.text : "";
    public string ExternalSummary(bool left)
    {
        var bar = left ? playerBar : enemyBar;
        if (bar == null) return "";
        return bar.label.text + " | " + bar.amount.text + " | " + (bar.gaugeLabel != null ? bar.gaugeLabel.text : "");
    }

    private void Unbind(Bar bar)
    {
        if (bar == null) return;
        if (bar.health != null)
        {
            bar.health.OnHealthChanged -= bar.healthChanged;
            bar.health.OnDeath -= OnDeath;
        }
        if (bar.resource != null) bar.resource.OnChargeChanged -= bar.chargeChanged;
        if (bar.foeResource != null && bar.chargeChanged != null) bar.foeResource.OnChargeChanged -= bar.chargeChanged;
    }

    private void OnChanged(Bar bar, int current, int max)
    {
        if (bar == null) return;
        float next = max > 0 ? Mathf.Clamp01((float)current / max) : 0;
        if (next < bar.targetRatio) bar.trailUntil = Time.unscaledTime + damageTrailDelay;
        // A heal/reset must never leave the previous damage trail behind.
        else if (next > bar.targetRatio) bar.chipRatio = next;
        bar.targetRatio = next;
        bar.graphic.SetValues(next, bar.chipRatio);
        string name = bar.health.DisplayName;
        if (string.IsNullOrWhiteSpace(name) || name == "PLAYER" || name == "ENEMY")
            name = bar.leftSide ? "서청운" : "상대";
        bar.label.text = name;
        bar.amount.text = current + " / " + max;
    }

    private void UpdateChip(Bar bar)
    {
        if (bar == null || Time.unscaledTime < bar.trailUntil) return;
        if (Mathf.Approximately(bar.chipRatio, bar.targetRatio)) return;
        bar.chipRatio = Mathf.MoveTowards(bar.chipRatio, bar.targetRatio, chipSpeed * Time.unscaledDeltaTime);
        bar.graphic.SetValues(bar.targetRatio, bar.chipRatio);
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

    public void ShowTimeOut(bool won)
    {
        matchOver = true;
        koText.text = "TIME UP\n" + (won ? "YOU WIN" : "YOU LOSE")
                    + (allowRestart ? "\n[" + restartKey + "] 키로 다시 시작" : "");
        koText.color = won ? new Color(1f, 0.9f, 0.4f) : new Color(1f, 0.45f, 0.45f);
        koText.gameObject.SetActive(true);
    }

    private void BuildTimer()
    {
        if (battle == null && !external) return;
        var rect = CreateRect("RoundTimer", topRail);
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1);
        rect.pivot = new Vector2(.5f, 1);
        rect.anchoredPosition = new Vector2(0, -35);
        rect.sizeDelta = new Vector2(176, 92);
        timerText = CreateText("Seconds", rect, 76, TextAnchor.MiddleCenter);
        timerText.font = WuxiaTheme.DisplayFont;
        Stretch(timerText.rectTransform);
        AddShadow(timerText, new Vector2(2, -3));
        UpdateTimer();
    }

    private void UpdateTimer()
    {
        if (timerText == null || battle == null) return;
        timerText.text = Mathf.CeilToInt(battle.RemainingSeconds).ToString("D2");
        timerText.color = battle.RemainingSeconds <= 10f
            ? new Color(1f, .36f, .25f) : new Color(1f, .93f, .77f);
    }

    private void RestartRound()
    {
        matchOver = false;
        koText.gameObject.SetActive(false);

        RestoreFighter(playerHealth, playerStartPos);
        RestoreFighter(enemyHealth, enemyStartPos);

        if (playerBar != null) playerBar.chipRatio = 1f;
        if (enemyBar != null) enemyBar.chipRatio = 1f;

        if (special != null) special.ResetCharge();
        if (enemyBar != null && enemyBar.resource != null) enemyBar.resource.ResetCharge();
        if (enemyBar != null && enemyBar.foeResource != null) enemyBar.foeResource.ResetForRound();
        if (battle != null) battle.ResetRoundTimer();
        UpdateTimer();
    }

    private void RestoreFighter(Health health, Vector3 position)
    {
        if (health == null) return;
        health.transform.position = position;
        var rb = health.GetComponent<Rigidbody2D>();
        if (rb != null) rb.linearVelocity = Vector2.zero;
        health.ResetHealth();
    }

    // A resource is displayed only from a real SpecialMove component; no fictitious enemy MP.
    private void BuildSpecialGauge(Bar bar, RectTransform parent)
    {
        bar.resource = bar.health != null ? bar.health.GetComponent<SpecialMove>() : null;
        bar.foeResource = bar.health != null && bar.resource == null ? bar.health.GetComponent<EnemySpecial>() : null;
        bool shown = bar.health == null || bar.resource != null || (bar.foeResource != null && bar.foeResource.Available);
        bar.gauge = CreateBarGraphic("SpecialGauge", parent, true, !bar.leftSide, gaugeFillColor);
        var r = bar.gauge.rectTransform;
        r.anchorMin = new Vector2(bar.leftSide ? 0 : .13f, 0);
        r.anchorMax = new Vector2(bar.leftSide ? .87f : 1, 0);
        r.offsetMin = new Vector2(bar.leftSide ? 26 : 0, -22);
        r.offsetMax = new Vector2(bar.leftSide ? 0 : -26, -5);
        bar.gaugeLabel = CreateText("GaugeLabel", parent, 16, bar.leftSide ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight);
        var l = bar.gaugeLabel.rectTransform;
        l.anchorMin = Vector2.zero; l.anchorMax = new Vector2(1,0);
        l.offsetMin = new Vector2(29,-47); l.offsetMax = new Vector2(-29,-24);
        bar.gaugeLabel.color = new Color(.65f,.76f,.79f);
        AddShadow(bar.gaugeLabel, new Vector2(1,-1));
        bar.gauge.SetValues(0,0,false,shown);
        bar.gaugeLabel.text = "";
    }

    /// <summary>적 필살기 게이지: 기술 이름과 함께 채워지는 칸, 가득 차면 준비, 쓰는 중이면 발동, 쓰고 나면 사용함.</summary>
    private void OnFoeChargeChanged(Bar bar, int current, int max)
    {
        var s = bar.foeResource;
        if (s == null || bar.gauge == null) return;
        bool lit = s.IsReady || s.IsPlaying;
        float ratio = s.IsPlaying ? 1f : s.Used ? 0f : max > 0 ? (float)current / max : 0f;
        bar.gauge.fillColor = lit ? gaugeReadyColor : gaugeFillColor;
        bar.gauge.SetValues(ratio, 0, lit);
        bar.gaugeLabel.text = s.IsPlaying ? s.MoveName + "  발동"
            : s.Used ? s.MoveName + "  사용함"
            : s.IsReady ? s.MoveName + "  준비"
            : s.MoveName + "  " + current + " / " + max;
        bar.gaugeLabel.color = lit ? new Color(1f,.84f,.62f) : s.Used ? new Color(.6f,.62f,.62f) : new Color(.83f,.88f,.88f);
    }

    private void OnChargeChanged(Bar bar, int current, int max)
    {
        bool ready = max > 0 && current >= max;
        bar.gauge.fillColor = ready ? gaugeReadyColor : gaugeFillColor;
        bar.gauge.SetValues(max > 0 ? (float)current/max : 0, 0, ready);
        bar.gaugeLabel.text = ready
            ? "파운장 준비  [" + bar.resource.ActivateKey + "]"
            : "파운장  " + current + " / " + max;
        bar.gaugeLabel.color = ready ? new Color(.66f,.93f,1f) : new Color(.83f,.88f,.88f);
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

        topRail = CreateRect("DuelHeader", canvas.transform);
        topRail.anchorMin = topRail.anchorMax = new Vector2(.5f, 1);
        topRail.pivot = new Vector2(.5f, 1);
        topRail.sizeDelta = new Vector2(1920, 180);
        topRail.anchoredPosition = Vector2.zero;
        var shade = CreateRect("TopShade", topRail).gameObject.AddComponent<WuxiaHUDShade>();
        shade.raycastTarget = false;
        Stretch(shade.rectTransform);

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
        var rect = CreateRect(leftSide ? "PlayerVitals" : "EnemyVitals", topRail);
        rect.anchorMin = rect.anchorMax = new Vector2(leftSide ? 0 : 1, 1);
        rect.pivot = new Vector2(leftSide ? 0 : 1, 1);
        // Clamp authoring values to preserve the central timer and outer screen margins.
        float margin = Mathf.Clamp(barMargin.x, 36, 120);
        rect.sizeDelta = new Vector2(Mathf.Min(barSize.x, 960-margin-100), Mathf.Clamp(barSize.y, 36, 60));
        rect.anchoredPosition = new Vector2(leftSide ? margin : -margin, -Mathf.Max(56,barMargin.y));
        bar.graphic = CreateBarGraphic("HealthBar", rect, false, !leftSide, color);
        bar.graphic.trailColor = chipColor;
        Stretch(bar.graphic.rectTransform);

        bar.label = CreateText("FighterName", rect, 30, leftSide ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight);
        bar.label.font = WuxiaTheme.MenuFont;
        bar.label.color = new Color(1,.94f,.79f);
        bar.label.resizeTextForBestFit = true;
        bar.label.resizeTextMinSize = 20; bar.label.resizeTextMaxSize = 30;
        var name = bar.label.rectTransform;
        name.anchorMin = new Vector2(leftSide ? 0 : .24f,1);
        name.anchorMax = new Vector2(leftSide ? .76f : 1,1);
        name.offsetMin = new Vector2(29,3); name.offsetMax = new Vector2(-29,40);
        AddShadow(bar.label, new Vector2(1,-2));

        bar.amount = CreateText("HealthValue", rect, 17, leftSide ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft);
        bar.amount.color = new Color(.84f,.80f,.67f);
        var value = bar.amount.rectTransform;
        value.anchorMin = new Vector2(leftSide ? .76f : 0,1);
        value.anchorMax = new Vector2(leftSide ? 1 : .24f,1);
        value.offsetMin = new Vector2(29,4); value.offsetMax = new Vector2(-29,33);
        AddShadow(bar.amount, new Vector2(1,-1));
        BuildSpecialGauge(bar, rect);
        return bar;
    }

    private WuxiaCombatBar CreateBarGraphic(string name, Transform parent, bool resource, bool mirror, Color fill)
    {
        var g = CreateRect(name, parent).gameObject.AddComponent<WuxiaCombatBar>();
        g.resource = resource; g.mirror = mirror; g.fillColor = fill; g.raycastTarget = false;
        return g;
    }
    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
    private static void AddShadow(Text text, Vector2 offset)
    {
        var shadow = text.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0,0,0,.95f); shadow.effectDistance = offset;
    }

    private RectTransform CreateRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
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
        return WuxiaTheme.Font;
    }


}
