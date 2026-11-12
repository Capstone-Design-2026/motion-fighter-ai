using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 공통 전투 수치를 유지하면서 지도 노드별로 다른 적 외형을 지정한다.
/// </summary>
[Serializable]
public class NodeEnemyVisual
{
    [Tooltip("스토리 지도 노드 ID. 예: scout. 체력과 AI 수치는 기존 스테이지 설정을 사용합니다.")]
    public string nodeId;
    public GameObject visualPrefab;
}

/// <summary>지도 노드에 맞는 전용 전투 무대를 지정한다. 전투 규칙은 공통으로 유지한다.</summary>
[Serializable]
public class NodeBattleScene
{
    public string nodeId;
    [Tooltip("Build Settings에 포함된 씬 이름. 비우면 공통 GameScene 사용.")]
    public string sceneName;
}

/// <summary>전투 한 판의 설정. enemyPrefab이 없으면 씬의 기본 적을 사용한다.</summary>
[Serializable]
public class StageDefinition
{
    [Tooltip("고유 키. 지도 노드가 이 값으로 스테이지를 찾는다.")]
    public string id = "stage_01";
    public string displayName = "이름 없는 적";

    [Tooltip("이 스테이지가 배치될 칸 종류.")]
    public NodeType nodeType = NodeType.Battle;
    [Tooltip("이 층 이상에서만 등장한다. 0 이면 제한 없음.")]
    public int minRow = 0;

    [Header("전투 설정 (0 이면 GameScene 기본값 유지)")]
    [Tooltip("적의 최대 체력. 0 이면 씬에 설정된 값을 그대로 쓴다.")]
    public int enemyMaxHealth = 0;
    [Tooltip("이 스테이지 적의 행동 타입.")]
    public EnemyBehaviorType behaviorType = EnemyBehaviorType.Balanced;

    [Header("단계별 전투 조정 (켜면 아래 수치를 적용)")]
    public bool overrideCombatStats;
    [Min(1)] public int attackDamage = 8;
    [Min(0.1f)] public float moveSpeed = 2.6f;
    [Min(0.1f)] public float decisionInterval = 0.8f;
    [Min(0f)] public float attackWeight = 5f;
    [Min(0f)] public float blockWeight = 3f;
    [Min(0f)] public float backstepWeight = 1.5f;
    [Min(0f)] public float crouchWeight = 1f;
    public bool overrideTint;
    public Color enemyTint = Color.white;
    [Tooltip("비워두면 적의 기존 애니메이터를 사용합니다.")]
    public RuntimeAnimatorController animatorController;

    [Header("연출용 (나중에 채우면 됨)")]
    public Sprite portrait;
    public Sprite battleBackground;
    [TextArea] public string introText;

    [Tooltip("비워두면 GameScene 에 이미 있는 Enemy 를 그대로 쓴다. 캐릭터가 만들어지면 여기에 연결한다.")]
    public GameObject enemyPrefab;
}

/// <summary>
/// 한 판(run) 전체 설정. Resources 폴더에 두면 RunState 가 자동으로 읽어간다.
/// 만드는 법: Project 창 우클릭 → Create → VisionStriker → Run Config
/// 반드시 Assets/Resources/RunConfig.asset 경로여야 자동으로 잡힌다.
/// </summary>
/// <summary>주인공 이동 방식(HeroAuthoredMovement).</summary>
public enum HeroMovementStyle
{
    /// <summary>승인 걷기(스텝)만 반복. 뒤로는 같은 클립을 거꾸로 재생.</summary>
    ApprovedWalk,
    /// <summary>대시 → 이어 달리기 → 스텝, 뒤로는 백대시.</summary>
    DashAndStep,
    /// <summary>적과 같은 걷기(WALK_FORWARD / WALK_BACK).</summary>
    CommonWalk,
}

[CreateAssetMenu(menuName = "VisionStriker/Run Config", fileName = "RunConfig")]
public class RunConfig : ScriptableObject
{
    /// <summary>Resources 안에서 찾을 파일 이름.</summary>
    public const string ResourcePath = "RunConfig";

    [Header("지도 생성")]
    public MapSettings map = new MapSettings();
    [Tooltip("사부의 행방을 추적하는 탐험 지도. 새 게임부터 적용됩니다.")]
    public bool useStoryMap = true;

    [Header("분기형 보스 지도")]
    [Tooltip("켜면 보스 순서와 휴식 시점이 다른 경로를 만듭니다. 목록의 마지막은 최종 보스, 나머지는 각 경로에서 한 번씩 만납니다.")]
    public bool useOrderedCampaign;
    public List<string> campaignStageIds = new List<string>();
    [Tooltip("지도 경로 수. 전투 횟수는 보스 목록의 개수로 결정됩니다.")]
    [Range(2, 3)] public int campaignBranches = 3;

    [Header("주인공 이동")]
    [Tooltip("ApprovedWalk: 승준의 승인 걷기(스텝)로 앞뒤 이동(기본). DashAndStep: 대시 → 이어 달리기 → 스텝, 뒤로는 백대시. CommonWalk: 적과 같은 걷기.")]
    // Keep the field-validated camera stride. Authored travel remains an optional developer setting.
    public HeroMovementStyle heroMovement = HeroMovementStyle.CommonWalk;
    [Tooltip("ApprovedWalk 일 때 걷기 빠르기 배율. 1 = 클립 그대로(초당 약 0.38m). 재생과 이동을 함께 올려 발이 미끄러지지 않는다. Play 중에 바꿔도 바로 반영된다.")]
    [Range(1f, 3f)] public float heroWalkSpeed = 2.61f;   // 초당 약 1.0m

    [Header("엔딩 크레딧")]
    public string endingTitle = "흑운협";
    [Tooltip("엔딩 크레딧에서 반복 재생할 음악. 비어 있으면 무음.")]
    public AudioClip endingMusic;
    [Tooltip("마지막 제목 화면 아래에 작게 적는 저작권 표시. 비우면 표시하지 않는다.")]
    public string creditsCopyright = "© 2026 팀 RedBool";
    [Tooltip("줄 머리: [logo] 제목 그림, # 머리말, ## 이름, = 직함, - 맡은 일, --- 구분 장식. 그 밖은 일반 문장.")]
    [TextArea(6, 30)] public string creditsText = "# 제작\n## 팀 RedBool";
    [Tooltip("음악, 사운드, 폰트, 외부 에셋의 제작자와 출처/라이선스를 입력합니다. 비어 있으면 표시하지 않습니다.")]
    [TextArea(3, 12)] public string creditsAttributions = "";
    [Min(5f)] public float creditsDuration = 32f;

    [Header("플레이어")]
    public int playerMaxHealth = 100;
    [Tooltip("전투가 끝나도 체력이 이어진다. 끄면 매 전투 만피로 시작.")]
    public bool carryPlayerHealth = true;
    [Tooltip("휴식 칸에서 회복하는 비율 (0.3 = 최대 체력의 30%).")]
    [Range(0f, 1f)] public float restHealPercent = 0.3f;

    [Header("스테이지 목록")]
    public List<StageDefinition> stages = new List<StageDefinition>();

    [Header("지도 노드별 적 외형")]
    public List<NodeEnemyVisual> nodeEnemyVisuals = new List<NodeEnemyVisual>();

    [Header("지도 노드별 전투 무대")]
    public List<NodeBattleScene> nodeBattleScenes = new List<NodeBattleScene>();

    public string GetBattleScene(string nodeId)
    {
        if (!string.IsNullOrEmpty(nodeId) && nodeBattleScenes != null)
            foreach (var entry in nodeBattleScenes)
                if (entry != null && entry.nodeId == nodeId && !string.IsNullOrWhiteSpace(entry.sceneName))
                    return entry.sceneName;
        return SceneRoutes.Battle;
    }

    public GameObject GetEnemyVisual(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId) || nodeEnemyVisuals == null) return null;
        foreach (var entry in nodeEnemyVisuals)
            if (entry != null && entry.nodeId == nodeId) return entry.visualPrefab;
        return null;
    }

    public StageDefinition Get(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        for (int i = 0; i < stages.Count; i++)
            if (stages[i] != null && stages[i].id == id) return stages[i];
        return null;
    }

    /// <summary>
    /// 해당 칸 종류/층에 어울리는 스테이지를 하나 고른다.
    /// 후보가 없으면 null 을 돌려주고, 그 칸은 GameScene 기본 적으로 싸운다.
    /// </summary>
    public StageDefinition Pick(NodeType type, int row, System.Random rng)
    {
        var pool = new List<StageDefinition>();
        for (int i = 0; i < stages.Count; i++)
        {
            var s = stages[i];
            if (s == null) continue;
            if (s.nodeType != type) continue;
            if (row < s.minRow) continue;
            pool.Add(s);
        }
        if (pool.Count == 0) return null;
        return pool[rng.Next(pool.Count)];
    }
}
