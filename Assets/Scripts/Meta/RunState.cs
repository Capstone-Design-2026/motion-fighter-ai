using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>한 판(run) 의 진행 상황. 통째로 JSON 으로 저장된다.</summary>
[Serializable]
public class RunData
{
    public List<string> discoveredNodeIds = new List<string>();
    public int storyPresentationVersion;
    public List<string> seenStoryIds = new List<string>();
    public int tutorialVersion;
    public bool tutorialCompleted;
    public int tutorialStep;
    public int seed;
    public MapData map = new MapData();

    public string currentNodeId = "";              // 지금 서 있는 칸. 비어 있으면 아직 시작 전
    public List<string> clearedNodeIds = new List<string>();

    public int playerHealth = 100;
    public int playerMaxHealth = 100;
    public int gold;

    public string pendingNodeId = "";              // 전투씬에 들어갈 때 어떤 칸인지 기록
    public bool finished;                          // 보스까지 클리어함
}

/// <summary>
/// 씬이 바뀌어도 살아남는 진행 상황 보관소.
/// 어떤 씬에서 플레이를 눌러도 자동으로 만들어지므로 씬에 배치할 필요가 없다.
/// 흐름: StartScene → (새 게임) → MapScene → (칸 선택) → GameScene → (승/패) → MapScene
/// </summary>
public class RunState : MonoBehaviour
{
    private const string SaveKey = "VisionStriker.Run";

    public static RunState Instance { get; private set; }

    public RunConfig Config { get; private set; }
    public RunData Data { get; private set; }

    /// <summary>진행 중인 판이 있는가.</summary>
    public bool HasRun { get { return Data != null && Data.map != null && Data.map.nodes.Count > 0; } }

    /// <summary>전투씬으로 들어가는 중인가. GameScene 을 단독 실행했는지 구분하는 데 쓴다.</summary>
    public bool HasPendingBattle { get { return HasRun && !string.IsNullOrEmpty(Data.pendingNodeId); } }

    /// <summary>직전 전투 결과. MapScene 에서 연출용으로 읽는다.</summary>
    public bool LastBattleWon { get; private set; }
    public string LastMessage { get; set; }

    // ─────────────────────── 부팅 ───────────────────────

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("[RunState]");
        Instance = go.AddComponent<RunState>();
        DontDestroyOnLoad(go);
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        Config = Resources.Load<RunConfig>(RunConfig.ResourcePath);
        if (Config == null)
        {
            // 에셋이 아직 없어도 굴러가도록 기본값으로 대체한다.
            Config = ScriptableObject.CreateInstance<RunConfig>();
            Debug.LogWarning("[RunState] Assets/Resources/RunConfig.asset 이 없어서 기본 설정으로 진행합니다. "
                           + "Project 창 우클릭 → Create → VisionStriker → Run Config 로 만들 수 있습니다.");
        }
    }

    // ─────────────────────── 판 만들기 / 저장 ───────────────────────

    public void StartNewRun(int seed = 0)
    {
        if (seed == 0) seed = UnityEngine.Random.Range(1, int.MaxValue);

        Data = new RunData
        {
            seed = seed,
            storyPresentationVersion = 1,
            tutorialVersion = 1,
            playerMaxHealth = Mathf.Max(1, Config.playerMaxHealth),
        };
        Data.playerHealth = Data.playerMaxHealth;
        Data.map = MapGenerator.Generate(Config.map, Config, seed);

        if (IsStoryMap)
        {
            Data.currentNodeId = "cave";
            Data.discoveredNodeIds.Add("cave");
        }
        LastMessage = IsStoryMap ? Data.map.Find("cave").story : null;
        LastBattleWon = false;
        RefreshStatus();
        Save();
        Debug.Log("[RunState] 새 판 시작. seed=" + seed + ", 칸 수=" + Data.map.nodes.Count);
    }

    public bool HasSave()
    {
        return PlayerPrefs.HasKey(SaveKey) && !string.IsNullOrEmpty(PlayerPrefs.GetString(SaveKey));
    }

    public bool LoadRun()
    {
        if (!HasSave()) return false;
        try
        {
            Data = JsonUtility.FromJson<RunData>(PlayerPrefs.GetString(SaveKey));
        }
        catch (Exception e)
        {
            Debug.LogWarning("[RunState] 저장 내용을 읽지 못했습니다: " + e.Message);
            Data = null;
        }
        if (!HasRun) { Data = null; return false; }

        if(Data.tutorialVersion==0) {
            Data.tutorialVersion=1;
            Data.tutorialCompleted=Data.clearedNodeIds.Contains("cave");
            Data.tutorialStep=Data.tutorialCompleted?CaveTutorial.Actions.Length:0;
            if(Data.tutorialCompleted) {
                if(Data.seenStoryIds==null)Data.seenStoryIds=new List<string>();
                if(!Data.seenStoryIds.Contains("cave_prologue"))Data.seenStoryIds.Add("cave_prologue");
            }
            Save();
        }
        // Existing saves must not replay places completed before cutscenes were introduced.
        if (Data.seenStoryIds == null) Data.seenStoryIds = new List<string>();
        if (Data.storyPresentationVersion == 0) {
            foreach(var id in Data.clearedNodeIds) {
                if(!Data.seenStoryIds.Contains(id+"_visit")) Data.seenStoryIds.Add(id+"_visit");
                var oldNode=Data.map.Find(id);
                if(oldNode!=null && oldNode.IsBattle && !Data.seenStoryIds.Contains(id+"_after")) Data.seenStoryIds.Add(id+"_after");
            }
            Data.storyPresentationVersion=1;
            Save();
        }
        RefreshStatus();
        return true;
    }

    public void Save()
    {
        if (RedboolDuelTest.Active || RedboolPvp.Active) return;
        if (!HasRun) return;
        PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(Data));
        PlayerPrefs.Save();
    }

    public void ClearSave()
    {
        if (RedboolDuelTest.Active || RedboolPvp.Active) return;
        PlayerPrefs.DeleteKey(SaveKey);
        PlayerPrefs.Save();
    }

    public void AbandonRun()
    {
        Data = null;
        ClearSave();
    }

    // ─────────────────────── 지도 진행 ───────────────────────

    /// <summary>각 칸이 지금 선택 가능한지 다시 계산한다. 지도를 그리기 전에 호출한다.</summary>
    public void RefreshStatus()
    {
        if (!HasRun) return;

        if (IsStoryMap)
        {
            if (Data.discoveredNodeIds == null) Data.discoveredNodeIds = new List<string>();
            foreach (var id in Data.clearedNodeIds)
            {
                var visited = Data.map.Find(id);
                if (visited == null) continue;
                Discover(id);
                foreach (var near in StoryMap.Neighbors(Data.map, visited)) Discover(near.id);
            }
            foreach (var node in Data.map.nodes)
                node.status = Data.clearedNodeIds.Contains(node.id) ? NodeStatus.Cleared
                    : FindTravelPath(node.id) != null ? NodeStatus.Available : NodeStatus.Locked;
            if(!Data.tutorialCompleted && Data.currentNodeId=="cave") {
                Discover("cave");
                foreach(var node in Data.map.nodes)node.status=node.id=="cave"?NodeStatus.Available:NodeStatus.Locked;
            }
            return;
        }
        foreach (var node in Data.map.nodes)
            node.status = Data.clearedNodeIds.Contains(node.id) ? NodeStatus.Cleared : NodeStatus.Locked;

        if (string.IsNullOrEmpty(Data.currentNodeId))
        {
            // 아직 한 칸도 안 밟았으면 0층 전체가 시작 후보
            foreach (var node in Data.map.RowNodes(0))
                if (node.status != NodeStatus.Cleared) node.status = NodeStatus.Available;
            return;
        }

        var current = Data.map.Find(Data.currentNodeId);
        if (current == null) return;

        foreach (var id in current.next)
        {
            var n = Data.map.Find(id);
            if (n != null && n.status != NodeStatus.Cleared) n.status = NodeStatus.Available;
        }
    }

    public void CompleteTutorial() {
        if(!HasRun || Data.tutorialCompleted)return;
        Data.tutorialCompleted=true;Data.tutorialStep=CaveTutorial.Actions.Length;AdvanceTo("cave");Save();
    }
    public bool HasSeenStory(string key) { return Data != null && Data.seenStoryIds != null && Data.seenStoryIds.Contains(key); }
    public void CompleteStory(string key) {
        if (!HasRun) return;
        if (Data.seenStoryIds == null) Data.seenStoryIds = new List<string>();
        if (!Data.seenStoryIds.Contains(key)) Data.seenStoryIds.Add(key);
        if (!Data.finished) Save();
    }
    private System.Collections.IEnumerator StoryEnding() {
        yield return StoryCutscene.Play(this,"final_after");
        SceneRoutes.LoadEnding();
    }

    public bool IsStoryMap { get { return HasRun && Data.map.storyVersion > 0; } }
    private void Discover(string id)
    {
        if (!Data.discoveredNodeIds.Contains(id)) Data.discoveredNodeIds.Add(id);
    }
    public bool IsDiscovered(string id)
    {
        return !IsStoryMap || (Data.discoveredNodeIds != null && Data.discoveredNodeIds.Contains(id));
    }
    // Each click crosses exactly one visible connection. Even cleared detours require visiting the junction.
    public List<string> FindTravelPath(string destination)
    {
        if (!IsStoryMap || !IsDiscovered(destination)) return null;
        var current = CurrentNode;
        var target = Data.map.Find(destination);
        if (current == null || target == null || !Data.clearedNodeIds.Contains(current.id)) return null;
        if (destination == current.id) return new List<string> { current.id };
        if (!current.next.Contains(destination) && !target.next.Contains(current.id)) return null;
        return new List<string> { current.id, destination };
    }
    public bool MoveAlongClearedPath(string destination)
    {
        if (!HasRun || Data.finished || HasPendingBattle || !Data.clearedNodeIds.Contains(destination)) return false;
        var path = FindTravelPath(destination);
        if (path == null) return false;
        Data.currentNodeId = destination;
        LastMessage = Config.carryPlayerHealth ? "지나온 길로 이동했다. 체력과 휴식 사용 기록은 유지된다." : "지나온 길로 이동했다.";
        RefreshStatus(); Save(); return true;
    }

    public MapNode CurrentNode { get { return HasRun ? Data.map.Find(Data.currentNodeId) : null; } }

    public int CurrentFloor
    {
        get
        {
            var n = CurrentNode;
            return n != null ? n.row + 1 : 0;
        }
    }

    /// <summary>
    /// 지도에서 칸을 눌렀을 때. 전투 칸이면 GameScene 으로 넘어가고,
    /// 그 외(휴식/이벤트/상점)는 아직 내용이 없으므로 즉시 처리하고 지도에 남는다.
    /// </summary>
    public void EnterNode(MapNode node)
    {
        if (!HasRun || Data.finished || HasPendingBattle || node == null) return;
        if(IsStoryMap && node.id=="cave" && !Data.tutorialCompleted)return;
        if (IsStoryMap && node.status == NodeStatus.Cleared)
        {
            MoveAlongClearedPath(node.id);
            return;
        }
        if (node.status != NodeStatus.Available) return;
        if (IsStoryMap && FindTravelPath(node.id) == null) return;

        if (node.IsBattle)
        {
            Data.pendingNodeId = node.id;
            Save();
            SceneRoutes.LoadBattle();
            return;
        }

        switch (node.type)
        {
            case NodeType.Rest:
                if (!Config.carryPlayerHealth)
                {
                    // 체력이 이어지지 않으면 늘 가득 차 있으므로 회복 줄을 띄우지 않는다.
                    Data.playerHealth = Data.playerMaxHealth;
                    LastMessage = IsStoryMap ? "" : "휴식";
                    break;
                }
                int heal = Mathf.RoundToInt(Data.playerMaxHealth * Config.restHealPercent);
                int before = Data.playerHealth;
                Data.playerHealth = Mathf.Min(Data.playerMaxHealth, Data.playerHealth + heal);
                LastMessage = "휴식 - 체력 " + before + " → " + Data.playerHealth;
                break;

            case NodeType.Event:
                LastMessage = string.IsNullOrEmpty(node.story) ? "주변을 살펴보았다." : node.story;
                break;

            case NodeType.Shop:
                LastMessage = "상점 - 아직 준비 중입니다. 그냥 지나갑니다.";
                break;
        }

        if (IsStoryMap && node.type == NodeType.Rest) LastMessage = string.IsNullOrEmpty(LastMessage) ? node.story : LastMessage + "\n" + node.story;
        AdvanceTo(node.id);
        Save();
    }

    /// <summary>전투가 끝났을 때 BattleSceneBridge 가 호출한다.</summary>
    public void ResolveBattle(bool won, int remainingHealth)
    {
        if (!HasRun) { SceneRoutes.LoadStart(); return; }

        LastBattleWon = won;
        string nodeId = Data.pendingNodeId;
        Data.pendingNodeId = "";

        if (!won)
        {
            // 2026-11-19: 져도 판을 지우지 않는다. 진 칸 앞에 선 채로 저장해 두면 시작 화면의 "이어하기"가 지도로 데려가고,
            // 그 칸을 다시 눌러 재도전한다(체력은 스테이지마다 가득). 안내는 전투 화면의 패배 줄(BattleSceneBridge.ShowBanner)이
            // 맡으므로 시작 화면에는 글을 남기지 않는다.
            LastMessage = null;
            Save();
            SceneRoutes.LoadStart();
            return;
        }

        if (Config.carryPlayerHealth)
            Data.playerHealth = Mathf.Clamp(remainingHealth, 1, Data.playerMaxHealth);
        else
            Data.playerHealth = Data.playerMaxHealth;

        var node = Data.map.Find(nodeId);
        AdvanceTo(nodeId);

        if (node != null && node.type == NodeType.Boss)
        {
            Data.finished = true;
            LastMessage = null;   // 엔딩과 크레딧이 끝을 알린다. 시작 화면에 "최종 보스 격파!"를 남기지 않는다.
            ClearSave(); // 완료한 판은 이어하기 대상으로 남기지 않는다.
            if (IsStoryMap) StartCoroutine(StoryEnding());
            else SceneRoutes.LoadEnding();
            return;
        }
        else
        {
            LastMessage = IsStoryMap && node != null ? node.story
                : Config.carryPlayerHealth ? "승리 - 남은 체력 " + Data.playerHealth + " / " + Data.playerMaxHealth : "승리";
        }

        Save();
        SceneRoutes.LoadMap();
    }

    private void AdvanceTo(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId)) return;
        if (!Data.clearedNodeIds.Contains(nodeId)) Data.clearedNodeIds.Add(nodeId);
        Data.currentNodeId = nodeId;
        RefreshStatus();
    }

    /// <summary>이번 전투에 쓸 스테이지 정의. 없으면 null (= GameScene 기본 적).</summary>
    public StageDefinition PendingStage
    {
        get
        {
            if (!HasPendingBattle) return null;
            var node = Data.map.Find(Data.pendingNodeId);
            var stage = node != null ? Config.Get(node.stageId) : null;
            if (!IsStoryMap || stage == null) return stage;
            var copy = JsonUtility.FromJson<StageDefinition>(JsonUtility.ToJson(stage));
            copy.displayName = StoryMap.EnemyName(node);   // HUD 에는 장소 없이 상대 이름만
            copy.nodeType = node.type;
            return copy;
        }
    }

    public MapNode PendingNode
    {
        get { return HasPendingBattle ? Data.map.Find(Data.pendingNodeId) : null; }
    }
}
