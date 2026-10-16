using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 적이 공격하기 직전, 머리 위에 빨간 느낌표를 띄워 플레이어에게 알린다.
/// EnemyAI 가 공격 딜레이를 시작할 때 Show, 공격이 나가거나 취소되면 Hide 를 부른다.
///
/// 화면 위 UI(Screen Space Overlay)로 그리므로 2D/3D 카메라와 상관없이 항상 정면을 보고,
/// 배경이나 다른 캐릭터에 가려지지 않는다. 위치는 매 프레임 캐릭터 렌더러의 머리 꼭대기를
/// 화면 좌표로 바꿔 따라간다. 3D 모델을 갈아끼워도(Fighter3DView.ApplyVisual) Show 할 때 다시 찾는다.
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
    private Collider2D bodyCollider;

    private bool visible;
    private float shownAt;
    private float duration;

    /// <summary>경고 표시. duration 초 뒤에 공격이 나간다.</summary>
    public void Show(float duration)
    {
        EnsureUI();

        // 모델이 바뀌었을 수 있으니 보일 때마다 다시 찾는다.
        renderers = GetComponentsInChildren<Renderer>();
        bodyCollider = GetComponent<Collider2D>();

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
        Camera cam = Camera.main;
        if (cam == null)
        {
            markRect.gameObject.SetActive(false);
            return;
        }

        Vector3 screen = cam.WorldToScreenPoint(HeadTop());
        bool onScreen = screen.z > 0f;   // 카메라 뒤에 있으면 그리지 않는다
        markRect.gameObject.SetActive(onScreen);
        if (!onScreen) return;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out Vector2 local);
        markRect.anchoredPosition = local + new Vector2(0f, screenOffset);

        float t = Time.time - shownAt;
        float scale = t < popTime
            ? Mathf.Lerp(popScale, 1f, t / popTime)
            : Mathf.Lerp(1f, finalScale, Mathf.Clamp01((t - popTime) / Mathf.Max(0.01f, duration - popTime)));
        markRect.localScale = Vector3.one * scale;
    }

    /// <summary>캐릭터 머리 꼭대기(월드 좌표). 3D 모델이면 메시, 2D 면 스프라이트 범위를 쓴다.</summary>
    private Vector3 HeadTop()
    {
        bool found = false;
        Bounds bounds = default;
        if (renderers != null)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                // 이펙트는 머리 높이 계산에서 뺀다.
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;

                if (!found) { bounds = r.bounds; found = true; }
                else bounds.Encapsulate(r.bounds);
            }
        }

        if (!found && bodyCollider != null)
        {
            bounds = bodyCollider.bounds;
            found = true;
        }

        if (!found) return transform.position + Vector3.up * 2f;
        return new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
    }

    private void EnsureUI()
    {
        if (canvas != null) return;

        // 캐릭터 밑에 두지 않는다. 캐릭터가 뒤집히거나 크기가 바뀌어도 UI 가 따라 변하지 않게 하려는 것.
        canvas = UIFactory.CreateCanvas(name + " AttackWarning", null, sortingOrder);
        canvasRect = (RectTransform)canvas.transform;
        Destroy(canvas.GetComponent<GraphicRaycaster>());   // 클릭을 가로채지 않게

        var text = UIFactory.CreateText("Mark", canvas.transform, symbol, fontSize, TextAnchor.MiddleCenter, color);
        text.fontStyle = FontStyle.Bold;

        // 밝은 배경에서도 잘 보이도록 테두리를 두른다.
        var outline = text.gameObject.AddComponent<Outline>();
        outline.effectColor = outlineColor;
        outline.effectDistance = new Vector2(4f, -4f);

        markRect = text.rectTransform;
        UIFactory.Place(markRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f), Vector2.zero,
                        new Vector2(fontSize, fontSize * 1.2f));
        markRect.gameObject.SetActive(false);
    }
}
