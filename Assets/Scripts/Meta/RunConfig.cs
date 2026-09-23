using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 스테이지(=전투 한 판) 정의. 보스마다 다른 값을 넣기 위한 껍데기다.
/// 아직 적 프리팹이 없으므로 enemyPrefab 을 비워두면 GameScene 에 이미 배치된 Enemy 를 그대로 쓴다.
/// </summary>
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
[CreateAssetMenu(menuName = "VisionStriker/Run Config", fileName = "RunConfig")]
public class RunConfig : ScriptableObject
{
    /// <summary>Resources 안에서 찾을 파일 이름.</summary>
    public const string ResourcePath = "RunConfig";

    [Header("지도 생성")]
    public MapSettings map = new MapSettings();

    [Header("분기형 보스 지도")]
    [Tooltip("켜면 보스 순서와 휴식 시점이 다른 경로를 만듭니다. 목록의 마지막은 최종 보스, 나머지는 각 경로에서 한 번씩 만납니다.")]
    public bool useOrderedCampaign;
    public List<string> campaignStageIds = new List<string>();
    [Tooltip("지도 경로 수. 전투 횟수는 보스 목록의 개수로 결정됩니다.")]
    [Range(2, 3)] public int campaignBranches = 3;

    [Header("엔딩 크레딧")]
    public string endingTitle = "VISION STRIKER";
    [TextArea(6, 20)] public string creditsText = "제작진";
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
