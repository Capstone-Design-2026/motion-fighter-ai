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
    [Header("디자인 (비워두면 산수화와 기본 표식)")]
    [Tooltip("NodeType 순서대로: 전투 / 강적 / 보스 / 휴식 / 이벤트 / 상점")]
    [SerializeField] private Sprite[] nodeIcons = new Sprite[6];
    [SerializeField] private Sprite backgroundSprite;

    [Header("배치")]
    [SerializeField] private float rowSpacing = 215f;
    [SerializeField] private float columnSpacing = 240f;
    [SerializeField] private float bottomPadding = 140f;
    [SerializeField] private float nodeSize = 92f;
    [SerializeField] private float lineThickness = 3f;
    [SerializeField] private float headerHeight = 110f;
    [SerializeField] private float footerHeight = 90f;


    [Header("연출")]
    [Tooltip("고를 수 있는 칸이 커졌다 작아지는 폭.")]
    [SerializeField] private float pulseAmount = 0.025f;
    [SerializeField] private float pulseSpeed = 3f;

    private readonly HashSet<string> reachable = new HashSet<string>();

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

        WuxiaBackdrop.Create(canvas.transform, backgroundSprite, true);
        var wash=UIFactory.CreateImage("MapReadingVeil",canvas.transform,new Color(.96f,.91f,.8f,.06f));
        UIFactory.Stretch(wash.rectTransform);

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
        var bar = UIFactory.CreateImage("Header", canvas.transform, new Color(.94f,.88f,.76f,.94f));
        var rect = bar.rectTransform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(0f, headerHeight);
        rect.anchoredPosition = Vector2.zero;

        WuxiaTheme.Rule(bar.transform,0,new Color(.36f,.29f,.19f,.45f));
        var title=WuxiaTheme.Text("MapTitle",bar.transform,"무예의 길",36,TextAnchor.MiddleLeft,WuxiaTheme.Ink);
        UIFactory.Place(title.rectTransform,new Vector2(0,.5f),new Vector2(0,.5f),new Vector2(48,0),new Vector2(260,60));
        var legend=WuxiaTheme.Text("Legend",bar.transform,"전투  ·  휴식  ·  최종 보스\n붉은 테두리: 선택 가능   /   휠·드래그: 지도 이동",19,TextAnchor.MiddleRight,WuxiaTheme.Ink);
        UIFactory.Place(legend.rectTransform,new Vector2(1,.5f),new Vector2(1,.5f),new Vector2(-40,0),new Vector2(600,68));
        headerText = WuxiaTheme.Text("HeaderText", bar.transform, "", 25, TextAnchor.MiddleLeft,WuxiaTheme.Ink);
        UIFactory.Stretch(headerText.rectTransform, 330f, 0f, 640f, 0f);
    }

    private void BuildFooter()
    {
        var bar = UIFactory.CreateImage("Footer", canvas.transform, new Color(.94f,.88f,.76f,.94f));
        var rect = bar.rectTransform;
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.sizeDelta = new Vector2(0f, footerHeight);
        rect.anchoredPosition = Vector2.zero;

        messageText = WuxiaTheme.Text("Message", bar.transform, "", 26,
                                           TextAnchor.MiddleCenter, WuxiaTheme.Ink);
        UIFactory.Stretch(messageText.rectTransform, 300f, 0f, 300f, 0f);

        var menu = UIFactory.CreateButton("MenuButton", bar.transform, "메뉴", 26,
                                          WuxiaTheme.Ink, OnMenu);
        WuxiaTheme.StyleButton(menu);
        WuxiaTheme.Rule(bar.transform,1,new Color(.36f,.29f,.19f,.45f));
        UIFactory.Place(menu.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                        new Vector2(40f, 0f), new Vector2(160f, 56f));
    }

    // ─────────────────────── 지도 그리기 ───────────────────────

    private void BuildMap()
    {
        if (Run == null || !Run.HasRun) return;
        var map = Run.Data.map;
        reachable.Clear();
        var queue = new Queue<MapNode>();
        foreach (var node in map.nodes)
            if (node.status == NodeStatus.Available) queue.Enqueue(node);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (!reachable.Add(node.id)) continue;
            foreach (var id in node.next)
            {
                var next = map.Find(id);
                if (next != null) queue.Enqueue(next);
            }
        }

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
                int visitedIndex = Run.Data.clearedNodeIds.IndexOf(node.id);
                bool travelled = fromCleared && target != null && visitedIndex >= 0
                    && visitedIndex + 1 < Run.Data.clearedNodeIds.Count
                    && Run.Data.clearedNodeIds[visitedIndex + 1] == nextId;
                bool ahead = (reachable.Contains(node.id) || node.id == Run.Data.currentNodeId) && reachable.Contains(nextId);
                DrawLine(edges, nodePositions[node.id], to, travelled ? new Color(.62f,.2f,.13f,.8f)
                    : (ahead ? new Color(.18f,.15f,.10f,.85f) : new Color(.25f,.22f,.17f,.12f)));
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
        Vector2 delta=to-from;
        float length=delta.magnitude;
        if(length<=nodeSize+20)return;
        Vector2 direction=delta/length;
        // Trim the endpoints, then ink the route as short separated strokes.
        for(float d=nodeSize*.5f+10;d<length-nodeSize*.5f-10;d+=17)
        {
            var img=UIFactory.CreateImage("RouteDash",parent,color);
            UIFactory.Place(img.rectTransform,new Vector2(.5f,0),new Vector2(0,.5f),from+direction*d,
                new Vector2(Mathf.Min(8,length-nodeSize*.5f-10-d),lineThickness));
            img.rectTransform.localRotation=Quaternion.Euler(0,0,Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg);
        }
    }

    private void DrawNode(RectTransform parent, MapNode node)
    {
        bool available=node.status==NodeStatus.Available;
        bool cleared=node.status==NodeStatus.Cleared;
        bool current=Run.Data.currentNodeId==node.id;
        bool ahead=reachable.Contains(node.id);
        Color ink=node.type==NodeType.Rest?new Color(.08f,.25f,.15f):node.type==NodeType.Boss?new Color(.60f,.10f,.05f):new Color(.08f,.06f,.04f);
        if(!ahead&&!cleared)ink=new Color(.52f,.48f,.4f,.65f);
        var button=UIFactory.CreateButton("Node_"+node.id,parent,"",20,Color.clear,null);
        var rect=button.GetComponent<RectTransform>();
        UIFactory.Place(rect,new Vector2(.5f,0),new Vector2(.5f,.5f),nodePositions[node.id],new Vector2(nodeSize,nodeSize));
        var disk=WuxiaTheme.Ornament("SealPaper",rect,WuxiaGraphic.Shape.Disc,new Color(1f,.95f,.82f,1f));
        UIFactory.Stretch(disk.rectTransform);
        var ring=WuxiaTheme.Ornament("SealRing",rect,WuxiaGraphic.Shape.Ring,available?WuxiaTheme.Vermilion:ink);
        UIFactory.Stretch(ring.rectTransform);
        if(available)
        {
            var outer=WuxiaTheme.Ornament("ReachableRing",rect,WuxiaGraphic.Shape.Ring,WuxiaTheme.Vermilion);
            UIFactory.Stretch(outer.rectTransform,-7,-7,-7,-7);
            pulsing.Add(outer.rectTransform);
        }
        var stage=Run.Config.Get(node.stageId);
        var icon=stage!=null&&stage.portrait!=null?stage.portrait:IconFor(node.type);
        if(icon!=null)
        {
            var portrait=UIFactory.CreateImage("Portrait",rect,Color.white);portrait.sprite=icon;portrait.preserveAspect=true;
            UIFactory.Stretch(portrait.rectTransform,15,15,15,15);
        }
        else
        {
            var shape=node.type==NodeType.Rest?WuxiaGraphic.Shape.Pavilion:node.type==NodeType.Boss?WuxiaGraphic.Shape.Fortress:WuxiaGraphic.Shape.Swords;
            var glyph=WuxiaTheme.Ornament("NodeSymbol",rect,shape,ink);UIFactory.Stretch(glyph.rectTransform,17,17,17,17);
        }
        var name=stage!=null?stage.displayName:TypeLabel(node.type);
        var detail=stage!=null?"HP "+stage.enemyMaxHealth+(stage.overrideCombatStats?" · 공격 "+stage.attackDamage:""):
            node.type==NodeType.Rest?"체력 +"+Mathf.RoundToInt(Run.Config.restHealPercent*100)+"%":"";
        // An opaque paper strip keeps crossing routes from running through the caption.
        var caption=UIFactory.CreateImage("CaptionPaper",rect,new Color(.99f,.94f,.83f,1f));
        UIFactory.Place(caption.rectTransform,new Vector2(.5f,0),new Vector2(.5f,1),new Vector2(0,-12),new Vector2(220,66));
        var label=WuxiaTheme.Text("Caption",caption.transform,name+"\n"+detail,22,TextAnchor.MiddleCenter,ink);
        UIFactory.Stretch(label.rectTransform,3,0,3,0);label.horizontalOverflow=HorizontalWrapMode.Wrap;
        label.resizeTextForBestFit=true;label.resizeTextMinSize=18;label.resizeTextMaxSize=22;label.fontStyle=FontStyle.Bold;
        if(cleared)
        {
            var stamp=UIFactory.CreateImage("ClearedStamp",rect,WuxiaTheme.Vermilion);
            UIFactory.Place(stamp.rectTransform,new Vector2(1,1),new Vector2(.5f,.5f),new Vector2(-3,-4),new Vector2(44,25));
            stamp.rectTransform.localRotation=Quaternion.Euler(0,0,-12);
            var done=WuxiaTheme.Text("StampText",stamp.transform,"완료",15,TextAnchor.MiddleCenter,WuxiaTheme.Paper);UIFactory.Stretch(done.rectTransform);
        }
        if(current)
        {
            var tag=WuxiaTheme.Text("CurrentPosition",rect,"▼ 현재 위치",18,TextAnchor.MiddleCenter,WuxiaTheme.Vermilion);
            UIFactory.Place(tag.rectTransform,new Vector2(.5f,1),new Vector2(.5f,0),new Vector2(0,12),new Vector2(180,30));
        }
        button.targetGraphic=disk;
        var colors=button.colors;colors.highlightedColor=new Color(1.07f,.97f,.8f);colors.selectedColor=colors.highlightedColor;colors.disabledColor=Color.white;button.colors=colors;
        button.interactable=available;
        if(available)button.onClick.AddListener(()=>OnNodeClicked(node));
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

    private void RefreshHeader()
    {
        if (headerText == null || Run == null || !Run.HasRun) return;
        var d = Run.Data;
        int completed = 0, total = 0;
        foreach (var node in d.map.nodes)
        {
            total = Mathf.Max(total, node.battleNumber);
            if (node.IsBattle && d.clearedNodeIds.Contains(node.id)) completed++;
        }
        headerText.text = total > 0
            ? string.Format("보스 격파 {0} / {1}      HP {2} / {3}", completed, total, d.playerHealth, d.playerMaxHealth)
            : string.Format("{0}층 / {1}층      HP {2} / {3}", Run.CurrentFloor, d.map.rows, d.playerHealth, d.playerMaxHealth);
    }

    private void ShowLastMessage()
    {
        if (messageText == null || Run == null) return;
        messageText.text = Run.LastMessage ?? "앞길의 보스와 휴식 위치를 비교하세요 · 전투 5회 / 휴식 2회";
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
        StartCoroutine(FocusOnCurrentRow());
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
