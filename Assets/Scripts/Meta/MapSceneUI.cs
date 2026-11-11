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

    // ── 스토리 지도 표시 강화: 흐르는 길, 통통 튀는 화살표, 현재 위치 표식, 정보 카드 ──
    private struct RouteDot { public Graphic graphic; public RectTransform rect; public float t; public Color color; }
    private readonly List<RouteDot> flowing = new List<RouteDot>();
    private readonly List<RectTransform> bouncing = new List<RectTransform>();
    private RectTransform travellerBob;
    private Text progressLabel;
    private GameObject infoCard;
    private RectTransform infoArtHolder;
    private Text infoTitle, infoKind, infoState, infoStory;
    private float TravellerLift { get { return MarkerStyle == 2 ? 0f : MarkerStyle == 1 ? nodeSize * .5f + 70f : nodeSize * .5f + 56f; } }

    /// <summary>전투 지점 → 상대 번호(대화 초상 Enemy01~11). 지점 그림이 없을 때 얼굴을 대신 쓴다.</summary>
    private static readonly Dictionary<string, int> enemyNumber = new Dictionary<string, int>
    {
        {"scout",1},{"river",2},{"ridge",3},{"old_ferry",4},{"gate",5},{"quarry",6},
        {"east",7},{"camp",8},{"rescue",9},{"entrance",10},{"final",11}
    };

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
            rowSpacing = 225; columnSpacing = 280; nodeSize = 92;

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
        if (journal != null && Input.GetKeyDown(KeyCode.Escape)) CloseJournal();
        float now = Time.unscaledTime;
        float s = 1f + Mathf.Sin(now * pulseSpeed) * (Run != null && Run.IsStoryMap ? pulseAmount * 2.4f : pulseAmount);
        for (int i = 0; i < pulsing.Count; i++)
            if (pulsing[i] != null) pulsing[i].localScale = new Vector3(s, s, 1f);

        // 갈 수 있는 길: 현재 위치에서 목적지 쪽으로 빛이 흘러간다.
        for (int i = 0; i < flowing.Count; i++)
        {
            var dot = flowing[i];
            if (dot.graphic == null) continue;
            float wave = Mathf.Max(0f, Mathf.Sin(now * 4.2f - dot.t * 9f));
            wave *= wave;
            var c = dot.color; c.a = Mathf.Lerp(.45f, 1f, wave);
            dot.graphic.color = c;
            float scale = 1f + wave * .55f;
            dot.rect.localScale = new Vector3(scale, scale, 1f);
        }
        // 갈 수 있는 곳 위의 화살표가 위아래로 움직인다.
        float hop = Mathf.Abs(Mathf.Sin(now * 3.1f)) * 9f;
        for (int i = 0; i < bouncing.Count; i++)
            if (bouncing[i] != null) bouncing[i].anchoredPosition = new Vector2(0f, 10f + hop);
        for (int i = 0; i < ripples.Count; i++)
        {
            var wave = ripples[i];
            if (wave.graphic == null) continue;
            float phase = (now * .6f + wave.phase) % 1f, grow = 1f + phase * 1.15f;
            wave.rect.localScale = new Vector3(grow, grow, 1f);
            var tone = wave.color; tone.a = (1f - phase) * .85f; wave.graphic.color = tone;
        }
        if (travellerBob != null) travellerBob.anchoredPosition = new Vector2(0f, Mathf.Sin(now * 2.4f) * 4f);
        if (infoCard != null && infoCard.activeSelf && (travelling || journal != null || StoryCutscene.IsOpen)) infoCard.SetActive(false);
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
            BuildInfoCard();
            if (PreviewStyle == 1) BuildLegend();
        }
    }

    /// <summary>지점에 마우스를 올리면 그 지점 바로 아래에 뜨는 작은 말풍선. 종류와 지금 갈 수 있는지만 알려 준다.</summary>
    private void BuildInfoCard()
    {
        var tip = UIFactory.CreateImage("NodeTooltip", canvas.transform, new Color(.12f, .10f, .08f, .95f));
        tip.rectTransform.anchorMin = tip.rectTransform.anchorMax = new Vector2(.5f, .5f); tip.rectTransform.pivot = new Vector2(.5f, 1f);
        tip.rectTransform.sizeDelta = new Vector2(340, 70);
        infoCard = tip.gameObject;
        var accent = UIFactory.CreateImage("TipAccent", tip.transform, new Color(.90f, .66f, .26f));
        accent.rectTransform.anchorMin = new Vector2(0, 1); accent.rectTransform.anchorMax = Vector2.one;
        accent.rectTransform.offsetMin = new Vector2(0, -3); accent.rectTransform.offsetMax = Vector2.zero;
        infoKind = WuxiaTheme.Text("TipKind", tip.transform, "", 18, TextAnchor.MiddleCenter, new Color(.80f, .74f, .62f));
        UIFactory.Place(infoKind.rectTransform, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -8), new Vector2(330, 24));
        infoState = WuxiaTheme.Text("TipState", tip.transform, "", 21, TextAnchor.MiddleCenter, Color.white);
        UIFactory.Place(infoState.rectTransform, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -34), new Vector2(330, 30));
        infoState.fontStyle = FontStyle.Bold;
        infoCard.SetActive(false);
    }

    private static string KindLabel(NodeType type)
    {
        switch (type)
        {
            case NodeType.Battle: return "전투";
            case NodeType.Elite:  return "강적과의 전투";
            case NodeType.Boss:   return "최종 전투";
            case NodeType.Rest:   return "쉼터 · 체력 회복";
            case NodeType.Event:  return "탐색 · 단서";
        }
        return "";
    }

    private void ShowInfo(MapNode node)
    {
        if (infoCard == null || travelling || journal != null || StoryCutscene.IsOpen) return;
        RectTransform anchor;
        if (!nodeRects.TryGetValue(node.id, out anchor) || anchor == null) return;
        bool cleared = node.status == NodeStatus.Cleared;
        bool current = Run.Data.currentNodeId == node.id;
        bool connected = !current && Run.FindTravelPath(node.id) != null;
        infoKind.text = KindLabel(node.type) + (cleared && node.IsBattle ? "  ·  돌파함" : cleared ? "  ·  다녀옴" : "");
        if (current) { infoState.text = "지금 있는 곳"; infoState.color = new Color(1f, .58f, .46f); }
        else if (!cleared && connected) { infoState.text = node.IsBattle ? "갈 수 있음 · 도착하면 전투" : "갈 수 있음"; infoState.color = new Color(1f, .80f, .36f); }
        else if (cleared && connected) { infoState.text = "지나온 곳 · 다시 갈 수 있음"; infoState.color = new Color(.64f, .88f, .72f); }
        else if (cleared) { infoState.text = "지나온 곳"; infoState.color = new Color(.78f, .74f, .68f); }
        else { infoState.text = "아직 갈 수 없음"; infoState.color = new Color(.74f, .70f, .64f); }
        var tipRect = (RectTransform)infoCard.transform;
        tipRect.position = anchor.TransformPoint(new Vector3(0f, -(anchor.rect.height * .5f + 64f), 0f));
        infoCard.transform.SetAsLastSibling();
        infoCard.SetActive(true);
    }

    private void HideInfo() { if (infoCard != null) infoCard.SetActive(false); }

    private static WuxiaGraphic.Shape GlyphFor(NodeType type)
    {
        return type == NodeType.Rest ? WuxiaGraphic.Shape.Pavilion : type == NodeType.Boss ? WuxiaGraphic.Shape.Fortress
             : type == NodeType.Event ? WuxiaGraphic.Shape.Guide : WuxiaGraphic.Shape.Swords;
    }

    /// <summary>지점 그림을 찾는다. Resources/UI/Wuxia/MapNodes/&lt;지점 id&gt; 가 있으면 그것을, 없으면 전투 상대의 얼굴을 쓴다.</summary>
    private bool TryNodeArt(MapNode node, out Texture texture, out Rect uv)
    {
        uv = new Rect(0, 0, 1, 1);
        texture = Resources.Load<Texture2D>("UI/Wuxia/MapNodes/" + node.id);
        if (texture != null) return true;
        return TryEnemyFace(node, out texture, out uv);
    }

    /// <summary>전투 지점이면 그 상대의 대화 초상에서 얼굴 부분을 찾는다.</summary>
    private bool TryEnemyFace(MapNode node, out Texture texture, out Rect uv)
    {
        uv = new Rect(0, 0, 1, 1);
        int number;
        if (node.IsBattle && enemyNumber.TryGetValue(node.id, out number))
        {
            var actor = Resources.Load<StoryActorDefinition>("Story/Actors/Enemy" + number.ToString("00"));
            if (actor != null && actor.artwork != null)
            {
                Rect crop = actor.crop;
                float cropW = Mathf.Max(1f, crop.width * actor.artwork.width), cropH = Mathf.Max(1f, crop.height * actor.artwork.height);
                float halfY = .17f, halfX = halfY * cropH / cropW;
                Vector2 centre = new Vector2(actor.mouth.x, actor.mouth.y + .075f);
                float x0 = Mathf.Clamp01(centre.x - halfX), x1 = Mathf.Clamp01(centre.x + halfX);
                float y0 = Mathf.Clamp01(centre.y - halfY), y1 = Mathf.Clamp01(centre.y + halfY);
                uv = new Rect(crop.x + x0 * crop.width, crop.y + y0 * crop.height, (x1 - x0) * crop.width, (y1 - y0) * crop.height);
                texture = actor.artwork;
                return true;
            }
        }
        texture = null;
        return false;
    }

    /// <summary>그림을 원 모양으로 잘라 넣는다.</summary>
    private RawImage CircleArt(Transform parent, Texture texture, Rect uv, float inset, Color tint)
    {
        var mask = WuxiaTheme.Ornament("ArtMask", parent, WuxiaGraphic.Shape.Disc, new Color(.90f, .84f, .71f, 1f));
        UIFactory.Stretch(mask.rectTransform, inset, inset, inset, inset);
        mask.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        var rect = UIFactory.CreateRect("Art", mask.transform);
        UIFactory.Stretch(rect);
        var raw = rect.gameObject.AddComponent<RawImage>();
        raw.texture = texture; raw.uvRect = uv; raw.color = tint; raw.raycastTarget = false;
        return raw;
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
        reachable.Clear(); flowing.Clear(); ripples.Clear(); nodeRects.Clear(); travellerBob = null;
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
        glowLayer = null;
        if (Run.IsStoryMap && DimStyle > 0)
        {
            // 지도 그림만 가라앉혀 길과 지점이 도드라지게 한다. 구름과 표식은 그 위에 그대로 그려진다.
            var dim = UIFactory.CreateImage("MapDim", content, new Color(.09f, .06f, .03f, DimStyle == 1 ? .20f : DimStyle == 2 ? .36f : .32f));
            UIFactory.Stretch(dim.rectTransform);
            if (DimStyle == 3) { glowLayer = UIFactory.CreateRect("NodeGlows", content); UIFactory.Stretch(glowLayer); }
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
                    bool fromCurrent = node.id == Run.Data.currentNodeId, toCurrent = nextId == Run.Data.currentNodeId;
                    bool bothCleared = fromCleared && target != null && target.status == NodeStatus.Cleared;
                    if ((fromCurrent || toCurrent) && !bothCleared)
                        // 지금 새로 갈 수 있는 길: 현재 위치에서 바깥으로 금빛이 흐른다.
                        DrawStoryRoute(edges, nodePositions[node.id], to, new Color(.98f, .74f, .26f, 1f), 4.2f, true, toCurrent);
                    else if (bothCleared && (fromCurrent || toCurrent))
                        // 되돌아갈 수 있는 지나온 길.
                        DrawStoryRoute(edges, nodePositions[node.id], to, new Color(.62f, .20f, .13f, .95f), 3.4f, false, false);
                    else if (bothCleared)
                        // 지나온 길: 붉은 먹 자국.
                        DrawStoryRoute(edges, nodePositions[node.id], to, new Color(.55f, .22f, .14f, .78f), 3f, false, false);
                    else
                        // 보이지만 아직 닿지 않는 길.
                        DrawStoryRoute(edges, nodePositions[node.id], to, new Color(.35f, .26f, .16f, .38f), 2.4f, false, false);
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
        pulsing.Clear(); bouncing.Clear();
        foreach (var node in map.nodes)
            if (showWholeMap || Run.IsDiscovered(node.id)) DrawNode(nodes, node);
        if (Run.IsStoryMap && Run.CurrentNode != null)
        {
            if (MarkerStyle == 0) BuildTraveller(nodes, nodePositions[Run.CurrentNode.id]);
            else BuildTravellerAlt(nodes, nodePositions[Run.CurrentNode.id]);
        }
        if (Run.IsStoryMap && progressLabel != null)
        {
            int battles = 0, won = 0, visited = 0;
            foreach (var node in map.nodes)
            {
                bool done = node.status == NodeStatus.Cleared;
                if (done) visited++;
                if (!node.IsBattle) continue;
                battles++; if (done) won++;
            }
            progressLabel.text = "<color=#9E3321>돌파</color>  " + won + " / " + battles + "      <color=#3B5C4A>탐색</color>  " + visited + " / " + map.nodes.Count;
        }
    }

    // ─────────────────────── 현재 위치·갈 길 강조 대안 (미리보기 전용) ───────────────────────
    // 0 이 실제 적용된 모습. 1~3 은 고르기 위한 대안이며 캡처 도구에서만 켠다.
    //  MarkerStyle 1 큰 메달+물결 / 2 지점에 올라선 메달 / 3 깃발+빛
    //  RouteStyle  1 굵은 금빛 토막 / 2 화살촉 행렬 / 3 이어진 금줄
    public static int MarkerStyle = 0;
    // 배경 어둡게 하기 대안 (미리보기 전용): 0 지금 / 1 살짝 / 2 더 어둡게 / 3 어둡게 + 갈 수 있는 곳만 밝힘
    public const int DefaultDimStyle = 3;   // 적용된 값: 어둡게 + 갈 수 있는 곳만 밝힘
    public static int DimStyle = DefaultDimStyle;
    private RectTransform glowLayer;
    public static int RouteStyle = 0;
    private struct Ripple { public RectTransform rect; public Graphic graphic; public float phase; public Color color; }
    private readonly List<Ripple> ripples = new List<Ripple>();
    private readonly Dictionary<string, RectTransform> nodeRects = new Dictionary<string, RectTransform>();

    /// <summary>현재 위치에서 갈 수 있는 곳까지의 길을 또렷하게 그린다. 끝에는 목적지를 가리키는 화살촉을 단다.</summary>
    private void DrawEmphasisRoute(RectTransform parent, Vector2 from, Vector2 to, bool reverse)
    {
        Vector2 start = reverse ? to : from, end = reverse ? from : to;
        float length = Vector2.Distance(from, to);
        float step = RouteStyle == 1 ? 30f : RouteStyle == 2 ? 36f : 7f;
        int samples = Mathf.Max(2, Mathf.CeilToInt(length / step));
        Color gold = new Color(1f, .78f, .24f, 1f), dark = new Color(.24f, .13f, .04f, .92f);
        var points = new List<Vector2>(); var angles = new List<float>(); var along = new List<float>();
        for (int i = 1; i < samples; i++)
        {
            float t = (float)i / samples, tt = reverse ? 1f - t : t;
            Vector2 point = StoryMapLayout.Point(from, to, tt);
            if (Vector2.Distance(point, start) < nodeSize * .5f + 18 || Vector2.Distance(point, end) < nodeSize * .5f + 34) continue;
            Vector2 ahead = StoryMapLayout.Point(from, to, Mathf.Clamp01(tt + (reverse ? -.01f : .01f))), dir = (ahead - point).normalized;
            if (dir.sqrMagnitude < .001f) dir = (end - start).normalized;
            points.Add(point); angles.Add(Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg); along.Add(t * length / 150f);
        }
        if (points.Count == 0) return;
        if (RouteStyle != 2)
        {
            // 짙은 밑줄을 먼저 깔아 어떤 배경 위에서도 금빛이 살아나게 한다.
            for (int i = 0; i < points.Count; i++)
            {
                if (RouteStyle == 1)
                {
                    var under = UIFactory.CreateImage("RouteUnder", parent, dark);
                    UIFactory.Place(under.rectTransform, new Vector2(.5f, 0), new Vector2(.5f, .5f), points[i], new Vector2(27, 13));
                    under.rectTransform.localRotation = Quaternion.Euler(0, 0, angles[i]);
                }
                else
                {
                    var under = WuxiaTheme.Ornament("RouteUnder", parent, WuxiaGraphic.Shape.Disc, dark);
                    UIFactory.Place(under.rectTransform, new Vector2(.5f, 0), new Vector2(.5f, .5f), points[i], new Vector2(14, 14));
                }
            }
        }
        for (int i = 0; i < points.Count; i++)
        {
            Graphic mark;
            if (RouteStyle == 1)
            {
                var bar = UIFactory.CreateImage("RouteBar", parent, gold);
                UIFactory.Place(bar.rectTransform, new Vector2(.5f, 0), new Vector2(.5f, .5f), points[i], new Vector2(21, 7));
                mark = bar;
            }
            else if (RouteStyle == 2)
            {
                var head = WuxiaTheme.Text("RouteChevron", parent, "▶", 27, TextAnchor.MiddleCenter, gold);
                UIFactory.Place(head.rectTransform, new Vector2(.5f, 0), new Vector2(.5f, .5f), points[i], new Vector2(34, 34));
                var edge = head.gameObject.AddComponent<Outline>(); edge.effectColor = dark; edge.effectDistance = new Vector2(1.6f, -1.6f);
                mark = head;
            }
            else
            {
                var bead = WuxiaTheme.Ornament("RouteLine", parent, WuxiaGraphic.Shape.Disc, gold);
                UIFactory.Place(bead.rectTransform, new Vector2(.5f, 0), new Vector2(.5f, .5f), points[i], new Vector2(8, 8));
                mark = bead;
            }
            mark.rectTransform.localRotation = Quaternion.Euler(0, 0, angles[i]);
            flowing.Add(new RouteDot { graphic = mark, rect = mark.rectTransform, t = along[i], color = gold });
        }
        // 목적지 바로 앞의 큰 화살촉
        int last = points.Count - 1;
        var tip = WuxiaTheme.Text("RouteArrow", parent, "▶", 44, TextAnchor.MiddleCenter, new Color(1f, .70f, .14f));
        UIFactory.Place(tip.rectTransform, new Vector2(.5f, 0), new Vector2(.5f, .5f), points[last], new Vector2(56, 56));
        tip.rectTransform.localRotation = Quaternion.Euler(0, 0, angles[last]);
        var tipEdge = tip.gameObject.AddComponent<Outline>(); tipEdge.effectColor = dark; tipEdge.effectDistance = new Vector2(2.2f, -2.2f);
    }

    private void BuildTravellerAlt(RectTransform parent, Vector2 nodePosition)
    {
        int style = MarkerStyle;
        float medalSize = style == 1 ? 96f : style == 2 ? nodeSize + 20f : 84f;
        traveller = UIFactory.CreateRect("Traveler", parent);
        UIFactory.Place(traveller, new Vector2(.5f, 0), new Vector2(.5f, .5f), nodePosition + new Vector2(0, TravellerLift), new Vector2(medalSize, medalSize));
        Vector2 ground = new Vector2(0, -TravellerLift);

        if (style == 1)
        {
            // 발밑에서 퍼져 나가는 붉은 물결
            for (int i = 0; i < 3; i++)
            {
                var wave = WuxiaTheme.Ornament("Ripple", traveller, WuxiaGraphic.Shape.Ring, WuxiaTheme.Vermilion);
                UIFactory.Place(wave.rectTransform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), ground, new Vector2(nodeSize + 18, nodeSize + 18));
                ripples.Add(new Ripple { rect = wave.rectTransform, graphic = wave, phase = i / 3f, color = WuxiaTheme.Vermilion });
            }
        }
        else if (style == 3)
        {
            // 지점을 비추는 금빛
            var light = WuxiaTheme.Ornament("Spotlight", traveller, WuxiaGraphic.Shape.Mist, new Color(1f, .82f, .32f, .42f));
            UIFactory.Place(light.rectTransform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), ground, new Vector2(nodeSize * 3.4f, nodeSize * 3.4f));
            pulsing.Add(light.rectTransform);
        }

        travellerBob = UIFactory.CreateRect("Bob", traveller);
        UIFactory.Place(travellerBob, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(medalSize, medalSize));
        if (style != 2)
        {
            var tail = WuxiaTheme.Text("Pointer", travellerBob, "▼", style == 1 ? 34 : 28, TextAnchor.MiddleCenter, WuxiaTheme.Vermilion);
            UIFactory.Place(tail.rectTransform, new Vector2(.5f, 0), new Vector2(.5f, .5f), new Vector2(0, -8), new Vector2(46, 34));
        }
        var rim = WuxiaTheme.Ornament("MedalRim", travellerBob, WuxiaGraphic.Shape.Disc, WuxiaTheme.Vermilion);
        UIFactory.Stretch(rim.rectTransform);
        var goldRim = WuxiaTheme.Ornament("MedalGold", travellerBob, WuxiaGraphic.Shape.Disc, new Color(.98f, .82f, .40f));
        float band = style == 2 ? 6f : 4f; UIFactory.Stretch(goldRim.rectTransform, band, band, band, band);
        var face = Resources.Load<Texture2D>("UI/Wuxia/MapHero");
        if (face != null) CircleArt(travellerBob, face, new Rect(0, 0, 1, 1), band + 4f, Color.white);

        if (style == 3)
        {
            // 옆에 세운 깃발
            var pole = UIFactory.CreateImage("FlagPole", travellerBob, new Color(.20f, .13f, .07f));
            UIFactory.Place(pole.rectTransform, new Vector2(1, 0), new Vector2(.5f, 0), new Vector2(8, -6), new Vector2(5, medalSize + 44));
            var flag = UIFactory.CreateImage("Flag", travellerBob, WuxiaTheme.Vermilion);
            UIFactory.Place(flag.rectTransform, new Vector2(1, 1), new Vector2(0, 1), new Vector2(10, 38), new Vector2(92, 34));
            var word = WuxiaTheme.Text("FlagText", flag.transform, "현재 위치", 18, TextAnchor.MiddleCenter, new Color(1f, .95f, .84f));
            UIFactory.Stretch(word.rectTransform); word.fontStyle = FontStyle.Bold;
        }
        else
        {
            var plate = UIFactory.CreateImage("CurrentPlate", travellerBob, WuxiaTheme.Vermilion);
            UIFactory.Place(plate.rectTransform, new Vector2(.5f, 1), new Vector2(.5f, 0), new Vector2(0, 4), new Vector2(style == 1 ? 124 : 112, style == 1 ? 32 : 28));
            var label = WuxiaTheme.Text("CurrentText", plate.transform, "현재 위치", style == 1 ? 21 : 18, TextAnchor.MiddleCenter, new Color(1f, .95f, .84f));
            UIFactory.Stretch(label.rectTransform); label.fontStyle = FontStyle.Bold;
        }
    }

    /// <summary>현재 위치 표식: 주인공 얼굴 메달과 "현재 위치" 팻말. 이동할 때 이 표식이 길을 따라 움직인다.</summary>
    private void BuildTraveller(RectTransform parent, Vector2 nodePosition)
    {
        traveller = UIFactory.CreateRect("Traveler", parent);
        UIFactory.Place(traveller, new Vector2(.5f, 0), new Vector2(.5f, .5f), nodePosition + new Vector2(0, TravellerLift), new Vector2(72, 92));
        travellerBob = UIFactory.CreateRect("Bob", traveller);
        UIFactory.Place(travellerBob, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(72, 92));

        // 아래로 뾰족한 꼬리: 표식이 가리키는 지점.
        var tail = WuxiaTheme.Text("Pointer", travellerBob, "▼", 24, TextAnchor.MiddleCenter, WuxiaTheme.Vermilion);
        UIFactory.Place(tail.rectTransform, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, -4), new Vector2(40, 26));
        var medal = UIFactory.CreateRect("Medal", travellerBob);
        UIFactory.Place(medal, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -2), new Vector2(68, 68));
        var rim = WuxiaTheme.Ornament("MedalRim", medal, WuxiaGraphic.Shape.Disc, WuxiaTheme.Vermilion);
        UIFactory.Stretch(rim.rectTransform);
        var gold = WuxiaTheme.Ornament("MedalGold", medal, WuxiaGraphic.Shape.Disc, new Color(.96f, .80f, .42f));
        UIFactory.Stretch(gold.rectTransform, 3, 3, 3, 3);
        var face = Resources.Load<Texture2D>("UI/Wuxia/MapHero");
        if (face != null) CircleArt(medal, face, new Rect(0, 0, 1, 1), 6, Color.white);
        else
        {
            var paper = WuxiaTheme.Ornament("MedalPaper", medal, WuxiaGraphic.Shape.Disc, new Color(1f, .95f, .82f));
            UIFactory.Stretch(paper.rectTransform, 6, 6, 6, 6);
            var figure = WuxiaTheme.Ornament("MedalFigure", medal, WuxiaGraphic.Shape.Traveler, WuxiaTheme.Vermilion);
            UIFactory.Stretch(figure.rectTransform, 14, 10, 14, 10);
        }
        var plate = UIFactory.CreateImage("CurrentPlate", travellerBob, WuxiaTheme.Vermilion);
        UIFactory.Place(plate.rectTransform, new Vector2(.5f, 1), new Vector2(.5f, 0), new Vector2(0, 2), new Vector2(104, 26));
        var label = WuxiaTheme.Text("CurrentText", plate.transform, "현재 위치", 17, TextAnchor.MiddleCenter, new Color(1f, .95f, .84f));
        UIFactory.Stretch(label.rectTransform); label.fontStyle = FontStyle.Bold;
    }

    /// <summary>스토리 지도의 길. flow 가 켜지면 점들이 현재 위치에서 목적지 쪽으로 빛난다.</summary>
    private void DrawStoryRoute(RectTransform parent, Vector2 from, Vector2 to, Color color, float radius, bool flow, bool reverse)
    {
        if (flow && RouteStyle > 0 && PreviewStyle == 0) { DrawEmphasisRoute(parent, from, to, reverse); return; }
        if (DrawAltRoute(parent, from, to, color, radius, flow, reverse)) return;
        float length = Vector2.Distance(from, to); int samples = Mathf.Max(2, Mathf.CeilToInt(length / (flow ? 20f : 15f)));
        for (int i = 1; i < samples; i++)
        {
            float t = (float)i / samples; Vector2 point = StoryMapLayout.Point(from, to, t);
            if (Vector2.Distance(point, from) < nodeSize * .5f + 12 || Vector2.Distance(point, to) < nodeSize * .5f + 12) continue;
            var dot = WuxiaTheme.Ornament("RouteDash", parent, WuxiaGraphic.Shape.Disc, color);
            UIFactory.Place(dot.rectTransform, new Vector2(.5f, 0), new Vector2(.5f, .5f), point, new Vector2(radius * 2, radius * 2));
            if (flow) flowing.Add(new RouteDot { graphic = dot, rect = dot.rectTransform, t = (reverse ? 1f - t : t) * length / 150f, color = color });
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

    /// <summary>
    /// 스토리 지도의 지점 하나. 상태가 한눈에 구분되도록 그린다.
    ///  · 지금 새로 갈 수 있는 곳: 금빛 테두리가 뛰고, 위에 화살표가 통통 튄다.
    ///  · 지나온 곳: 그림이 차분해지고 붉은 도장(전투 勝 / 그 외 完)이 찍힌다.
    ///  · 아직 못 가는 곳: 흐리게 바랜다.
    /// </summary>
    private void DrawStoryNode(RectTransform parent, MapNode node)
    {
        bool available = node.status == NodeStatus.Available;
        bool cleared = node.status == NodeStatus.Cleared;
        bool current = Run.Data.currentNodeId == node.id;
        bool connected = !current && Run.FindTravelPath(node.id) != null;
        bool canTravel = available || (cleared && connected);
        bool fresh = canTravel && !cleared;
        bool locked = !cleared && !canTravel && !current;
        Color typeInk = node.type == NodeType.Rest ? WuxiaTheme.Jade : node.IsBattle ? WuxiaTheme.Vermilion : new Color(.30f, .24f, .16f);
        Color ink = locked ? new Color(.52f, .48f, .40f, .75f) : new Color(.08f, .06f, .04f);
        Color gold = new Color(.98f, .76f, .30f);

        var button = UIFactory.CreateButton("Node_" + node.id, parent, "", 20, Color.clear, null);
        var rect = button.GetComponent<RectTransform>();
        UIFactory.Place(rect, new Vector2(.5f, 0), new Vector2(.5f, .5f), nodePositions[node.id], new Vector2(nodeSize, nodeSize));
        nodeRects[node.id] = rect;
        if (glowLayer != null && (fresh || current))
        {
            var lit = WuxiaTheme.Ornament("NodeGlow", glowLayer, WuxiaGraphic.Shape.Mist, new Color(1f, .94f, .72f, current ? .85f : 1f));
            UIFactory.Place(lit.rectTransform, new Vector2(.5f, 0), new Vector2(.5f, .5f), nodePositions[node.id], new Vector2(nodeSize * 4.2f, nodeSize * 4.2f));
        }

        if (fresh)
        {
            var halo = WuxiaTheme.Ornament("ReachableHalo", rect, WuxiaGraphic.Shape.Mist, new Color(1f, .80f, .30f, .75f));
            UIFactory.Stretch(halo.rectTransform, -26, -26, -26, -26);
            pulsing.Add(halo.rectTransform);
            var outer = WuxiaTheme.Ornament("ReachableRing", rect, WuxiaGraphic.Shape.Ring, gold);
            UIFactory.Stretch(outer.rectTransform, -10, -10, -10, -10);
            pulsing.Add(outer.rectTransform);
        }
        else if (canTravel)
        {
            var back = WuxiaTheme.Ornament("ReturnRing", rect, WuxiaGraphic.Shape.Ring, new Color(.62f, .20f, .13f, .85f));
            UIFactory.Stretch(back.rectTransform, -7, -7, -7, -7);
        }
        if (current)
        {
            var here = WuxiaTheme.Ornament("CurrentRing", rect, WuxiaGraphic.Shape.Ring, WuxiaTheme.Vermilion);
            UIFactory.Stretch(here.rectTransform, -9, -9, -9, -9);
        }

        var disk = WuxiaTheme.Ornament("SealPaper", rect, WuxiaGraphic.Shape.Disc, locked ? new Color(.90f, .86f, .76f, .92f) : new Color(1f, .95f, .82f, 1f));
        UIFactory.Stretch(disk.rectTransform);
        Texture texture; Rect uv;
        if (TryNodeArt(node, out texture, out uv))
            CircleArt(rect, texture, uv, 5, locked ? new Color(.86f, .84f, .80f, .62f) : cleared && !current ? new Color(.92f, .89f, .84f, 1f) : Color.white);
        else
        {
            var glyph = WuxiaTheme.Ornament("NodeSymbol", rect, GlyphFor(node.type), locked ? ink : typeInk);
            UIFactory.Stretch(glyph.rectTransform, 20, 20, 20, 20);
        }
        var ring = WuxiaTheme.Ornament("SealRing", rect, WuxiaGraphic.Shape.Ring, current ? WuxiaTheme.Vermilion : fresh ? gold : ink);
        UIFactory.Stretch(ring.rectTransform);

        // 종류 배지(전투/쉼터/탐색): 그림만으로는 알기 어려운 지점 종류를 왼쪽 아래에 작게 표시한다.
        if (!cleared)
        {
            var badge = UIFactory.CreateRect("KindBadge", rect);
            UIFactory.Place(badge, new Vector2(0, 0), new Vector2(.5f, .5f), new Vector2(9, 9), new Vector2(34, 34));
            var plate = WuxiaTheme.Ornament("BadgePlate", badge, WuxiaGraphic.Shape.Disc, locked ? new Color(.55f, .51f, .44f, .9f) : typeInk);
            UIFactory.Stretch(plate.rectTransform);
            var mark = WuxiaTheme.Ornament("BadgeMark", badge, GlyphFor(node.type), new Color(1f, .95f, .84f));
            UIFactory.Stretch(mark.rectTransform, 6, 6, 6, 6);
        }

        var stage = Run.Config.Get(node.stageId);
        var name = !string.IsNullOrEmpty(node.title) ? node.title : stage != null ? stage.displayName : TypeLabel(node.type);
        var caption = UIFactory.CreateImage("CaptionPaper", rect, locked ? new Color(.95f, .91f, .81f, .85f) : new Color(.99f, .94f, .83f, 1f));
        UIFactory.Place(caption.rectTransform, new Vector2(.5f, 0), new Vector2(.5f, 1), new Vector2(0, -12), new Vector2(220, 40));
        if (fresh || current)
        {
            var underline = UIFactory.CreateImage("CaptionAccent", caption.transform, current ? WuxiaTheme.Vermilion : gold);
            UIFactory.Place(underline.rectTransform, new Vector2(.5f, 0), new Vector2(.5f, 0), Vector2.zero, new Vector2(220, 3));
        }
        var label = WuxiaTheme.Text("Caption", caption.transform, name, 22, TextAnchor.MiddleCenter, ink);
        UIFactory.Stretch(label.rectTransform, 3, 0, 3, 0); label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.resizeTextForBestFit = true; label.resizeTextMinSize = 18; label.resizeTextMaxSize = 22; label.fontStyle = FontStyle.Bold;

        if (cleared)
        {
            // 붉은 인장: 전투는 勝, 그 외 장소는 完.
            var stamp = UIFactory.CreateImage("ClearedStamp", rect, new Color(.66f, .16f, .10f, .96f));
            UIFactory.Place(stamp.rectTransform, new Vector2(1, 1), new Vector2(.5f, .5f), new Vector2(-4, -4), new Vector2(42, 42));
            stamp.rectTransform.localRotation = Quaternion.Euler(0, 0, -10);
            var inner = UIFactory.CreateImage("StampInner", stamp.transform, new Color(1f, .93f, .82f, .9f));
            UIFactory.Stretch(inner.rectTransform, 2, 2, 2, 2);
            var fill = UIFactory.CreateImage("StampFill", stamp.transform, new Color(.66f, .16f, .10f, 1f));
            UIFactory.Stretch(fill.rectTransform, 4, 4, 4, 4);
            var done = WuxiaTheme.Text("StampText", stamp.transform, node.IsBattle ? "勝" : "完", 25, TextAnchor.MiddleCenter, new Color(1f, .94f, .84f));
            UIFactory.Stretch(done.rectTransform); done.fontStyle = FontStyle.Bold;
        }
        if (fresh)
        {
            // 갈 수 있는 곳 위에서 튀는 화살표.
            var arrowHolder = UIFactory.CreateRect("GoArrow", rect);
            UIFactory.Place(arrowHolder, new Vector2(.5f, 1), new Vector2(.5f, 0), new Vector2(0, 10), new Vector2(60, 34));
            var arrow = WuxiaTheme.Text("GoArrowText", arrowHolder, "▼", 30, TextAnchor.MiddleCenter, new Color(.86f, .52f, .08f));
            UIFactory.Stretch(arrow.rectTransform);
            var shadow = arrow.gameObject.AddComponent<Outline>(); shadow.effectColor = new Color(1f, .96f, .85f, .9f); shadow.effectDistance = new Vector2(1.5f, -1.5f);
            bouncing.Add(arrowHolder);
        }

        button.targetGraphic = disk;
        var colors = button.colors; colors.highlightedColor = new Color(1.07f, .97f, .8f); colors.selectedColor = colors.highlightedColor; colors.disabledColor = Color.white; button.colors = colors;
        button.interactable = canTravel;
        if (canTravel) button.onClick.AddListener(() => OnNodeClicked(node));

        // 마우스를 올리면 정보 카드. (EventTrigger 는 드래그·휠까지 가로채 지도 스크롤을 막으므로 쓰지 않는다)
        var hover = button.gameObject.AddComponent<MapNodeHover>();
        hover.Enter = () => ShowInfo(node); hover.Exit = HideInfo;
    }

    // ─────────────────────── 지도 표시 대안 (미리보기 전용) ───────────────────────
    // PreviewStyle 0 이 실제 적용된 모습이다. 1~4 는 고르기 위한 대안이며 캡처 도구에서만 켠다.
    //  1 첨탑식 먹 기호 / 2 족자 그림패 / 3 미니멀 집중 / 4 깃발 핀
    public static int PreviewStyle = 0;

    private void BuildLegend()
    {
        var panel = UIFactory.CreateImage("MapLegend", canvas.transform, new Color(.97f, .92f, .81f, .93f));
        UIFactory.Place(panel.rectTransform, new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(-30, 40), new Vector2(210, 282));
        var head = WuxiaTheme.Text("LegendTitle", panel.transform, "범례", 22, TextAnchor.MiddleCenter, WuxiaTheme.Ink);
        UIFactory.Place(head.rectTransform, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -8), new Vector2(190, 34)); head.fontStyle = FontStyle.Bold;
        var rows = new (NodeType type, string label)[] { (NodeType.Battle, "전투"), (NodeType.Boss, "강적 · 최종"), (NodeType.Rest, "쉼터"), (NodeType.Event, "탐색") };
        for (int i = 0; i < rows.Length; i++)
        {
            var icon = WuxiaTheme.Ornament("LegendIcon", panel.transform, GlyphFor(rows[i].type), new Color(.12f, .09f, .06f));
            UIFactory.Place(icon.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(22, -52 - i * 44), new Vector2(34, 34));
            var label = WuxiaTheme.Text("LegendLabel", panel.transform, rows[i].label, 20, TextAnchor.MiddleLeft, WuxiaTheme.Ink);
            UIFactory.Place(label.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(70, -52 - i * 44), new Vector2(130, 34));
        }
        var ring = WuxiaTheme.Ornament("LegendRing", panel.transform, WuxiaGraphic.Shape.Ring, new Color(.15f, .11f, .08f, .9f));
        UIFactory.Place(ring.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(22, -52 - 4 * 44), new Vector2(34, 34));
        var visited = WuxiaTheme.Text("LegendLabel", panel.transform, "지나온 곳", 20, TextAnchor.MiddleLeft, WuxiaTheme.Ink);
        UIFactory.Place(visited.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(70, -52 - 4 * 44), new Vector2(130, 34));
    }

    /// <summary>대안 1(먹 점선)·2(발자국)의 길. true 를 돌려주면 여기서 다 그린 것이다.</summary>
    private bool DrawAltRoute(RectTransform parent, Vector2 from, Vector2 to, Color color, float radius, bool flow, bool reverse)
    {
        if (PreviewStyle != 1 && PreviewStyle != 2) return false;
        float length = Vector2.Distance(from, to); float step = PreviewStyle == 1 ? 26f : 22f; int samples = Mathf.Max(2, Mathf.CeilToInt(length / step));
        if (PreviewStyle == 1) color = flow ? new Color(.10f, .07f, .04f, 1f) : new Color(color.r * .6f, color.g * .55f, color.b * .5f, Mathf.Max(.45f, color.a));
        for (int i = 1; i < samples; i++)
        {
            float t = (float)i / samples; Vector2 point = StoryMapLayout.Point(from, to, t);
            if (Vector2.Distance(point, from) < nodeSize * .5f + 14 || Vector2.Distance(point, to) < nodeSize * .5f + 14) continue;
            Vector2 ahead = StoryMapLayout.Point(from, to, Mathf.Min(1f, t + .01f)), dir = (ahead - point).normalized;
            if (dir.sqrMagnitude < .001f) dir = (to - from).normalized;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            Graphic mark;
            if (PreviewStyle == 1)
            {
                // 붓으로 그은 짧은 선
                var dash = UIFactory.CreateImage("RouteDash", parent, color);
                UIFactory.Place(dash.rectTransform, new Vector2(.5f, 0), new Vector2(.5f, .5f), point, new Vector2(14, flow ? 5.5f : 4f));
                mark = dash;
            }
            else
            {
                // 좌우로 번갈아 찍힌 발자국
                Vector2 normal = new Vector2(-dir.y, dir.x);
                var foot = WuxiaTheme.Ornament("RouteFoot", parent, WuxiaGraphic.Shape.Disc, color);
                UIFactory.Place(foot.rectTransform, new Vector2(.5f, 0), new Vector2(.5f, .5f), point + normal * (i % 2 == 0 ? 5.5f : -5.5f), new Vector2(radius * 3.2f, radius * 1.9f));
                mark = foot;
            }
            mark.rectTransform.localRotation = Quaternion.Euler(0, 0, angle);
            if (flow) flowing.Add(new RouteDot { graphic = mark, rect = mark.rectTransform, t = (reverse ? 1f - t : t) * length / 150f, color = color });
        }
        return true;
    }

    private void DrawStoryNodeAlt(RectTransform parent, MapNode node, int style)
    {
        bool available = node.status == NodeStatus.Available;
        bool cleared = node.status == NodeStatus.Cleared;
        bool current = Run.Data.currentNodeId == node.id;
        bool connected = !current && Run.FindTravelPath(node.id) != null;
        bool canTravel = available || (cleared && connected);
        bool fresh = canTravel && !cleared;
        bool locked = !cleared && !canTravel && !current;
        Color ink = locked ? new Color(.52f, .48f, .40f, .75f) : new Color(.08f, .06f, .04f);
        Color gold = new Color(.98f, .76f, .30f);
        Color ringColor = current ? WuxiaTheme.Vermilion : fresh ? gold : cleared ? new Color(.62f, .20f, .13f) : new Color(.50f, .45f, .37f, .8f);
        Color artTint = locked ? new Color(.86f, .84f, .80f, .62f) : cleared && !current ? new Color(.92f, .89f, .84f, 1f) : Color.white;
        bool small = style == 3 && !fresh && !current && !(cleared && connected);
        float size = style == 3 ? (small ? 24f : fresh || current ? nodeSize * 1.2f : nodeSize * .8f) : style == 1 ? nodeSize * .86f : nodeSize;

        var button = UIFactory.CreateButton("Node_" + node.id, parent, "", 20, Color.clear, null);
        var rect = button.GetComponent<RectTransform>();
        UIFactory.Place(rect, new Vector2(.5f, 0), new Vector2(.5f, .5f), nodePositions[node.id], new Vector2(size, size));
        nodeRects[node.id] = rect;
        Texture texture; Rect uv; bool hasArt = TryNodeArt(node, out texture, out uv);
        float captionDrop = -12f;

        if (style == 1)
        {
            // 첨탑식: 그림 없이 붓으로 그린 기호. 지나온 곳은 먹으로 동그라미를 친다.
            var wash = WuxiaTheme.Ornament("InkWash", rect, WuxiaGraphic.Shape.Mist, new Color(.99f, .95f, .85f, locked ? .6f : .95f));
            UIFactory.Stretch(wash.rectTransform, -16, -16, -16, -16);
            Color glyphInk = locked ? new Color(.35f, .30f, .24f, .42f) : cleared ? new Color(.24f, .19f, .14f, .92f) : new Color(.08f, .05f, .03f);
            var glyph = WuxiaTheme.Ornament("NodeSymbol", rect, GlyphFor(node.type), glyphInk);
            float pad = node.type == NodeType.Boss ? -6f : 6f; UIFactory.Stretch(glyph.rectTransform, pad, pad, pad, pad);
            if (fresh) pulsing.Add(glyph.rectTransform);
            if (cleared)
            {
                var loop = WuxiaTheme.Ornament("VisitedLoop", rect, WuxiaGraphic.Shape.Ring, new Color(.14f, .10f, .07f, .92f));
                UIFactory.Stretch(loop.rectTransform, -9, -9, -9, -9);
                var loop2 = WuxiaTheme.Ornament("VisitedLoop2", rect, WuxiaGraphic.Shape.Ring, new Color(.14f, .10f, .07f, .7f));
                UIFactory.Stretch(loop2.rectTransform, -12, -6, -6, -11); loop2.rectTransform.localRotation = Quaternion.Euler(0, 0, 14);
            }
            if (current)
            {
                var here = WuxiaTheme.Ornament("CurrentRing", rect, WuxiaGraphic.Shape.Ring, WuxiaTheme.Vermilion);
                UIFactory.Stretch(here.rectTransform, -15, -15, -15, -15);
            }
            if (fresh)
            {
                var hint = WuxiaTheme.Ornament("ReachableRing", rect, WuxiaGraphic.Shape.Ring, new Color(.86f, .52f, .08f, .9f));
                UIFactory.Stretch(hint.rectTransform, -10, -10, -10, -10); pulsing.Add(hint.rectTransform);
            }
        }
        else if (style == 2)
        {
            // 족자 그림패: 네모난 그림을 나무 틀에 끼운 모양. 지나온 곳은 귀퉁이에 붉은 띠.
            if (fresh)
            {
                var halo = WuxiaTheme.Ornament("ReachableHalo", rect, WuxiaGraphic.Shape.Mist, new Color(1f, .80f, .30f, .85f));
                UIFactory.Stretch(halo.rectTransform, -30, -30, -30, -30); pulsing.Add(halo.rectTransform);
            }
            var frame = UIFactory.CreateImage("Frame", rect, current ? WuxiaTheme.Vermilion : fresh ? gold : locked ? new Color(.50f, .44f, .36f, .8f) : new Color(.24f, .17f, .10f));
            UIFactory.Stretch(frame.rectTransform, -6, -6, -6, -6);
            var mat = UIFactory.CreateImage("Mat", rect, new Color(.97f, .92f, .80f));
            UIFactory.Stretch(mat.rectTransform, -2, -2, -2, -2);
            if (hasArt)
            {
                var pic = UIFactory.CreateRect("Art", rect); UIFactory.Stretch(pic, 2, 2, 2, 2);
                var raw = pic.gameObject.AddComponent<RawImage>(); raw.texture = texture; raw.uvRect = uv; raw.color = artTint; raw.raycastTarget = false;
            }
            else
            {
                var glyph = WuxiaTheme.Ornament("NodeSymbol", rect, GlyphFor(node.type), ink); UIFactory.Stretch(glyph.rectTransform, 20, 20, 20, 20);
            }
            if (cleared)
            {
                var ribbon = UIFactory.CreateImage("ClearedRibbon", rect, new Color(.66f, .16f, .10f, .97f));
                UIFactory.Place(ribbon.rectTransform, new Vector2(1, 1), new Vector2(.5f, .5f), new Vector2(-14, -14), new Vector2(84, 22));
                ribbon.rectTransform.localRotation = Quaternion.Euler(0, 0, -38);
                var word = WuxiaTheme.Text("RibbonText", ribbon.transform, node.IsBattle ? "돌파" : "완료", 15, TextAnchor.MiddleCenter, new Color(1f, .94f, .84f));
                UIFactory.Stretch(word.rectTransform); word.fontStyle = FontStyle.Bold;
            }
            if (fresh)
            {
                var tag = UIFactory.CreateRect("GoTag", rect);
                UIFactory.Place(tag, new Vector2(.5f, 1), new Vector2(.5f, 0), new Vector2(0, 10), new Vector2(104, 26));
                var plate = UIFactory.CreateImage("GoTagPlate", tag, new Color(.86f, .56f, .12f)); UIFactory.Stretch(plate.rectTransform);
                var word = WuxiaTheme.Text("GoTagText", tag, node.IsBattle ? "전투 가능" : "갈 수 있음", 16, TextAnchor.MiddleCenter, new Color(1f, .97f, .88f));
                UIFactory.Stretch(word.rectTransform); word.fontStyle = FontStyle.Bold;
                bouncing.Add(tag);
            }
            captionDrop = -16f;
        }
        else if (style == 3)
        {
            // 미니멀 집중: 지금 고를 수 있는 곳만 크게, 나머지는 작은 점으로 줄인다.
            if (small)
            {
                var dot = WuxiaTheme.Ornament("SmallDot", rect, WuxiaGraphic.Shape.Disc, cleared ? new Color(.66f, .16f, .10f) : new Color(.36f, .31f, .24f, locked ? .6f : 1f));
                UIFactory.Stretch(dot.rectTransform);
                var rim = WuxiaTheme.Ornament("SmallRim", rect, WuxiaGraphic.Shape.Ring, new Color(1f, .95f, .84f, .9f));
                UIFactory.Stretch(rim.rectTransform, -3, -3, -3, -3);
                if (cleared)
                {
                    var tick = WuxiaTheme.Text("SmallTick", rect, "✓", 15, TextAnchor.MiddleCenter, new Color(1f, .95f, .84f));
                    UIFactory.Stretch(tick.rectTransform); tick.fontStyle = FontStyle.Bold;
                }
            }
            else
            {
                if (fresh)
                {
                    var outer = WuxiaTheme.Ornament("ReachableRing", rect, WuxiaGraphic.Shape.Ring, gold);
                    UIFactory.Stretch(outer.rectTransform, -12, -12, -12, -12); pulsing.Add(outer.rectTransform);
                }
                var disk = WuxiaTheme.Ornament("SealPaper", rect, WuxiaGraphic.Shape.Disc, new Color(1f, .95f, .82f)); UIFactory.Stretch(disk.rectTransform);
                if (hasArt) CircleArt(rect, texture, uv, 5, artTint);
                var ring = WuxiaTheme.Ornament("SealRing", rect, WuxiaGraphic.Shape.Ring, ringColor); UIFactory.Stretch(ring.rectTransform, -2, -2, -2, -2);
                if (fresh)
                {
                    var arrowHolder = UIFactory.CreateRect("GoArrow", rect);
                    UIFactory.Place(arrowHolder, new Vector2(.5f, 1), new Vector2(.5f, 0), new Vector2(0, 10), new Vector2(60, 34));
                    var arrow = WuxiaTheme.Text("GoArrowText", arrowHolder, "▼", 32, TextAnchor.MiddleCenter, new Color(.86f, .52f, .08f));
                    UIFactory.Stretch(arrow.rectTransform); bouncing.Add(arrowHolder);
                }
            }
            captionDrop = small ? -6f : -14f;
        }
        else
        {
            // 깃발 핀: 지도에 꽂힌 핀. 지나온 곳에는 붉은 깃발이 꽂힌다.
            var shadow = WuxiaTheme.Ornament("PinShadow", rect, WuxiaGraphic.Shape.Disc, new Color(0f, 0f, 0f, .30f));
            UIFactory.Place(shadow.rectTransform, new Vector2(.5f, 0), new Vector2(.5f, .5f), new Vector2(0, -8), new Vector2(size * .55f, 12));
            var body = UIFactory.CreateRect("PinBody", rect);
            UIFactory.Place(body, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, 10), new Vector2(size, size));
            if (fresh) bouncing.Add(body);
            var tail = WuxiaTheme.Text("PinTail", body, "▼", 34, TextAnchor.MiddleCenter, ringColor);
            UIFactory.Place(tail.rectTransform, new Vector2(.5f, 0), new Vector2(.5f, .5f), new Vector2(0, -5), new Vector2(44, 34));
            var rim = WuxiaTheme.Ornament("PinRim", body, WuxiaGraphic.Shape.Disc, ringColor); UIFactory.Stretch(rim.rectTransform, -4, -4, -4, -4);
            var paper = WuxiaTheme.Ornament("PinPaper", body, WuxiaGraphic.Shape.Disc, new Color(1f, .95f, .82f)); UIFactory.Stretch(paper.rectTransform);
            if (hasArt) CircleArt(body, texture, uv, 3, artTint);
            else { var glyph = WuxiaTheme.Ornament("NodeSymbol", body, GlyphFor(node.type), ink); UIFactory.Stretch(glyph.rectTransform, 20, 20, 20, 20); }
            if (cleared)
            {
                var pole = UIFactory.CreateImage("FlagPole", body, new Color(.20f, .14f, .08f));
                UIFactory.Place(pole.rectTransform, new Vector2(1, 1), new Vector2(.5f, 0), new Vector2(-12, -20), new Vector2(4, 46));
                var flag = UIFactory.CreateImage("Flag", body, new Color(.72f, .16f, .10f));
                UIFactory.Place(flag.rectTransform, new Vector2(1, 1), new Vector2(0, 1), new Vector2(-10, 26), new Vector2(30, 20));
                var word = WuxiaTheme.Text("FlagText", flag.transform, node.IsBattle ? "勝" : "完", 15, TextAnchor.MiddleCenter, new Color(1f, .94f, .84f));
                UIFactory.Stretch(word.rectTransform); word.fontStyle = FontStyle.Bold;
            }
            captionDrop = -20f;
        }

        var stage = Run.Config.Get(node.stageId);
        var name = !string.IsNullOrEmpty(node.title) ? node.title : stage != null ? stage.displayName : TypeLabel(node.type);
        if (style == 3 && small)
        {
            var tiny = WuxiaTheme.Text("Caption", rect, name, 16, TextAnchor.UpperCenter, new Color(.20f, .16f, .11f, locked ? .6f : .95f));
            UIFactory.Place(tiny.rectTransform, new Vector2(.5f, 0), new Vector2(.5f, 1), new Vector2(0, captionDrop), new Vector2(220, 24));
            var glow = tiny.gameObject.AddComponent<Outline>(); glow.effectColor = new Color(1f, .96f, .86f, .9f); glow.effectDistance = new Vector2(1.5f, -1.5f);
        }
        else
        {
            bool dark = style == 2;
            var caption = UIFactory.CreateImage("CaptionPaper", rect, dark ? new Color(.16f, .13f, .10f, locked ? .7f : .95f) : locked ? new Color(.95f, .91f, .81f, .85f) : new Color(.99f, .94f, .83f, style == 1 ? .88f : 1f));
            UIFactory.Place(caption.rectTransform, new Vector2(.5f, 0), new Vector2(.5f, 1), new Vector2(0, captionDrop), new Vector2(220, style == 1 ? 34 : 40));
            if (fresh || current)
            {
                var underline = UIFactory.CreateImage("CaptionAccent", caption.transform, current ? WuxiaTheme.Vermilion : gold);
                UIFactory.Place(underline.rectTransform, new Vector2(.5f, 0), new Vector2(.5f, 0), Vector2.zero, new Vector2(220, 3));
            }
            var label = WuxiaTheme.Text("Caption", caption.transform, name, 22, TextAnchor.MiddleCenter, dark ? new Color(1f, .94f, .82f, locked ? .7f : 1f) : ink);
            UIFactory.Stretch(label.rectTransform, 3, 0, 3, 0); label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.resizeTextForBestFit = true; label.resizeTextMinSize = 17; label.resizeTextMaxSize = style == 1 ? 20 : 22; label.fontStyle = FontStyle.Bold;
        }

        button.interactable = canTravel;
        var colors = button.colors; colors.disabledColor = Color.white; button.colors = colors;
        if (canTravel) button.onClick.AddListener(() => OnNodeClicked(node));
        var hover = button.gameObject.AddComponent<MapNodeHover>();
        hover.Enter = () => ShowInfo(node); hover.Exit = HideInfo;
    }

    private void DrawNode(RectTransform parent, MapNode node)
    {
        if (Run.IsStoryMap) { if (PreviewStyle == 0) DrawStoryNode(parent, node); else DrawStoryNodeAlt(parent, node, PreviewStyle); return; }
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
            Vector2 from = nodePositions[path[i-1]]+new Vector2(0,TravellerLift);
            Vector2 to = nodePositions[path[i]]+new Vector2(0,TravellerLift);
            scroll.StopMovement();
            float duration=Mathf.Clamp(Vector2.Distance(from,to)/500f,.65f,1.6f);
            for (float elapsed=0;elapsed<duration;elapsed+=Time.unscaledDeltaTime)
            {
                if (traveller != null) traveller.anchoredPosition = StoryMapLayout.Point(from-new Vector2(0,TravellerLift),to-new Vector2(0,TravellerLift),Mathf.SmoothStep(0,1,elapsed/duration))+new Vector2(0,TravellerLift);
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

    // ─────────────────────── 단서 기록 ───────────────────────

    private void CloseJournal() { if (journal != null) { Destroy(journal); journal = null; } }

    /// <summary>
    /// 단서 기록 화면. 왼쪽은 다녀온 순서대로 정리한 장소 목록(그림·종류·상태),
    /// 오른쪽은 고른 장소의 단서 요약과 대화 기록이다.
    /// </summary>
    private void ShowJournal()
    {
        if (travelling || journal != null) return;
        HideInfo();
        var shade=UIFactory.CreateImage("ClueJournalOverlay",canvas.transform,new Color(0,0,0,.74f),true);
        UIFactory.Stretch(shade.rectTransform);journal=shade.gameObject;
        var paper=UIFactory.CreateImage("JournalPaper",shade.transform,new Color(.96f,.91f,.81f),true);
        UIFactory.Stretch(paper.rectTransform,100,70,100,70);
        var border=UIFactory.CreateImage("JournalBorder",paper.transform,new Color(.53f,.40f,.23f,.30f));
        UIFactory.Stretch(border.rectTransform,10,10,10,10);
        var inner=UIFactory.CreateImage("JournalInner",paper.transform,new Color(.965f,.915f,.815f));
        UIFactory.Stretch(inner.rectTransform,12,12,12,12);

        // 기록 대상: 다녀온 순서대로, 그다음 도착 대화만 남은 곳.
        var entries=new List<MapNode>();
        foreach(var id in Run.Data.clearedNodeIds){var visited=Run.Data.map.Find(id);if(visited!=null&&!entries.Contains(visited))entries.Add(visited);}
        foreach(var seen in Run.Data.map.nodes)
            if(!entries.Contains(seen)&&(Run.HasSeenStory(seen.id+"_visit")||Run.HasSeenStory(seen.id+"_after")||Run.HasSeenStory(seen.id+"_enter")||Run.HasSeenStory(seen.id+"_prologue")))entries.Add(seen);
        int battles=0,won=0;
        foreach(var fight in Run.Data.map.nodes){if(!fight.IsBattle)continue;battles++;if(Run.Data.clearedNodeIds.Contains(fight.id))won++;}

        var title=WuxiaTheme.Text("JournalTitle",paper.transform,"여정의 기록",38,TextAnchor.MiddleLeft,WuxiaTheme.Ink);
        UIFactory.Place(title.rectTransform,new Vector2(0,1),new Vector2(0,1),new Vector2(44,-26),new Vector2(260,60));title.fontStyle=FontStyle.Bold;
        var summary=WuxiaTheme.Text("JournalSummary",paper.transform,"기록한 장소 "+entries.Count+"곳      돌파한 전투 "+won+" / "+battles,21,TextAnchor.MiddleLeft,new Color(.43f,.38f,.29f));
        UIFactory.Place(summary.rectTransform,new Vector2(0,1),new Vector2(0,1),new Vector2(300,-34),new Vector2(700,44));
        var close=UIFactory.CreateButton("CloseJournal",paper.transform,"닫기  (Esc)",22,WuxiaTheme.Ink,CloseJournal);
        UIFactory.Place(close.GetComponent<RectTransform>(),Vector2.one,Vector2.one,new Vector2(-34,-30),new Vector2(170,52));
        var rule=UIFactory.CreateImage("JournalRule",paper.transform,new Color(.53f,.40f,.23f,.45f));
        rule.rectTransform.anchorMin=new Vector2(0,1);rule.rectTransform.anchorMax=Vector2.one;
        rule.rectTransform.offsetMin=new Vector2(34,-98);rule.rectTransform.offsetMax=new Vector2(-34,-96);

        var listView=JournalScroll("LocationList",paper.transform,34,30,0,112,out var listBody);
        listView.GetComponent<RectTransform>().anchorMax=new Vector2(0,1);
        listView.GetComponent<RectTransform>().offsetMax=new Vector2(440,-112);
        var divider=UIFactory.CreateImage("JournalDivider",paper.transform,new Color(.53f,.40f,.23f,.35f));
        divider.rectTransform.anchorMin=new Vector2(0,0);divider.rectTransform.anchorMax=new Vector2(0,1);
        divider.rectTransform.offsetMin=new Vector2(462,34);divider.rectTransform.offsetMax=new Vector2(464,-112);
        var detailView=JournalScroll("EntryDetail",paper.transform,500,34,60,116,out var detailBody);

        if(entries.Count==0)
        {
            var empty=WuxiaTheme.Text("JournalEmpty",paper.transform,"아직 기록된 단서가 없습니다.\n장소를 다녀오면 이곳에 정리됩니다.",26,TextAnchor.MiddleCenter,new Color(.43f,.38f,.29f));
            UIFactory.Stretch(empty.rectTransform,500,34,60,116);
            return;
        }

        Color rowIdle=new Color(.90f,.84f,.72f,.55f),rowChosen=new Color(.20f,.17f,.13f,.96f),soft=new Color(.43f,.38f,.29f);
        var rows=new List<Image>();var rowTitles=new List<Text>();var rowSubs=new List<Text>();var rowBars=new List<GameObject>();

        System.Action<int> select = index => {
            for(int j=0;j<rows.Count;j++) {
                bool chosen=j==index;
                rows[j].color=chosen?rowChosen:rowIdle;
                rowTitles[j].color=chosen?new Color(1f,.95f,.84f):WuxiaTheme.Ink;
                rowSubs[j].color=chosen?new Color(.93f,.80f,.55f):soft;
                rowBars[j].SetActive(chosen);
            }
            foreach(Transform child in detailBody) {child.gameObject.SetActive(false);Destroy(child.gameObject);}
            var n=entries[index];bool cleared=Run.Data.clearedNodeIds.Contains(n.id);bool here=Run.Data.currentNodeId==n.id;
            float y=0;

            // 머리말: 장소 그림, 이름, 종류, 상태
            var head=UIFactory.CreateRect("EntryHead",detailBody);
            head.anchorMin=new Vector2(0,1);head.anchorMax=Vector2.one;head.pivot=new Vector2(.5f,1);head.anchoredPosition=Vector2.zero;head.sizeDelta=new Vector2(0,150);
            var artHolder=UIFactory.CreateRect("EntryArt",head);
            UIFactory.Place(artHolder,new Vector2(0,1),new Vector2(0,1),new Vector2(0,-4),new Vector2(136,136));
            JournalArt(artHolder,n,4);
            var name=WuxiaTheme.Text("EntryTitle",head,n.title,36,TextAnchor.MiddleLeft,WuxiaTheme.Ink);
            UIFactory.Place(name.rectTransform,new Vector2(0,1),new Vector2(0,1),new Vector2(164,-10),new Vector2(860,50));name.fontStyle=FontStyle.Bold;
            var kind=WuxiaTheme.Text("EntryKind",head,KindLabel(n.type),21,TextAnchor.MiddleLeft,WuxiaTheme.Jade);
            UIFactory.Place(kind.rectTransform,new Vector2(0,1),new Vector2(0,1),new Vector2(166,-62),new Vector2(600,30));
            string state=here?"지금 있는 곳":cleared?(n.IsBattle?"돌파 완료":"다녀옴"):"전투 전 · 도착 기록";
            var chip=UIFactory.CreateImage("EntryState",head,here?WuxiaTheme.Vermilion:cleared?new Color(.30f,.42f,.34f):new Color(.52f,.46f,.36f));
            UIFactory.Place(chip.rectTransform,new Vector2(0,1),new Vector2(0,1),new Vector2(166,-100),new Vector2(190,32));
            var chipText=WuxiaTheme.Text("EntryStateText",chip.transform,state,18,TextAnchor.MiddleCenter,new Color(1f,.95f,.84f));
            UIFactory.Stretch(chipText.rectTransform);chipText.fontStyle=FontStyle.Bold;
            y=172;

            JournalHeading(detailBody,"단서 요약",ref y);
            AddJournalBox(detailBody,cleared?n.story:(n.id=="cave"?"사부의 지시를 따라 동굴 벽화 수련을 준비한다.":"아직 전투를 마치지 않았다. 도착 당시의 대화만 기록되어 있다."),ref y);

            bool anyTalk=false;
            foreach(var phase in new[]{"_prologue","_enter","_visit","_after"}) {
                if(!Run.HasSeenStory(n.id+phase))continue;
                var sequence=StoryCutscene.Load(n.id+phase);if(sequence==null||sequence.lines==null)continue;
                if(!anyTalk){JournalHeading(detailBody,"대화 기록",ref y);anyTalk=true;}
                JournalSceneChip(detailBody,phase=="_prologue"?"사라진 사부":phase=="_enter"?"동굴 입장":phase=="_visit"?"첫 방문":"전투 이후",ref y);
                foreach(var line in sequence.lines) {
                    bool narration=string.IsNullOrEmpty(line.speaker);
                    if(!narration)
                    {
                        Color who=line.speaker.Contains("서진")?WuxiaTheme.Jade:line.speaker.Contains("도윤")?new Color(.36f,.28f,.16f):new Color(.56f,.20f,.13f);
                        var speaker=AddJournalText(detailBody,"Speaker",line.speaker,20,who,ref y,3,20);speaker.fontStyle=FontStyle.Bold;
                    }
                    var said=AddJournalText(detailBody,"Dialogue",line.text,24,narration?soft:WuxiaTheme.Ink,ref y,18,20);
                    if(narration)said.fontStyle=FontStyle.Italic;
                }
                y+=10;
            }
            detailBody.sizeDelta=new Vector2(0,y+30);detailView.verticalNormalizedPosition=1;
        };

        for(int i=0;i<entries.Count;i++) {
            int selected=i;var n=entries[i];bool cleared=Run.Data.clearedNodeIds.Contains(n.id);bool here=Run.Data.currentNodeId==n.id;
            var button=UIFactory.CreateButton("JournalLocation_"+n.id,listBody,"",20,rowIdle,()=>select(selected));
            var rect=button.GetComponent<RectTransform>();rect.anchorMin=new Vector2(0,1);rect.anchorMax=Vector2.one;rect.pivot=new Vector2(.5f,1);rect.anchoredPosition=new Vector2(0,-i*100);rect.sizeDelta=new Vector2(0,90);
            var colors=button.colors;colors.highlightedColor=new Color(1.06f,1.03f,.96f);colors.pressedColor=new Color(.9f,.9f,.9f);button.colors=colors;
            var bar=UIFactory.CreateImage("ChosenBar",rect,new Color(.96f,.76f,.32f));
            bar.rectTransform.anchorMin=Vector2.zero;bar.rectTransform.anchorMax=new Vector2(0,1);bar.rectTransform.offsetMin=Vector2.zero;bar.rectTransform.offsetMax=new Vector2(6,0);
            var art=UIFactory.CreateRect("RowArt",rect);
            UIFactory.Place(art,new Vector2(0,.5f),new Vector2(0,.5f),new Vector2(18,0),new Vector2(68,68));
            JournalArt(art,n,3);
            var rowTitle=WuxiaTheme.Text("RowTitle",rect,n.title,22,TextAnchor.MiddleLeft,WuxiaTheme.Ink);
            UIFactory.Place(rowTitle.rectTransform,new Vector2(0,1),new Vector2(0,1),new Vector2(100,-12),new Vector2(292,36));
            rowTitle.fontStyle=FontStyle.Bold;rowTitle.resizeTextForBestFit=true;rowTitle.resizeTextMinSize=16;rowTitle.resizeTextMaxSize=22;
            string status=here?"지금 있는 곳":cleared?(n.IsBattle?"돌파":"다녀옴"):"전투 전";
            var rowSub=WuxiaTheme.Text("RowSub",rect,(i+1).ToString("00")+"   "+KindShort(n.type)+"  ·  "+status,17,TextAnchor.MiddleLeft,soft);
            UIFactory.Place(rowSub.rectTransform,new Vector2(0,1),new Vector2(0,1),new Vector2(100,-50),new Vector2(292,28));
            rows.Add(button.GetComponent<Image>());rowTitles.Add(rowTitle);rowSubs.Add(rowSub);rowBars.Add(bar.gameObject);
        }
        listBody.sizeDelta=new Vector2(0,entries.Count*100+4);
        int current=entries.FindIndex(e=>e.id==Run.Data.currentNodeId);
        int focus=current>=0?current:entries.Count-1;
        select(focus);
        // 고른 장소가 목록 아래쪽이면 보이도록 내려 준다.
        Canvas.ForceUpdateCanvases();
        float viewHeight=listView.viewport.rect.height,total=listBody.rect.height;
        if(total>viewHeight)listView.verticalNormalizedPosition=1f-Mathf.Clamp01((focus*100f-viewHeight*.4f)/(total-viewHeight));
    }

    private static string KindShort(NodeType type)
    {
        return type==NodeType.Rest?"쉼터":type==NodeType.Event?"탐색":type==NodeType.Boss?"최종 전투":type==NodeType.Elite?"강적":"전투";
    }

    /// <summary>기록 화면용 원형 장소 그림. 그림이 없으면 종류 기호를 넣는다.</summary>
    private void JournalArt(RectTransform holder,MapNode node,float inset)
    {
        var rim=WuxiaTheme.Ornament("ArtRim",holder,WuxiaGraphic.Shape.Disc,new Color(.53f,.40f,.23f,.9f));
        UIFactory.Stretch(rim.rectTransform);
        Texture texture;Rect uv;
        if(TryNodeArt(node,out texture,out uv)){CircleArt(holder,texture,uv,inset,Color.white);return;}
        var paper=WuxiaTheme.Ornament("ArtPaper",holder,WuxiaGraphic.Shape.Disc,new Color(1f,.95f,.82f));
        UIFactory.Stretch(paper.rectTransform,inset,inset,inset,inset);
        var glyph=WuxiaTheme.Ornament("ArtGlyph",holder,GlyphFor(node.type),WuxiaTheme.Ink);
        float pad=holder.sizeDelta.x*.24f;UIFactory.Stretch(glyph.rectTransform,pad,pad,pad,pad);
    }

    private void JournalHeading(RectTransform parent,string value,ref float y)
    {
        var mark=UIFactory.CreateImage("HeadingMark",parent,WuxiaTheme.Vermilion);
        UIFactory.Place(mark.rectTransform,new Vector2(0,1),new Vector2(0,1),new Vector2(0,-(y+7)),new Vector2(6,22));
        var text=WuxiaTheme.Text("Heading",parent,value,23,TextAnchor.MiddleLeft,WuxiaTheme.Vermilion);
        UIFactory.Place(text.rectTransform,new Vector2(0,1),new Vector2(0,1),new Vector2(18,-y),new Vector2(600,36));text.fontStyle=FontStyle.Bold;
        y+=46;
    }

    private void JournalSceneChip(RectTransform parent,string value,ref float y)
    {
        var chip=UIFactory.CreateImage("SceneChip",parent,new Color(.85f,.78f,.64f,.75f));
        UIFactory.Place(chip.rectTransform,new Vector2(0,1),new Vector2(0,1),new Vector2(0,-y),new Vector2(150,30));
        var text=WuxiaTheme.Text("SceneChipText",chip.transform,value,18,TextAnchor.MiddleCenter,new Color(.36f,.30f,.20f));
        UIFactory.Stretch(text.rectTransform);text.fontStyle=FontStyle.Bold;
        var line=UIFactory.CreateImage("SceneLine",parent,new Color(.53f,.40f,.23f,.28f));
        line.rectTransform.anchorMin=new Vector2(0,1);line.rectTransform.anchorMax=Vector2.one;line.rectTransform.pivot=new Vector2(.5f,1);
        line.rectTransform.offsetMin=new Vector2(162,-(y+16));line.rectTransform.offsetMax=new Vector2(0,-(y+15));
        y+=44;
    }

    /// <summary>단서 요약 상자: 연한 종이 바탕에 붉은 세로줄.</summary>
    private void AddJournalBox(RectTransform parent,string value,ref float y)
    {
        var box=UIFactory.CreateImage("ClueBox",parent,new Color(1f,.975f,.89f,.95f));
        box.rectTransform.anchorMin=new Vector2(0,1);box.rectTransform.anchorMax=Vector2.one;box.rectTransform.pivot=new Vector2(.5f,1);
        box.rectTransform.anchoredPosition=new Vector2(0,-y);box.rectTransform.sizeDelta=new Vector2(0,80);
        var bar=UIFactory.CreateImage("ClueBar",box.transform,WuxiaTheme.Vermilion);
        bar.rectTransform.anchorMin=Vector2.zero;bar.rectTransform.anchorMax=new Vector2(0,1);bar.rectTransform.offsetMin=Vector2.zero;bar.rectTransform.offsetMax=new Vector2(5,0);
        var text=WuxiaTheme.Text("ClueText",box.transform,value,25,TextAnchor.UpperLeft,WuxiaTheme.Ink);
        text.horizontalOverflow=HorizontalWrapMode.Wrap;text.supportRichText=false;text.lineSpacing=1.15f;
        UIFactory.Stretch(text.rectTransform,26,16,22,16);
        Canvas.ForceUpdateCanvases();
        float height=Mathf.Max(34,text.preferredHeight)+36;
        box.rectTransform.sizeDelta=new Vector2(0,height);y+=height+34;
    }

    private ScrollRect JournalScroll(string name,Transform parent,float left,float bottom,float right,float top,out RectTransform body)
    {
        var view=UIFactory.CreateImage(name,parent,Color.clear,true);UIFactory.Stretch(view.rectTransform,left,bottom,right,top);
        view.gameObject.AddComponent<RectMask2D>();var scroller=view.gameObject.AddComponent<ScrollRect>();
        body=UIFactory.CreateRect("Content",view.transform);body.anchorMin=new Vector2(0,1);body.anchorMax=Vector2.one;body.pivot=new Vector2(.5f,1);
        scroller.content=body;scroller.viewport=view.rectTransform;scroller.horizontal=false;scroller.movementType=ScrollRect.MovementType.Clamped;scroller.scrollSensitivity=45;
        return scroller;
    }
    private Text AddJournalText(RectTransform parent,string name,string value,int size,Color color,ref float y,float gap,float indent=0)
    {
        var text=WuxiaTheme.Text(name,parent,value,size,TextAnchor.UpperLeft,color);text.horizontalOverflow=HorizontalWrapMode.Wrap;text.supportRichText=false;
        text.rectTransform.anchorMin=new Vector2(0,1);text.rectTransform.anchorMax=Vector2.one;text.rectTransform.pivot=new Vector2(.5f,1);
        text.rectTransform.anchoredPosition=new Vector2(indent*.5f,-y);text.rectTransform.sizeDelta=new Vector2(-indent,60);
        Canvas.ForceUpdateCanvases();float height=Mathf.Max(size+8,text.preferredHeight+4);
        text.rectTransform.sizeDelta=new Vector2(-indent,height);y+=height+gap;
        return text;
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

/// <summary>지점 위에 마우스가 올라오고 나가는 것만 알린다. 드래그와 휠은 건드리지 않아 지도 스크롤이 그대로 된다.</summary>
public class MapNodeHover : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
{
    public System.Action Enter, Exit;
    public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData eventData) { if (Enter != null) Enter(); }
    public void OnPointerExit(UnityEngine.EventSystems.PointerEventData eventData) { if (Exit != null) Exit(); }
    void OnDisable() { if (Exit != null) Exit(); }
}
