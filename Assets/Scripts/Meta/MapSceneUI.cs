using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Slay the Spire 식 지도 화면.
/// RunState 가 들고 있는 MapData 를 읽어서 칸과 연결선을 그리고,
/// 지금 갈 수 있는 칸을 누르면 전투씬으로 넘긴다.
///
/// 지도 데이터 자체는 MapGenerator 가 만들고 여기서는 "그리기 + 클릭"만 담당한다.
/// 디자인이 나오면 nodeIcons 배열에 스프라이트만 넣으면 된다.
/// </summary>
public class MapSceneUI : MonoBehaviour
{
    [Header("디자인 (비워두면 단색 도형으로 그린다)")]
    [Tooltip("NodeType 순서대로: 전투 / 강적 / 보스 / 휴식 / 이벤트 / 상점")]
    [SerializeField] private Sprite[] nodeIcons = new Sprite[6];
    [SerializeField] private Sprite backgroundSprite;

    [Header("배치")]
    [SerializeField] private float rowSpacing = 170f;
    [SerializeField] private float columnSpacing = 190f;
    [SerializeField] private float bottomPadding = 140f;
    [SerializeField] private float nodeSize = 92f;
    [SerializeField] private float lineThickness = 6f;
    [SerializeField] private float headerHeight = 110f;
    [SerializeField] private float footerHeight = 90f;

    [Header("색")]
    [SerializeField] private Color backgroundColor = new Color(0.07f, 0.08f, 0.12f);
    [SerializeField] private Color lineColor       = new Color(1f, 1f, 1f, 0.18f);
    [SerializeField] private Color lineClearedColor= new Color(0.95f, 0.75f, 0.25f, 0.55f);
    [SerializeField] private Color lockedColor     = new Color(0.28f, 0.30f, 0.36f);
    [SerializeField] private Color clearedColor    = new Color(0.45f, 0.48f, 0.55f);

    [Header("연출")]
    [Tooltip("고를 수 있는 칸이 커졌다 작아지는 폭.")]
    [SerializeField] private float pulseAmount = 0.08f;
    [SerializeField] private float pulseSpeed = 3f;

    private Canvas canvas;
    private ScrollRect scroll;
    private RectTransform content;
    private Text headerText;
    private Text messageText;

    private readonly List<RectTransform> pulsing = new List<RectTransform>();
    private readonly Dictionary<string, Vector2> nodePositions = new Dictionary<string, Vector2>();

    private RunState Run { get { return RunState.Instance; } }

    void Start()
    {
        UIFactory.EnsureEventSystem();

        // 지도 씬을 단독으로 실행했으면 임시 판을 하나 만들어서 바로 볼 수 있게 한다.
        if (Run != null && !Run.HasRun)
        {
            Debug.Log("[MapSceneUI] 진행 중인 판이 없어서 임시로 새 판을 만듭니다. (MapScene 단독 실행)");
            Run.StartNewRun();
        }
        if (Run != null) Run.RefreshStatus();

        BuildCanvas();
        BuildMap();
        RefreshHeader();
        ShowLastMessage();

        StartCoroutine(FocusOnCurrentRow());

        if (Run != null && Run.HasRun && Run.Data.finished) BuildClearOverlay();
    }

    /// <summary>보스까지 잡았을 때 뜨는 마무리 화면. 더 갈 칸이 없으므로 여기서 판을 정리한다.</summary>
    private void BuildClearOverlay()
    {
        var panel = UIFactory.CreateImage("ClearOverlay", canvas.transform, new Color(0f, 0f, 0f, 0.8f), true);
        UIFactory.Stretch(panel.rectTransform);

        var title = UIFactory.CreateText("ClearTitle", panel.transform, "STAGE CLEAR", 80,
                                         TextAnchor.MiddleCenter, new Color(0.95f, 0.75f, 0.25f));
        UIFactory.Place(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                        new Vector2(0f, 120f), new Vector2(1200f, 140f));

        var desc = UIFactory.CreateText("ClearDesc", panel.transform,
                                        "보스를 쓰러뜨렸습니다. 다음 스테이지는 아직 준비 중입니다.", 30,
                                        TextAnchor.MiddleCenter);
        UIFactory.Place(desc.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                        new Vector2(0f, 30f), new Vector2(1400f, 60f));

        var button = UIFactory.CreateButton("ClearButton", panel.transform, "시작 화면으로", 30,
                                            new Color(0.18f, 0.20f, 0.28f), OnFinishRun);
        UIFactory.Place(button.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                        new Vector2(0f, -80f), new Vector2(380f, 76f));
    }

    private void OnFinishRun()
    {
        // 끝난 판을 저장해두면 "이어하기" 로 들어와도 할 게 없으므로 여기서 지운다.
        if (Run != null) Run.AbandonRun();
        SceneRoutes.LoadStart();
    }

    void Update()
    {
        if (pulsing.Count == 0) return;
        float s = 1f + Mathf.Sin(Time.unscaledTime * pulseSpeed) * pulseAmount;
        for (int i = 0; i < pulsing.Count; i++)
            if (pulsing[i] != null) pulsing[i].localScale = new Vector3(s, s, 1f);
    }

    // ─────────────────────── 뼈대 ───────────────────────

    private void BuildCanvas()
    {
        canvas = UIFactory.CreateCanvas("MapCanvas", transform, 0);

        var bg = UIFactory.CreateImage("Background", canvas.transform, backgroundColor);
        if (backgroundSprite != null) { bg.sprite = backgroundSprite; bg.color = Color.white; }
        UIFactory.Stretch(bg.rectTransform);

        // 스크롤 영역 (위/아래 바를 피해서 자리를 잡는다)
        var scrollGO = new GameObject("MapScroll", typeof(RectTransform), typeof(ScrollRect));
        scrollGO.transform.SetParent(canvas.transform, false);
        var scrollRect = (RectTransform)scrollGO.transform;
        UIFactory.Stretch(scrollRect, 0f, footerHeight, 0f, headerHeight);

        // 뷰포트는 거의 투명하지만 raycastTarget 이 켜져 있어야 드래그 스크롤이 먹는다.
        var viewport = UIFactory.CreateImage("Viewport", scrollGO.transform, new Color(0f, 0f, 0f, 0.002f), true);
        viewport.gameObject.AddComponent<RectMask2D>();
        UIFactory.Stretch(viewport.rectTransform);

        content = UIFactory.CreateRect("Content", viewport.transform);
        content.anchorMin = new Vector2(0f, 0f);
        content.anchorMax = new Vector2(1f, 0f);
        content.pivot     = new Vector2(0.5f, 0f);
        content.anchoredPosition = Vector2.zero;

        scroll = scrollGO.GetComponent<ScrollRect>();
        scroll.content = content;
        scroll.viewport = viewport.rectTransform;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40f;
        scroll.inertia = true;

        BuildHeader();
        BuildFooter();
    }

    private void BuildHeader()
    {
        var bar = UIFactory.CreateImage("Header", canvas.transform, new Color(0f, 0f, 0f, 0.55f));
        var rect = bar.rectTransform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(0f, headerHeight);
        rect.anchoredPosition = Vector2.zero;

        headerText = UIFactory.CreateText("HeaderText", bar.transform, "", 30, TextAnchor.MiddleLeft);
        UIFactory.Stretch(headerText.rectTransform, 40f, 0f, 40f, 0f);
    }

    private void BuildFooter()
    {
        var bar = UIFactory.CreateImage("Footer", canvas.transform, new Color(0f, 0f, 0f, 0.55f));
        var rect = bar.rectTransform;
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.sizeDelta = new Vector2(0f, footerHeight);
        rect.anchoredPosition = Vector2.zero;

        messageText = UIFactory.CreateText("Message", bar.transform, "", 26,
                                           TextAnchor.MiddleCenter, new Color(0.9f, 0.85f, 0.6f));
        UIFactory.Stretch(messageText.rectTransform, 300f, 0f, 300f, 0f);

        var menu = UIFactory.CreateButton("MenuButton", bar.transform, "메뉴", 26,
                                          new Color(0.18f, 0.20f, 0.28f), OnMenu);
        UIFactory.Place(menu.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                        new Vector2(40f, 0f), new Vector2(160f, 56f));
    }

    // ─────────────────────── 지도 그리기 ───────────────────────

    private void BuildMap()
    {
        if (Run == null || !Run.HasRun) return;
        var map = Run.Data.map;

        float height = bottomPadding * 2f + (map.rows - 1) * rowSpacing;
        content.sizeDelta = new Vector2(0f, height);

        nodePositions.Clear();
        foreach (var node in map.nodes)
            nodePositions[node.id] = NodePosition(node, map.columns);

        // 선을 먼저 만들어야 칸 아래에 깔린다 (uGUI 는 자식 순서대로 그린다)
        var edges = UIFactory.CreateRect("Edges", content);
        UIFactory.Stretch(edges);
        var nodes = UIFactory.CreateRect("Nodes", content);
        UIFactory.Stretch(nodes);

        foreach (var node in map.nodes)
        {
            bool fromCleared = node.status == NodeStatus.Cleared;
            foreach (var nextId in node.next)
            {
                Vector2 to;
                if (!nodePositions.TryGetValue(nextId, out to)) continue;
                var target = map.Find(nextId);
                bool travelled = fromCleared && target != null && target.status == NodeStatus.Cleared;
                DrawLine(edges, nodePositions[node.id], to, travelled ? lineClearedColor : lineColor);
            }
        }

        pulsing.Clear();
        foreach (var node in map.nodes)
            DrawNode(nodes, node);
    }

    private Vector2 NodePosition(MapNode node, int columns)
    {
        float x = (node.column - (columns - 1) * 0.5f) * columnSpacing;
        float y = bottomPadding + node.row * rowSpacing;
        return new Vector2(x, y);
    }

    private void DrawLine(RectTransform parent, Vector2 from, Vector2 to, Color color)
    {
        var img = UIFactory.CreateImage("Line", parent, color);
        var rect = img.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot     = new Vector2(0f, 0.5f);
        rect.anchoredPosition = from;

        Vector2 delta = to - from;
        rect.sizeDelta = new Vector2(delta.magnitude, lineThickness);
        rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
    }

    private void DrawNode(RectTransform parent, MapNode node)
    {
        bool available = node.status == NodeStatus.Available;
        bool cleared   = node.status == NodeStatus.Cleared;
        bool isCurrent = Run.Data.currentNodeId == node.id;

        Color color = available ? TypeColor(node.type) : (cleared ? clearedColor : lockedColor);

        var button = UIFactory.CreateButton("Node_" + node.id, parent, "", 20, color, null);
        var rect = button.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot     = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(nodeSize, nodeSize);
        rect.anchoredPosition = nodePositions[node.id];

        var img = button.GetComponent<Image>();
        var icon = IconFor(node.type);
        if (icon != null)
        {
            img.sprite = icon;
            img.preserveAspect = true;
            if (available) img.color = Color.white;
        }

        // 라벨은 칸 아래에 따로 붙인다 (아이콘을 가리지 않도록)
        var label = UIFactory.CreateText("Caption", button.transform, TypeLabel(node.type), 22,
                                         TextAnchor.UpperCenter,
                                         available ? Color.white : new Color(1f, 1f, 1f, 0.45f));
        UIFactory.Place(label.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f),
                        new Vector2(0f, -6f), new Vector2(180f, 30f));

        if (isCurrent)
        {
            var ring = UIFactory.CreateImage("Current", button.transform, new Color(1f, 1f, 1f, 0.25f));
            UIFactory.Stretch(ring.rectTransform, -12f, -12f, -12f, -12f);
            ring.transform.SetAsFirstSibling();
        }

        button.interactable = available;
        if (!available) return;

        var captured = node;
        button.onClick.AddListener(() => OnNodeClicked(captured));
        pulsing.Add(rect);
    }

    // ─────────────────────── 표시용 정보 ───────────────────────

    private Sprite IconFor(NodeType type)
    {
        int i = (int)type;
        if (nodeIcons == null || i < 0 || i >= nodeIcons.Length) return null;
        return nodeIcons[i];
    }

    private static string TypeLabel(NodeType type)
    {
        switch (type)
        {
            case NodeType.Battle: return "전투";
            case NodeType.Elite:  return "강적";
            case NodeType.Boss:   return "보스";
            case NodeType.Rest:   return "휴식";
            case NodeType.Event:  return "이벤트";
            case NodeType.Shop:   return "상점";
        }
        return "";
    }

    private static Color TypeColor(NodeType type)
    {
        switch (type)
        {
            case NodeType.Battle: return new Color(0.85f, 0.30f, 0.28f);
            case NodeType.Elite:  return new Color(0.72f, 0.25f, 0.70f);
            case NodeType.Boss:   return new Color(0.95f, 0.55f, 0.10f);
            case NodeType.Rest:   return new Color(0.30f, 0.72f, 0.45f);
            case NodeType.Event:  return new Color(0.35f, 0.55f, 0.90f);
            case NodeType.Shop:   return new Color(0.90f, 0.80f, 0.30f);
        }
        return Color.gray;
    }

    private void RefreshHeader()
    {
        if (headerText == null || Run == null || !Run.HasRun) return;
        var d = Run.Data;
        headerText.text = string.Format("{0}층 / {1}층      HP {2} / {3}      골드 {4}",
                                        Run.CurrentFloor, d.map.rows, d.playerHealth, d.playerMaxHealth, d.gold);
    }

    private void ShowLastMessage()
    {
        if (messageText == null || Run == null) return;
        messageText.text = Run.LastMessage ?? "위로 올라갈 칸을 고르세요";
        Run.LastMessage = null;
    }

    /// <summary>지금 서 있는 층이 화면 가운데에 오도록 스크롤을 맞춘다.</summary>
    private IEnumerator FocusOnCurrentRow()
    {
        yield return null;                       // 캔버스 레이아웃이 한 번 돌 때까지 기다린다
        if (scroll == null || content == null) yield break;

        float viewHeight = scroll.viewport != null ? scroll.viewport.rect.height : 0f;
        float total = content.rect.height;
        if (total <= viewHeight) yield break;

        int row = Run != null && Run.CurrentNode != null ? Run.CurrentNode.row : 0;
        float focusY = bottomPadding + row * rowSpacing;
        scroll.verticalNormalizedPosition = Mathf.Clamp01((focusY - viewHeight * 0.5f) / (total - viewHeight));
    }

    // ─────────────────────── 입력 ───────────────────────

    private void OnNodeClicked(MapNode node)
    {
        if (Run == null) return;

        // 전투 칸이면 여기서 씬이 바뀐다. 나머지는 지도에 남으므로 다시 그린다.
        Run.EnterNode(node);
        if (node.IsBattle) return;

        Rebuild();
    }

    private void Rebuild()
    {
        // Destroy 는 프레임 끝에 처리되므로, 새로 그린 것과 겹치지 않게 부모부터 떼어낸다.
        for (int i = content.childCount - 1; i >= 0; i--)
        {
            var child = content.GetChild(i);
            child.SetParent(null, false);
            Destroy(child.gameObject);
        }
        pulsing.Clear();

        Run.RefreshStatus();
        BuildMap();
        RefreshHeader();
        ShowLastMessage();
    }

    private void OnMenu()
    {
        if (Run != null) Run.Save();
        SceneRoutes.LoadStart();
    }
}
