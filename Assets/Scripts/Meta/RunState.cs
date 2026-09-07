using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>한 판(run) 의 진행 상황. 통째로 JSON 으로 저장된다.</summary>
[Serializable]
public class RunData
{
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
            playerMaxHealth = Mathf.Max(1, Config.playerMaxHealth),
        };
        Data.playerHealth = Data.playerMaxHealth;
        Data.map = MapGenerator.Generate(Config.map, Config, seed);

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

        RefreshStatus();
        return true;
    }

    public void Save()
    {
        if (!HasRun) return;
        PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(Data));
        PlayerPrefs.Save();
    }

    public void ClearSave()
    {
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
        if (!HasRun || node == null) return;
        if (node.status != NodeStatus.Available) return;

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
                int heal = Mathf.RoundToInt(Data.playerMaxHealth * Config.restHealPercent);
                int before = Data.playerHealth;
                Data.playerHealth = Mathf.Min(Data.playerMaxHealth, Data.playerHealth + heal);
                LastMessage = "휴식 - 체력 " + before + " → " + Data.playerHealth;
                break;

            case NodeType.Event:
                LastMessage = "이벤트 - 아직 준비 중입니다. 그냥 지나갑니다.";
                break;

            case NodeType.Shop:
                LastMessage = "상점 - 아직 준비 중입니다. 그냥 지나갑니다.";
                break;
        }

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
            LastMessage = "패배했습니다. 이 판은 여기서 끝납니다.";
            AbandonRun();
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
            LastMessage = "보스 격파! 스테이지 클리어.";
        }
        else
        {
            LastMessage = "승리 - 남은 체력 " + Data.playerHealth + " / " + Data.playerMaxHealth;
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
            return node != null ? Config.Get(node.stageId) : null;
        }
    }

    public MapNode PendingNode
    {
        get { return HasPendingBattle ? Data.map.Find(Data.pendingNodeId) : null; }
    }
}
