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

    [Header("조작 안내")]
    [Tooltip("플레이 모드에 들어가면 좌측 하단에 키 안내를 띄운다.")]
    [SerializeField] private bool showKeyGuide = true;
    [Tooltip("안내를 켜고 끄는 키.")]
    [SerializeField] private KeyCode keyGuideToggleKey = KeyCode.F1;
    [Tooltip("0 보다 크면 이 시간(초, 실제 시간)이 지난 뒤 안내가 저절로 사라진다. 0 이면 계속 표시.")]
    [SerializeField] private float keyGuideAutoHideTime = 0f;
    [SerializeField] private float keyGuideWidth = 470f;
    [SerializeField] private Vector2 keyGuideMargin = new Vector2(60f, 40f);

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

    // 조작 안내
    private const float GuidePad = 16f;      // 패널 안쪽 여백
    private const float GuideRow = 32f;      // 한 줄 높이
    private const float GuideTop = GuidePad + GuideRow + 8f;   // 제목 아래, 첫 줄이 시작되는 y
    private const float GuideKeyCol = 140f;  // 키 이름이 들어가는 왼쪽 칸 너비

    private PlayerController player;
    private RectTransform keyGuidePanel;
    private readonly List<GameObject> guideRows = new List<GameObject>();
    private string guideSignature;           // 안내 내용이 바뀌었는지 판단하는 값
    private bool guideVisible;
    private float guideShownAt;              // 자동 숨김 기준 시각 (실제 시간)

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
        if (RedboolPvp.Active) { enabled=false; return; }
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
        BuildKeyGuide();

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
        UpdateKeyGuide();
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
        if (battle == null) return;
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
        bar.resource = bar.health.GetComponent<SpecialMove>();
        bar.foeResource = bar.resource == null ? bar.health.GetComponent<EnemySpecial>() : null;
        bool shown = bar.resource != null || (bar.foeResource != null && bar.foeResource.Available);
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
            : s.MoveName + "  충전 중";
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

    // ─────────────────────── 조작 안내 ───────────────────────

    /// <summary>
    /// 좌측 하단 키 안내 패널의 껍데기만 만든다. 줄 내용은 첫 Update 에서 채운다.
    /// 표시할 키는 PlayerController 에서 그대로 읽으므로 인스펙터에서 키를 바꾸면 안내도 따라간다.
    /// </summary>
    private void BuildKeyGuide()
    {
        if (!showKeyGuide) return;

        var bg = CreateImage("KeyGuide", canvas.transform, new Color(0f, 0f, 0f, 0.62f));
        keyGuidePanel = bg.rectTransform;
        keyGuidePanel.anchorMin = Vector2.zero;
        keyGuidePanel.anchorMax = Vector2.zero;
        keyGuidePanel.pivot = Vector2.zero;
        keyGuidePanel.anchoredPosition = keyGuideMargin;
        keyGuidePanel.sizeDelta = new Vector2(keyGuideWidth, GuideTop + GuidePad);

        var title = CreateText("Title", keyGuidePanel, 24, TextAnchor.UpperLeft);
        title.color = new Color(1f, 0.9f, 0.4f);
        title.text = "조작";
        PlaceTopLeft(title.rectTransform, GuidePad, GuidePad, keyGuideWidth - GuidePad * 2f, GuideRow);

        guideVisible = true;
        guideShownAt = Time.unscaledTime;
    }

    private void UpdateKeyGuide()
    {
        if (keyGuidePanel == null) return;

        if (Input.GetKeyDown(keyGuideToggleKey))
        {
            guideVisible = !guideVisible;
            keyGuidePanel.gameObject.SetActive(guideVisible);
            guideShownAt = Time.unscaledTime;   // 다시 켰으면 자동 숨김 시간도 다시 잰다
        }

        if (!guideVisible) return;

        RefreshGuide();

        // 자동 숨김은 실제 시간 기준. 히트스톱으로 timeScale 이 흔들려도 영향받지 않는다.
        if (keyGuideAutoHideTime > 0f && Time.unscaledTime - guideShownAt >= keyGuideAutoHideTime)
        {
            guideVisible = false;
            keyGuidePanel.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// 안내 줄을 다시 만든다. 내용이 바뀔 때만 실제로 다시 그린다.
    /// allowRestart 는 BattleSceneBridge 가 Start 에서 꺼버릴 수 있고, externalControl 은
    /// 웹캠 패킷이 처음 들어오는 순간 켜지므로 Awake 한 번으로는 최신 상태를 알 수 없다.
    /// </summary>
    private void RefreshGuide()
    {
        bool keyboard = player == null || player.useKeyboard;
        bool external = player != null && player.externalControl;

        string signature = keyboard + "|" + external + "|" + allowRestart + "|" + (special != null);
        if (signature == guideSignature) return;
        guideSignature = signature;

        foreach (var row in guideRows) Destroy(row);
        guideRows.Clear();

        int i = 0;
        if (keyboard)
        {
            // 인식이 들어오면 앉기/막기는 키보드가 아니라 인식이 정한다.
            // (PlayerController.HandleKeyInput 의 externalControl 분기)
            string posture = external ? "   (지금은 웹캠이 결정)" : "";

            AddGuideRow(i++, Key(PlayerKey.Left) + " / " + Key(PlayerKey.Right), "이동 · 뒤로는 조금 느리다");
            bool boxing = player != null && player.GetComponent<Fighter3DView>() != null;
            // 3D 복싱에는 점프·앉기가 없다.
            if (!boxing)
            {
                AddGuideRow(i++, Key(PlayerKey.Jump), "점프");
                AddGuideRow(i++, Key(PlayerKey.Crouch), "앉기" + posture);
            }
            // 막기는 웹캠 제어 중에도 키로 쓸 수 있다. 3D 에서는 높이(상단/하단)를 상대 공격에 맞춰 자동으로 고른다.
            AddGuideRow(i++, Key(PlayerKey.Block), "막기 · 누르는 동안 유지"
                + (boxing ? " · 높이 자동" : ""));
            AddGuideRow(i++, Key(PlayerKey.Attack), boxing ? "스트레이트" : "기본 공격 · 앉아서 로우킥 / 공중 하이킥");
            if (player != null && player.GetComponent<Fighter3DView>() != null)
            {
                AddGuideRow(i++, player.jabKey + " / " + player.straightKey, "잽 / 스트레이트");
                AddGuideRow(i++, player.highKickKey + " / " + player.lowKickKey, "하이킥 / 로우킥");
            }
            if (special != null)
                AddGuideRow(i++, special.ActivateKey.ToString(),
                            "파운장 · 바로 발사 · 일반 공격 " + special.HitsToCharge + "회 적중으로 충전 · " + special.Damage + " 피해");
        }
        else
        {
            AddGuideRow(i++, "웹캠", "키보드 입력이 꺼져 있다 (인식 전용)");
        }

        // 게이지 채우기는 useKeyboard 와 무관하게 먹으므로 키보드 분기 밖에 둔다.
        if (special != null && special.ChargeKey != KeyCode.None)
            AddGuideRow(i++, special.ChargeKey.ToString(), "파운장 게이지 채우기 (테스트용)");

        if (allowRestart)
            AddGuideRow(i++, restartKey.ToString(), "전투 종료 후 다시 시작");

        AddGuideRow(i++, keyGuideToggleKey.ToString(), "이 안내 켜기 / 끄기");

        keyGuidePanel.sizeDelta = new Vector2(keyGuideWidth, GuideTop + i * GuideRow + GuidePad);
    }

    /// <summary>
    /// 한 줄을 키 칸 / 설명 칸 두 개의 텍스트로 만든다.
    /// 여러 줄짜리 텍스트 두 개로 칸을 나누면 한글 폰트 폴백 때문에 줄 높이가 서로 달라져
    /// 아래로 갈수록 두 칸이 어긋난다. 줄마다 y 를 직접 잡아야 정확히 맞는다.
    /// </summary>
    private void AddGuideRow(int index, string key, string desc)
    {
        float y = GuideTop + index * GuideRow;

        var k = CreateText("Key" + index, keyGuidePanel, 22, TextAnchor.MiddleLeft);
        k.text = key;
        PlaceTopLeft(k.rectTransform, GuidePad, y, GuideKeyCol, GuideRow);

        var d = CreateText("Desc" + index, keyGuidePanel, 22, TextAnchor.MiddleLeft);
        d.text = desc;
        d.color = new Color(0.85f, 0.9f, 1f);
        PlaceTopLeft(d.rectTransform, GuidePad + GuideKeyCol, y,
                     keyGuideWidth - GuidePad * 2f - GuideKeyCol, GuideRow);

        guideRows.Add(k.gameObject);
        guideRows.Add(d.gameObject);
    }

    private enum PlayerKey { Left, Right, Jump, Crouch, Block, Attack }

    /// <summary>PlayerController 에 설정된 키 이름. 플레이어가 없으면 기본값을 쓴다.</summary>
    private string Key(PlayerKey which)
    {
        switch (which)
        {
            case PlayerKey.Left:   return (player != null ? player.moveLeftKey  : KeyCode.A).ToString();
            case PlayerKey.Right:  return (player != null ? player.moveRightKey : KeyCode.D).ToString();
            case PlayerKey.Jump:   return (player != null ? player.jumpKey      : KeyCode.W).ToString();
            case PlayerKey.Crouch: return (player != null ? player.crouchKey    : KeyCode.S).ToString();
            case PlayerKey.Block:  return (player != null ? player.blockKey     : KeyCode.U).ToString();
            default:               return (player != null ? player.attackKey    : KeyCode.P).ToString();
        }
    }

    /// <summary>부모의 왼쪽 위를 기준으로 배치한다. (x, y 는 왼쪽 위에서의 거리)</summary>
    private static void PlaceTopLeft(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
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
        return WuxiaTheme.Font;
    }


}
