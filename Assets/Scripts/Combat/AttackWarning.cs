using UnityEngine;

/// <summary>
/// 적이 공격하기 직전, 머리 위에 빨간 느낌표를 띄워 플레이어에게 알린다.
/// EnemyAI 가 공격 딜레이를 시작할 때 Show, 공격이 나가거나 취소되면 Hide 를 부른다.
///
/// 그리는 방식(화면 UI, 머리 위 따라가기)은 OverheadUI 를 쓴다. OpeningIndicator 와 같다.
///
/// 연출: 크게 튀어나오며 나타나고(pop), 공격 순간이 다가올수록 조금씩 커진다.
/// 시간은 EnemyAI 의 딜레이와 같은 게임 시간(Time.time)으로 재므로, 슬로우 연출로 시간이 느려지면 같이 느려진다.
///
/// UI 는 처음 Show 할 때 코드로 만든다. (CombatHUD, UIFactory 와 같은 방식)
/// </summary>
[DisallowMultipleComponent]
public class AttackWarning : MonoBehaviour
{
    [Header("모양")]
    [SerializeField] private string symbol = "!";
    [SerializeField] private Color color = new Color(1f, 0.12f, 0.08f);
    [SerializeField] private Color outlineColor = new Color(0f, 0f, 0f, 0.9f);
    [Tooltip("기준 해상도(1920x1080)에서의 글자 크기.")]
    [SerializeField] private int fontSize = 110;

    [Header("위치")]
    [Tooltip("머리 꼭대기에서 위쪽으로 얼마나 띄울지 (기준 해상도 픽셀).")]
    [SerializeField] private float screenOffset = 45f;

    [Header("연출")]
    [Tooltip("나타날 때 이 배율에서 1 로 줄어든다.")]
    [SerializeField] private float popScale = 1.8f;
    [SerializeField] private float popTime = 0.08f;
    [Tooltip("공격 순간에 이 배율까지 커진다. 1 이면 크기가 그대로다.")]
    [SerializeField] private float finalScale = 1.3f;

    [Header("그리는 순서")]
    [Tooltip("HUD 보다 위, 전투 결과 화면·컷신(200 이상)보다 아래.")]
    [SerializeField] private int sortingOrder = 50;

    private Canvas canvas;
    private RectTransform canvasRect;
    private RectTransform markRect;
    private Renderer[] renderers;

    private bool visible;
    private float shownAt;
    private float duration;

    /// <summary>경고 표시. duration 초 뒤에 공격이 나간다.</summary>
    public void Show(float duration)
    {
        EnsureUI();
        renderers = OverheadUI.BodyRenderers(this);

        this.duration = Mathf.Max(0.01f, duration);
        shownAt = Time.time;
        visible = true;
        markRect.gameObject.SetActive(true);
        UpdateMark();
    }

    public void Hide()
    {
        visible = false;
        if (markRect != null) markRect.gameObject.SetActive(false);
    }

    void LateUpdate()
    {
        // 캐릭터와 카메라가 이번 프레임 이동을 마친 뒤에 위치를 맞춘다.
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

    private void UpdateMark()
    {
        bool onScreen = OverheadUI.PlaceAbove(canvasRect, markRect, OverheadUI.HeadTop(this, renderers), screenOffset);
        markRect.gameObject.SetActive(onScreen);
        if (!onScreen) return;

        float t = Time.time - shownAt;
        float scale = t < popTime
            ? Mathf.Lerp(popScale, 1f, t / popTime)
            : Mathf.Lerp(1f, finalScale, Mathf.Clamp01((t - popTime) / Mathf.Max(0.01f, duration - popTime)));
        markRect.localScale = Vector3.one * scale;
    }

    private void EnsureUI()
    {
        if (canvas != null) return;

        canvas = OverheadUI.CreateCanvas(name + " AttackWarning", sortingOrder);
        canvasRect = (RectTransform)canvas.transform;

        var text = OverheadUI.CreateOutlinedText("Mark", canvas.transform, symbol, fontSize, color, outlineColor, 4f);
        markRect = text.rectTransform;
        UIFactory.Place(markRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f), Vector2.zero,
                        new Vector2(fontSize, fontSize * 1.2f));
        markRect.gameObject.SetActive(false);
    }
}
