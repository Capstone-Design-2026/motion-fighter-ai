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
    [SerializeField] private Vector2 gaugeSize = new Vector2(360f, 22f);
    [Tooltip("체력바 아래로 얼마나 내릴지.")]
    [SerializeField] private float gaugeTopOffset = 132f;
    [SerializeField] private Color gaugeEmptyColor = new Color(1f, 1f, 1f, 0.16f);
    [SerializeField] private Color gaugeFillColor = new Color(0.35f, 0.7f, 1f);
    [SerializeField] private Color gaugeReadyColor = new Color(1f, 0.85f, 0.25f);

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

    // 필살기 게이지
    private SpecialMove special;
    private Image[] gaugeCells;
    private Text gaugeLabel;

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

    // HUD가 생성되기 전에 새 프리팹의 Health로 연결한다.
    public void SetTargets(Health player, Health enemy)
    {
        playerHealth = player;
        enemyHealth = enemy;
    }

    void Awake()
    {
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
        BuildSpecialGauge();
        BuildKeyGuide();

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
        UpdateKeyGuide();
        UpdateTimer();

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
        var panel = CreateImage("RoundTimer", canvas.transform, new Color(0.06f, 0.05f, 0.04f, 0.9f));
        var rect = panel.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -24f);
        rect.sizeDelta = new Vector2(160f, 104f);
        timerText = CreateText("Seconds", rect, 58, TextAnchor.MiddleCenter);
        timerText.rectTransform.anchorMin = Vector2.zero;
        timerText.rectTransform.anchorMax = Vector2.one;
        timerText.rectTransform.offsetMin = timerText.rectTransform.offsetMax = Vector2.zero;
        UpdateTimer();
    }

    private void UpdateTimer()
    {
        if (timerText == null || battle == null) return;
        timerText.text = Mathf.CeilToInt(battle.RemainingSeconds).ToString("D2");
        timerText.color = battle.RemainingSeconds <= 10f
            ? new Color(1f, 0.3f, 0.25f) : new Color(1f, 0.88f, 0.6f);
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

    // ─────────────────────── 필살기 게이지 ───────────────────────

    /// <summary>
    /// 플레이어 체력바 아래에 칸으로 나뉜 게이지를 만든다.
    /// 몇 대 맞아야 차는지는 SpecialMove 가 정하므로 칸 수도 거기서 받아온다.
    /// </summary>
    private void BuildSpecialGauge()
    {
        if (special == null) return;

        int cells = Mathf.Max(1, special.HitsToCharge);

        var root = CreateImage("SpecialGauge", canvas.transform, new Color(0f, 0f, 0f, 0.55f));
        var rect = root.rectTransform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = gaugeSize;
        rect.anchoredPosition = new Vector2(barMargin.x, -gaugeTopOffset);

        // 칸 사이를 4px 씩 띄운다. 남은 폭을 칸 수로 나눠 쓴다.
        const float gap = 4f;
        const float pad = 4f;
        float cellWidth = (gaugeSize.x - pad * 2f - gap * (cells - 1)) / cells;

        gaugeCells = new Image[cells];
        for (int i = 0; i < cells; i++)
        {
            var cell = CreateImage("Cell" + i, root.transform, gaugeEmptyColor);
            var crect = cell.rectTransform;
            crect.anchorMin = new Vector2(0f, 0f);
            crect.anchorMax = new Vector2(0f, 1f);
            crect.pivot = new Vector2(0f, 0.5f);
            crect.offsetMin = new Vector2(pad + i * (cellWidth + gap), pad);
            crect.offsetMax = new Vector2(pad + i * (cellWidth + gap) + cellWidth, -pad);
            gaugeCells[i] = cell;
        }

        gaugeLabel = CreateText("GaugeLabel", root.transform, 22, TextAnchor.UpperLeft);
        var lrect = gaugeLabel.rectTransform;
        lrect.anchorMin = new Vector2(0f, 0f);
        lrect.anchorMax = new Vector2(1f, 0f);
        lrect.pivot = new Vector2(0.5f, 1f);
        lrect.offsetMin = new Vector2(2f, -32f);
        lrect.offsetMax = new Vector2(-2f, -4f);

        special.OnChargeChanged += OnChargeChanged;
        OnChargeChanged(special.Charge, special.HitsToCharge);
    }

    private void OnChargeChanged(int current, int max)
    {
        if (gaugeCells == null) return;

        bool ready = current >= max;

        for (int i = 0; i < gaugeCells.Length; i++)
        {
            if (gaugeCells[i] == null) continue;
            gaugeCells[i].color = i < current
                ? (ready ? gaugeReadyColor : gaugeFillColor)
                : gaugeEmptyColor;
        }

        if (gaugeLabel != null)
        {
            gaugeLabel.text = ready
                ? "필살기 준비 완료   [" + special.ActivateKey + "]"
                : "필살기   " + current + " / " + max;
            gaugeLabel.color = ready ? gaugeReadyColor : new Color(0.8f, 0.85f, 0.95f);
        }
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
            if (!boxing)
            {
                AddGuideRow(i++, Key(PlayerKey.Jump), "점프");
                AddGuideRow(i++, Key(PlayerKey.Crouch), "앉기" + posture);
            }
            AddGuideRow(i++, Key(PlayerKey.Block), "기본 방어 · 누르고 있는 동안" + posture);
            if (boxing) AddGuideRow(i++, Key(PlayerKey.Crouch) + " + " + Key(PlayerKey.Block), "로우킥 방어" + posture);
            AddGuideRow(i++, Key(PlayerKey.Attack), boxing ? "스트레이트" : "기본 공격 · 앉아서 로우킥 / 공중 하이킥");
            if (player != null && player.GetComponent<Fighter3DView>() != null)
            {
                AddGuideRow(i++, player.jabKey + " / " + player.straightKey, "잽 / 스트레이트");
                AddGuideRow(i++, player.highKickKey + " / " + player.lowKickKey, "하이킥 / 로우킥");
            }
            if (special != null)
                AddGuideRow(i++, special.ActivateKey.ToString(),
                            "필살기 · " + special.HitsToCharge + "번 맞으면 사용 가능");
        }
        else
        {
            AddGuideRow(i++, "웹캠", "키보드 입력이 꺼져 있다 (인식 전용)");
        }

        // 게이지 채우기는 useKeyboard 와 무관하게 먹으므로 키보드 분기 밖에 둔다.
        if (special != null && special.ChargeKey != KeyCode.None)
            AddGuideRow(i++, special.ChargeKey.ToString(), "필살기 게이지 채우기 (테스트용)");

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
