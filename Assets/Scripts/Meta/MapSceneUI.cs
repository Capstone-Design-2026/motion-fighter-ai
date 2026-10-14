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



    [Header("연출")]
    [Tooltip("고를 수 있는 칸이 커졌다 작아지는 폭.")]
    [SerializeField] private float pulseAmount = 0.025f;
    [SerializeField] private float pulseSpeed = 3f;

    private readonly HashSet<string> reachable = new HashSet<string>();

    private bool travelling;
    private bool showWholeMap;
    private Text mapPreviewLabel;
    private RectTransform traveller;
    private GameObject journal;
    private Canvas canvas;
    private ScrollRect scroll;
    private RectTransform content;


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

        if (Run != null && Run.IsStoryMap)
        {
            rowSpacing = 225; columnSpacing = 280; nodeSize = 62;

        }
        BuildCanvas();
        BuildMap();
        if (Run != null) Run.LastMessage = null;

        StartCoroutine(FocusOnCurrentRow());

        if (Run != null && Run.HasRun && Run.Data.finished) BuildClearOverlay();
        else StartCoroutine(ShowCurrentStory());
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
        if(scroll!=null) scroll.enabled=!travelling && journal==null && !StoryCutscene.IsOpen;
        if (pulsing.Count == 0) return;
        float s = 1f + Mathf.Sin(Time.unscaledTime * pulseSpeed) * pulseAmount;
        for (int i = 0; i < pulsing.Count; i++)
            if (pulsing[i] != null) pulsing[i].localScale = new Vector3(s, s, 1f);
    }

    // ─────────────────────── 뼈대 ───────────────────────

    private void BuildCanvas()
    {
        canvas = UIFactory.CreateCanvas("MapCanvas", transform, 0);

        if(backgroundSprite != null || Resources.Load<Texture2D>(Run.IsStoryMap?"UI/Wuxia/JourneyWorldMap":"UI/Wuxia/JourneyMapExtended")==null)
            WuxiaBackdrop.Create(canvas.transform, backgroundSprite, true);
        else {
            var paper=UIFactory.CreateImage("MapPaper",canvas.transform,new Color(.9f,.84f,.7f));
            UIFactory.Stretch(paper.rectTransform);
        }
        var wash=UIFactory.CreateImage("MapReadingVeil",canvas.transform,new Color(.96f,.91f,.8f,.06f));
        UIFactory.Stretch(wash.rectTransform);

        // 스크롤 영역 (위/아래 바를 피해서 자리를 잡는다)
        var scrollGO = new GameObject("MapScroll", typeof(RectTransform), Run.IsStoryMap?typeof(WorldMapScroll):typeof(ScrollRect));
        scrollGO.transform.SetParent(canvas.transform, false);
        var scrollRect = (RectTransform)scrollGO.transform;
        UIFactory.Stretch(scrollRect);

        // 뷰포트는 거의 투명하지만 raycastTarget 이 켜져 있어야 드래그 스크롤이 먹는다.
        var viewport = UIFactory.CreateImage("Viewport", scrollGO.transform, new Color(0f, 0f, 0f, 0.002f), true);
        viewport.gameObject.AddComponent<RectMask2D>();
        UIFactory.Stretch(viewport.rectTransform);

        content = UIFactory.CreateRect("Content", viewport.transform);
        content.anchorMin = new Vector2(0f, 0f);
        content.anchorMax = new Vector2(1f, 0f);
        content.pivot     = new Vector2(0.5f, 0f);
        content.anchoredPosition = Vector2.zero;
        if(Run.IsStoryMap){content.anchorMin=content.anchorMax=new Vector2(.5f,0);content.localScale=Vector3.one*.9f;}

        scroll = scrollGO.GetComponent<ScrollRect>();
        scroll.content = content;
        scroll.viewport = viewport.rectTransform;
        scroll.horizontal = Run.IsStoryMap;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40f;
        scroll.inertia = true;

        BuildToolbar();
    }

    private void BuildToolbar()
    {
        // Floating controls leave the full map visible; no opaque header/footer strips.
        var menu=UIFactory.CreateButton("MenuButton",canvas.transform,"메뉴",24,WuxiaTheme.Ink,OnMenu);
        WuxiaTheme.StyleButton(menu);
        UIFactory.Stretch(menu.transform.Find("Label").GetComponent<RectTransform>(),40,0,10,0);
        UIFactory.Place(menu.GetComponent<RectTransform>(),new Vector2(0,1),new Vector2(0,1),new Vector2(30,-28),new Vector2(130,56));
        var notes=UIFactory.CreateButton("ClueJournal",canvas.transform,"단서 기록",24,WuxiaTheme.Ink,ShowJournal);
        WuxiaTheme.StyleButton(notes);
        UIFactory.Stretch(notes.transform.Find("Label").GetComponent<RectTransform>(),40,0,10,0);
        UIFactory.Place(notes.GetComponent<RectTransform>(),Vector2.one,Vector2.one,new Vector2(-30,-28),new Vector2(190,56));
        if(Run.IsStoryMap) {
            var recenter=UIFactory.CreateButton("RecenterMap",canvas.transform,"◎",32,WuxiaTheme.Ink,()=>{if(!travelling&&journal==null)StartCoroutine(FocusOnCurrentRow());});
            UIFactory.Place(recenter.GetComponent<RectTransform>(),Vector2.zero,Vector2.zero,new Vector2(30,28),new Vector2(64,60));
            var minus=UIFactory.CreateButton("ZoomOut",canvas.transform,"−",30,WuxiaTheme.Ink,()=>{if(!travelling&&journal==null)((WorldMapScroll)scroll).SetZoom(content.localScale.x*.8f);});
            UIFactory.Place(minus.GetComponent<RectTransform>(),Vector2.zero,Vector2.zero,new Vector2(110,28),new Vector2(55,60));
            var plus=UIFactory.CreateButton("ZoomIn",canvas.transform,"+",30,WuxiaTheme.Ink,()=>{if(!travelling&&journal==null)((WorldMapScroll)scroll).SetZoom(content.localScale.x*1.25f);});
            UIFactory.Place(plus.GetComponent<RectTransform>(),Vector2.zero,Vector2.zero,new Vector2(173,28),new Vector2(55,60));
            var preview=UIFactory.CreateButton("ToggleMapClouds",canvas.transform,"전체 지도 보기",22,WuxiaTheme.Ink,ToggleMapPreview);
            WuxiaTheme.StyleButton(preview);
            UIFactory.Stretch(preview.transform.Find("Label").GetComponent<RectTransform>(),40,0,10,0);
            UIFactory.Place(preview.GetComponent<RectTransform>(),new Vector2(1,0),new Vector2(1,0),new Vector2(-30,28),new Vector2(235,56));
            mapPreviewLabel=preview.transform.Find("Label").GetComponent<Text>();
        }
    }

    private IEnumerator ShowCurrentStory()
    {
        if (Run == null || !Run.IsStoryMap || Run.CurrentNode == null) yield break;
        travelling=true;
        var node=Run.CurrentNode;
        if(!Run.Data.tutorialCompleted && node.id=="cave") {
            yield return StoryCutscene.Play(Run,"cave_prologue");
            travelling=false;yield break;
        }
        // Battles reveal their outcome only after victory. Ordinary places play on first arrival.
        yield return StoryCutscene.Play(Run,node.id+(node.IsBattle?"_after":"_visit"));
        travelling=false;
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
        content.sizeDelta = Run.IsStoryMap?StoryMapLayout.Size:new Vector2(0f, height);

        // Artwork belongs to the same scroll content as routes; no repetition or screen-fixed painting.
        var mapTexture=Resources.Load<Texture2D>(Run.IsStoryMap?"UI/Wuxia/JourneyWorldMap":"UI/Wuxia/JourneyMapExtended");
        if(backgroundSprite==null && mapTexture!=null) {
            var artwork=UIFactory.CreateRect("ExtendedMapArtwork",content);
            UIFactory.Stretch(artwork);
            var image=artwork.gameObject.AddComponent<RawImage>();image.texture=mapTexture;image.raycastTarget=false;
            var aspect=artwork.gameObject.AddComponent<AspectRatioFitter>();
            aspect.aspectMode=AspectRatioFitter.AspectMode.EnvelopeParent;
            aspect.aspectRatio=(float)mapTexture.width/mapTexture.height;
            if (Run.IsStoryMap) StoryMapWater.Create(artwork, mapTexture);
        }
        if(!showWholeMap) {
            var clouds=UIFactory.CreateRect("DriftingMapClouds",content);UIFactory.Stretch(clouds);
            var mist=clouds.gameObject.AddComponent<StoryMapFog>();mist.ambientOnly=true;mist.driftSpeed=26;mist.raycastTarget=false;
        }
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
            if (!showWholeMap && !Run.IsDiscovered(node.id)) continue;
            bool fromCleared = node.status == NodeStatus.Cleared;
            foreach (var nextId in node.next)
            {
                Vector2 to;
                if ((!showWholeMap && !Run.IsDiscovered(nextId)) || !nodePositions.TryGetValue(nextId, out to)) continue;
                var target = map.Find(nextId);
                int visitedIndex = Run.Data.clearedNodeIds.IndexOf(node.id);
                bool travelled = fromCleared && target != null && visitedIndex >= 0
                    && visitedIndex + 1 < Run.Data.clearedNodeIds.Count
                    && Run.Data.clearedNodeIds[visitedIndex + 1] == nextId;
                bool ahead = (reachable.Contains(node.id) || node.id == Run.Data.currentNodeId) && reachable.Contains(nextId);
                if (Run.IsStoryMap)
                {
                    bool adjacent = node.id == Run.Data.currentNodeId || nextId == Run.Data.currentNodeId;
                    DrawLine(edges,nodePositions[node.id],to,adjacent && !(fromCleared && target != null && target.status==NodeStatus.Cleared) ? new Color(.95f,.72f,.34f,1f)
                        : new Color(.35f,.23f,.12f,.72f));
                    continue;
                }
                DrawLine(edges, nodePositions[node.id], to, travelled ? new Color(.62f,.2f,.13f,.8f)
                    : (ahead ? new Color(.18f,.15f,.10f,.85f) : new Color(.25f,.22f,.17f,.12f)));
            }
        }

        if (Run.IsStoryMap && !showWholeMap)
        {
            var fogRect = UIFactory.CreateRect("ExplorationClouds",content);
            UIFactory.Stretch(fogRect);
            fogRect.pivot = new Vector2(.5f,0);
            var fog = fogRect.gameObject.AddComponent<StoryMapFog>();
            fog.raycastTarget = false;
            fog.Configure(Run.Data, nodePositions);
            // Keep markers above the veil; hidden destinations have no UI objects or click targets.
            nodes.SetAsLastSibling();
        }
        pulsing.Clear();
        foreach (var node in map.nodes)
            if (showWholeMap || Run.IsDiscovered(node.id)) DrawNode(nodes, node);
        if (Run.IsStoryMap && Run.CurrentNode != null)
        {
            var marker = WuxiaTheme.Ornament("Traveler",nodes,WuxiaGraphic.Shape.Traveler,WuxiaTheme.Vermilion);
            traveller = marker.rectTransform;
            UIFactory.Place(traveller,new Vector2(.5f,0),new Vector2(.5f,.5f),
                nodePositions[Run.CurrentNode.id]+new Vector2(0,55),new Vector2(36,58));

        }
    }

    private Vector2 NodePosition(MapNode node, int columns)
    {
        if(Run.IsStoryMap && StoryMapLayout.TryPosition(node.id,out var point))return point;
        float x = (node.column - (columns - 1) * 0.5f) * columnSpacing;
        float y = bottomPadding + node.row * rowSpacing;
        return new Vector2(x, y);
    }

    private void DrawLine(RectTransform parent, Vector2 from, Vector2 to, Color color)
    {
        float length=Vector2.Distance(from,to);int samples=Mathf.Max(2,Mathf.CeilToInt(length/15));
        for(int i=1;i<samples;i++) {
            float t=(float)i/samples;Vector2 point=Run.IsStoryMap?StoryMapLayout.Point(from,to,t):Vector2.Lerp(from,to,t);
            if(Vector2.Distance(point,from)<nodeSize*.5f+12||Vector2.Distance(point,to)<nodeSize*.5f+12)continue;
            var dot=WuxiaTheme.Ornament("RouteDash",parent,WuxiaGraphic.Shape.Disc,color);
            UIFactory.Place(dot.rectTransform,new Vector2(.5f,0),new Vector2(.5f,.5f),point,new Vector2(lineThickness*2,lineThickness*2));
        }
    }

    private void DrawNode(RectTransform parent, MapNode node)
    {
        bool available=node.status==NodeStatus.Available;
        bool cleared=node.status==NodeStatus.Cleared;
        bool current=Run.Data.currentNodeId==node.id;
        bool ahead=reachable.Contains(node.id);
        bool connected = Run.IsStoryMap && !current && Run.FindTravelPath(node.id) != null;
        bool selectable = available || (cleared && connected);
        Color ink=node.type==NodeType.Rest?new Color(.08f,.25f,.15f):node.type==NodeType.Boss?new Color(.60f,.10f,.05f):new Color(.08f,.06f,.04f);
        if(!ahead&&!cleared)ink=new Color(.52f,.48f,.4f,.65f);
        var button=UIFactory.CreateButton("Node_"+node.id,parent,"",20,Color.clear,null);
        var rect=button.GetComponent<RectTransform>();
        UIFactory.Place(rect,new Vector2(.5f,0),new Vector2(.5f,.5f),nodePositions[node.id],new Vector2(nodeSize,nodeSize));
        var disk=WuxiaTheme.Ornament("SealPaper",rect,WuxiaGraphic.Shape.Disc,new Color(1f,.95f,.82f,1f));
        UIFactory.Stretch(disk.rectTransform);
        var ring=WuxiaTheme.Ornament("SealRing",rect,WuxiaGraphic.Shape.Ring,selectable?new Color(.9f,.65f,.27f):ink);
        UIFactory.Stretch(ring.rectTransform);
        if(selectable)
        {
            var outer=WuxiaTheme.Ornament("ReachableRing",rect,WuxiaGraphic.Shape.Ring,new Color(.98f,.78f,.38f));
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
            var shape=node.type==NodeType.Rest?WuxiaGraphic.Shape.Pavilion:node.type==NodeType.Boss?WuxiaGraphic.Shape.Fortress:node.type==NodeType.Event?WuxiaGraphic.Shape.Guide:WuxiaGraphic.Shape.Swords;
            var glyph=WuxiaTheme.Ornament("NodeSymbol",rect,shape,ink);UIFactory.Stretch(glyph.rectTransform,17,17,17,17);
        }
        var name=!string.IsNullOrEmpty(node.title)?node.title:stage!=null?stage.displayName:TypeLabel(node.type);
        // An opaque paper strip keeps crossing routes from running through the caption.
        var caption=UIFactory.CreateImage("CaptionPaper",rect,new Color(.99f,.94f,.83f,1f));
        UIFactory.Place(caption.rectTransform,new Vector2(.5f,0),new Vector2(.5f,1),new Vector2(0,-12),new Vector2(220,40));
        var label=WuxiaTheme.Text("Caption",caption.transform,name,22,TextAnchor.MiddleCenter,ink);
        UIFactory.Stretch(label.rectTransform,3,0,3,0);label.horizontalOverflow=HorizontalWrapMode.Wrap;
        label.resizeTextForBestFit=true;label.resizeTextMinSize=18;label.resizeTextMaxSize=22;label.fontStyle=FontStyle.Bold;
        if(cleared)
        {
            var stamp=UIFactory.CreateImage("ClearedStamp",rect,WuxiaTheme.Vermilion);
            UIFactory.Place(stamp.rectTransform,new Vector2(1,1),new Vector2(.5f,.5f),new Vector2(-3,-4),new Vector2(28,22));
            stamp.rectTransform.localRotation=Quaternion.Euler(0,0,-12);
            var done=WuxiaTheme.Text("StampText",stamp.transform,"✓",15,TextAnchor.MiddleCenter,WuxiaTheme.Paper);UIFactory.Stretch(done.rectTransform);
        }
        if(current && !Run.IsStoryMap)
        {
            var tag=WuxiaTheme.Text("CurrentPosition",rect,"▼ 현재 위치",18,TextAnchor.MiddleCenter,WuxiaTheme.Vermilion);
            UIFactory.Place(tag.rectTransform,new Vector2(.5f,1),new Vector2(.5f,0),new Vector2(0,12),new Vector2(180,30));
        }
        button.targetGraphic=disk;
        var colors=button.colors;colors.highlightedColor=new Color(1.07f,.97f,.8f);colors.selectedColor=colors.highlightedColor;colors.disabledColor=Color.white;button.colors=colors;
        bool canTravel = available || (Run.IsStoryMap && cleared && !current && Run.FindTravelPath(node.id) != null);
        button.interactable = canTravel;
        if(canTravel) button.onClick.AddListener(()=>OnNodeClicked(node));
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

    /// <summary>지금 서 있는 층이 화면 가운데에 오도록 스크롤을 맞춘다.</summary>
    private IEnumerator FocusOnCurrentRow()
    {
        yield return null;                       // 캔버스 레이아웃이 한 번 돌 때까지 기다린다
        if (scroll == null || content == null) yield break;

        if(Run.IsStoryMap && Run.CurrentNode!=null){((WorldMapScroll)scroll).Focus(nodePositions[Run.CurrentNode.id]);yield break;}
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
        if (Run == null || travelling || journal != null) return;
        if(Run.IsStoryMap && node.id=="cave" && !Run.Data.tutorialCompleted){StartCoroutine(EnterCave());return;}
        if (Run.IsStoryMap) { StartCoroutine(Travel(node)); return; }
        Run.EnterNode(node);
        if (node.IsBattle) return;
        Rebuild(); StartCoroutine(FocusOnCurrentRow());
    }

    private IEnumerator EnterCave() {
        travelling=true;
        yield return StoryCutscene.Play(Run,"cave_enter");
        yield return CaveTutorial.Play(Run);
        if(Run.Data.tutorialCompleted)yield return StoryCutscene.Play(Run,"cave_visit");
        Rebuild();travelling=false;StartCoroutine(FocusOnCurrentRow());
    }

    private IEnumerator Travel(MapNode destination)
    {
        var path = Run.FindTravelPath(destination.id);
        if (path == null) yield break;
        travelling = true;
        bool cleared = destination.status == NodeStatus.Cleared;
        for (int i=1;i<path.Count;i++)
        {
            Vector2 from = nodePositions[path[i-1]]+new Vector2(0,55);
            Vector2 to = nodePositions[path[i]]+new Vector2(0,55);
            scroll.StopMovement();
            float duration=Mathf.Clamp(Vector2.Distance(from,to)/500f,.65f,1.6f);
            for (float elapsed=0;elapsed<duration;elapsed+=Time.unscaledDeltaTime)
            {
                if (traveller != null) traveller.anchoredPosition = StoryMapLayout.Point(from-new Vector2(0,55),to-new Vector2(0,55),Mathf.SmoothStep(0,1,elapsed/duration))+new Vector2(0,55);
                ((WorldMapScroll)scroll).Focus(traveller!=null?traveller.anchoredPosition:to);
                yield return null;
            }
            // Commit only safe steps. Reloading mid-travel cannot skip an unresolved encounter.
            if (Run.Data.clearedNodeIds.Contains(path[i])) Run.MoveAlongClearedPath(path[i]);
        }
        if (!cleared && destination.IsBattle)
            yield return StoryCutscene.Play(Run,destination.id+"_visit");
        Run.EnterNode(destination);
        if (!cleared && destination.IsBattle) yield break;
        Rebuild(); StartCoroutine(FocusOnCurrentRow());
        if (!cleared) yield return StoryCutscene.Play(Run,destination.id+"_visit");
        travelling = false;
    }

    private void ToggleMapPreview()
    {
        if (travelling || journal != null) return;
        showWholeMap = !showWholeMap;
        if (mapPreviewLabel != null) mapPreviewLabel.text = showWholeMap ? "구름 다시 켜기" : "전체 지도 보기";
        Vector2 position = scroll.normalizedPosition;
        Rebuild();
        Canvas.ForceUpdateCanvases();
        scroll.normalizedPosition = position;
    }

    private void ShowJournal()
    {
        if (travelling || journal != null) return;
        var shade=UIFactory.CreateImage("ClueJournalOverlay",canvas.transform,new Color(0,0,0,.72f),true);
        UIFactory.Stretch(shade.rectTransform);journal=shade.gameObject;
        var paper=UIFactory.CreateImage("JournalPaper",shade.transform,new Color(.96f,.91f,.81f),true);
        UIFactory.Stretch(paper.rectTransform,100,70,100,70);
        var title=WuxiaTheme.Text("JournalTitle",paper.transform,"여정의 기록",36,TextAnchor.MiddleLeft,WuxiaTheme.Ink);
        UIFactory.Place(title.rectTransform,new Vector2(0,1),new Vector2(0,1),new Vector2(36,-22),new Vector2(650,60));
        var close=UIFactory.CreateButton("CloseJournal",paper.transform,"닫기",24,WuxiaTheme.Ink,()=>{Destroy(journal);journal=null;});
        UIFactory.Place(close.GetComponent<RectTransform>(),Vector2.one,Vector2.one,new Vector2(-30,-25),new Vector2(120,52));
        var listView=JournalScroll("LocationList",paper.transform,26,26,0,100,out var listBody);
        listView.GetComponent<RectTransform>().anchorMax=new Vector2(0,1);
        listView.GetComponent<RectTransform>().offsetMax=new Vector2(350,-100);
        var divider=UIFactory.CreateImage("JournalDivider",paper.transform,new Color(.53f,.40f,.23f,.35f));
        divider.rectTransform.anchorMin=new Vector2(0,0);divider.rectTransform.anchorMax=new Vector2(0,1);
        divider.rectTransform.offsetMin=new Vector2(375,30);divider.rectTransform.offsetMax=new Vector2(377,-100);
        var detailView=JournalScroll("EntryDetail",paper.transform,410,32,36,100,out var detailBody);
        var entries=new List<MapNode>();
        foreach(var n in Run.Data.map.nodes)
            if(Run.Data.clearedNodeIds.Contains(n.id)||Run.HasSeenStory(n.id+"_visit")||Run.HasSeenStory(n.id+"_after")||Run.HasSeenStory(n.id+"_enter")||Run.HasSeenStory(n.id+"_prologue"))entries.Add(n);
        var buttons=new List<Button>();
        System.Action<int> select = index => {
            foreach(Transform child in detailBody) {child.gameObject.SetActive(false);Destroy(child.gameObject);}
            for(int j=0;j<buttons.Count;j++) {
                buttons[j].GetComponent<Image>().color=j==index?WuxiaTheme.Ink:new Color(.85f,.78f,.65f,.45f);
                buttons[j].GetComponentInChildren<Text>().color=j==index?WuxiaTheme.Paper:WuxiaTheme.Ink;
            }
            var n=entries[index];float y=0;
            AddJournalText(detailBody,"EntryTitle",n.title,34,WuxiaTheme.Ink,ref y,20);
            AddJournalText(detailBody,"SummaryHeading","단서 요약",23,WuxiaTheme.Vermilion,ref y,12);
            AddJournalText(detailBody,"Clue_"+n.id,Run.Data.clearedNodeIds.Contains(n.id)?n.story:(n.id=="cave"?"사부의 지시를 따라 동굴 벽화 수련을 준비한다.":"아직 전투를 마치지 않았다. 도착 당시의 대화만 기록되어 있다."),25,WuxiaTheme.Ink,ref y,30);
            AddJournalText(detailBody,"DialogueHeading","대화 기록",23,WuxiaTheme.Vermilion,ref y,20);
            foreach(var phase in new[]{"_prologue","_enter","_visit","_after"}) {
                if(!Run.HasSeenStory(n.id+phase))continue;
                var sequence=StoryCutscene.Load(n.id+phase);if(sequence==null||sequence.lines==null)continue;
                AddJournalText(detailBody,"SceneHeading",phase=="_prologue"?"사라진 사부":phase=="_enter"?"동굴 입장":phase=="_visit"?"첫 방문":"전투 이후",20,new Color(.43f,.38f,.29f),ref y,16);
                foreach(var line in sequence.lines) {
                    if(!string.IsNullOrEmpty(line.speaker))AddJournalText(detailBody,"Speaker",line.speaker,21,WuxiaTheme.Jade,ref y,6);
                    AddJournalText(detailBody,"Dialogue",line.text,25,WuxiaTheme.Ink,ref y,24);
                }
            }
            detailBody.sizeDelta=new Vector2(0,y+20);detailView.verticalNormalizedPosition=1;
        };
        for(int i=0;i<entries.Count;i++) {
            int selected=i;
            var button=UIFactory.CreateButton("JournalLocation_"+entries[i].id,listBody,(i+1).ToString("00")+"  "+entries[i].title,23,new Color(.85f,.78f,.65f,.45f),()=>select(selected));
            var rect=button.GetComponent<RectTransform>();rect.anchorMin=new Vector2(0,1);rect.anchorMax=Vector2.one;rect.pivot=new Vector2(.5f,1);rect.anchoredPosition=new Vector2(0,-i*86);rect.sizeDelta=new Vector2(0,74);
            var text=button.GetComponentInChildren<Text>();text.font=WuxiaTheme.Font;text.alignment=TextAnchor.MiddleLeft;text.horizontalOverflow=HorizontalWrapMode.Wrap;
            UIFactory.Stretch(text.rectTransform,14,6,12,6);buttons.Add(button);
        }
        listBody.sizeDelta=new Vector2(0,entries.Count*86+10);
        if(entries.Count>0){int current=entries.FindIndex(n=>n.id==Run.Data.currentNodeId);select(current>=0?current:0);}
    }

    private ScrollRect JournalScroll(string name,Transform parent,float left,float bottom,float right,float top,out RectTransform body)
    {
        var view=UIFactory.CreateImage(name,parent,Color.clear,true);UIFactory.Stretch(view.rectTransform,left,bottom,right,top);
        view.gameObject.AddComponent<RectMask2D>();var scroller=view.gameObject.AddComponent<ScrollRect>();
        body=UIFactory.CreateRect("Content",view.transform);body.anchorMin=new Vector2(0,1);body.anchorMax=Vector2.one;body.pivot=new Vector2(.5f,1);
        scroller.content=body;scroller.viewport=view.rectTransform;scroller.horizontal=false;scroller.movementType=ScrollRect.MovementType.Clamped;scroller.scrollSensitivity=45;
        return scroller;
    }
    private void AddJournalText(RectTransform parent,string name,string value,int size,Color color,ref float y,float gap)
    {
        var text=WuxiaTheme.Text(name,parent,value,size,TextAnchor.UpperLeft,color);text.horizontalOverflow=HorizontalWrapMode.Wrap;text.supportRichText=false;
        text.rectTransform.anchorMin=new Vector2(0,1);text.rectTransform.anchorMax=Vector2.one;text.rectTransform.pivot=new Vector2(.5f,1);
        text.rectTransform.anchoredPosition=new Vector2(0,-y);text.rectTransform.sizeDelta=new Vector2(0,60);
        Canvas.ForceUpdateCanvases();float height=Mathf.Max(size+8,text.preferredHeight+4);
        text.rectTransform.sizeDelta=new Vector2(0,height);y+=height+gap;
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
        if (Run != null) Run.LastMessage = null;
    }

    private void OnMenu()
    {
        if (travelling || journal != null || StoryCutscene.IsOpen) return;
        if (Run != null) Run.Save();
        SceneRoutes.LoadStart();
    }
}
