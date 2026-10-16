using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 상대가 무방비한 동안 머리 위에 노란 "공격 기회!" 를 띄워, 지금 때리면 들어간다는 걸 플레이어에게 알린다.
///
/// 무방비 = Fighter.IsOpen. 딜레이가 걸려 아무것도 못 하는 동안이다.
///   막기 성공: 상대 공격을 막아내 상대 후딜이 길어진 동안
///   헛침    : 상대 공격이 빗나가 후딜이 길어진 동안
///   방어 실패: 상대가 아무것도 막지 못하고 방어가 끝나 딜레이가 걸린 동안
/// 아래 막대가 남은 시간을, 작은 글씨가 이유를 보여 준다. 무방비가 끝나거나 그 틈에 공격이 들어가면 바로 사라진다.
///
/// 빨간 느낌표(AttackWarning, 위험)와 같은 자리에 뜨지만 둘이 겹치지는 않는다 —
/// 무방비한 동안에는 공격을 정할 수 없기 때문이다(Fighter.CanAct).
///
/// EnemyAI 가 자동으로 붙인다. 매 프레임 Fighter 상태를 읽기만 하므로 따로 부를 것이 없다.
/// 그리는 방식은 OverheadUI 를 쓴다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Fighter))]
public class OpeningIndicator : MonoBehaviour
{
    [Header("모양")]
    [SerializeField] private string label = "공격 기회!";
    [SerializeField] private Color color = new Color(1f, 0.82f, 0.1f);
    [SerializeField] private Color outlineColor = new Color(0f, 0f, 0f, 0.9f);
    [Tooltip("기준 해상도(1920x1080)에서의 글자 크기.")]
    [SerializeField] private int fontSize = 56;
    [Tooltip("이유(막기 성공 / 헛침 / 방어 실패)를 작게 함께 적는다.")]
    [SerializeField] private bool showCause = true;
    [SerializeField] private int causeFontSize = 28;

    [Header("남은 시간 막대")]
    [SerializeField] private Vector2 barSize = new Vector2(170f, 12f);
    [SerializeField] private Color barBackground = new Color(0f, 0f, 0f, 0.65f);

    [Header("위치")]
    [Tooltip("머리 꼭대기에서 위쪽으로 얼마나 띄울지 (기준 해상도 픽셀).")]
    [SerializeField] private float screenOffset = 35f;

    [Header("연출")]
    [Tooltip("나타날 때 이 배율에서 1 로 줄어든다.")]
    [SerializeField] private float popScale = 1.5f;
    [SerializeField] private float popTime = 0.1f;
    [Tooltip("두근거리는 크기 변화 폭. 0 이면 멈춰 있다.")]
    [SerializeField] private float pulseAmount = 0.06f;
    [SerializeField] private float pulseSpeed = 12f;

    [Header("그리는 순서")]
    [Tooltip("HUD 보다 위, 전투 결과 화면·컷신(200 이상)보다 아래.")]
    [SerializeField] private int sortingOrder = 50;

    private Fighter fighter;

    private Canvas canvas;
    private RectTransform canvasRect;
    private RectTransform root;
    private Text causeText;
    private RectTransform barFill;
    private Renderer[] renderers;

    private bool visible;
    private float shownAt;

    void Awake()
    {
        fighter = GetComponent<Fighter>();
    }

    void LateUpdate()
    {
        // 캐릭터와 카메라가 이번 프레임 이동을 마친 뒤에 위치를 맞춘다.
        bool open = fighter.IsOpen;
        if (open && !visible) Show();
        else if (!open && visible) Hide();

        if (visible) UpdateMark();
    }

    void OnDisable()
    {
        Hide();
    }

    void OnDestroy()
    {
        if (canvas != null) Destroy(canvas.gameObject);
    }

    private void Show()
    {
        EnsureUI();
        renderers = OverheadUI.BodyRenderers(this);
        shownAt = Time.time;
        visible = true;
        root.gameObject.SetActive(true);
    }

    private void Hide()
    {
        visible = false;
        if (root != null) root.gameObject.SetActive(false);
    }

    private void UpdateMark()
    {
        bool onScreen = OverheadUI.PlaceAbove(canvasRect, root, OverheadUI.HeadTop(this, renderers), screenOffset);
        root.gameObject.SetActive(onScreen);
        if (!onScreen) return;

        if (causeText != null) causeText.text = CauseLabel(fighter.OpenCause);

        float ratio = fighter.OpenDuration > 0f ? Mathf.Clamp01(fighter.OpenRemaining / fighter.OpenDuration) : 0f;
        barFill.anchorMax = new Vector2(ratio, 1f);

        float t = Time.time - shownAt;
        float scale = t < popTime
            ? Mathf.Lerp(popScale, 1f, t / popTime)
            : 1f + Mathf.Sin((t - popTime) * pulseSpeed) * pulseAmount;
        root.localScale = Vector3.one * scale;
    }

    private static string CauseLabel(Fighter.OpeningCause cause)
    {
        switch (cause)
        {
            case Fighter.OpeningCause.Blocked:     return "막기 성공";
            case Fighter.OpeningCause.Whiffed:     return "헛침";
            case Fighter.OpeningCause.GuardFailed: return "방어 실패";
            default:                               return "";
        }
    }

    private void EnsureUI()
    {
        if (canvas != null) return;

        canvas = OverheadUI.CreateCanvas(name + " OpeningIndicator", sortingOrder);
        canvasRect = (RectTransform)canvas.transform;

        // 머리 위 지점을 아래 가운데로 두고, 아래부터 막대 → 이유 → "공격 기회!" 순으로 쌓는다.
        root = UIFactory.CreateRect("Opening", canvas.transform);
        float labelHeight = fontSize * 1.2f;
        float causeHeight = showCause ? causeFontSize * 1.25f : 0f;
        UIFactory.Place(root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f), Vector2.zero,
                        new Vector2(Mathf.Max(barSize.x, fontSize * 6f), barSize.y + causeHeight + labelHeight + 12f));

        var barBack = UIFactory.CreateImage("TimeBar", root, barBackground);
        UIFactory.Place(barBack.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, barSize);

        var fill = UIFactory.CreateImage("Fill", barBack.rectTransform, color);
        barFill = fill.rectTransform;
        barFill.anchorMin = Vector2.zero;
        barFill.anchorMax = Vector2.one;
        barFill.offsetMin = new Vector2(2f, 2f);
        barFill.offsetMax = new Vector2(-2f, -2f);
        barFill.pivot = new Vector2(0f, 0.5f);

        float y = barSize.y + 4f;
        if (showCause)
        {
            causeText = OverheadUI.CreateOutlinedText("Cause", root, "", causeFontSize, Color.white, outlineColor, 2f);
            UIFactory.Place(causeText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                            new Vector2(0f, y), new Vector2(fontSize * 6f, causeHeight));
            y += causeHeight;
        }

        var text = OverheadUI.CreateOutlinedText("Label", root, label, fontSize, color, outlineColor, 3f);
        UIFactory.Place(text.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                        new Vector2(0f, y), new Vector2(fontSize * 6f, labelHeight));

        root.gameObject.SetActive(false);
    }
}
