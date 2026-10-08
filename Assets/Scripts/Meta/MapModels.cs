using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>지도에 찍히는 칸의 종류. 지금은 전투 계열만 실제로 동작한다.</summary>
public enum NodeType
{
    Battle = 0,   // 일반 전투
    Elite  = 1,   // 강적
    Boss   = 2,   // 스테이지 보스
    Rest   = 3,   // 휴식 (체력 회복)
    Event  = 4,   // 이벤트 (미구현 - 지금은 그냥 통과)
    Shop   = 5,   // 상점 (미구현 - 지금은 그냥 통과)
}

/// <summary>지도에서 그 칸을 지금 고를 수 있는지.</summary>
public enum NodeStatus
{
    Locked    = 0,   // 아직 못 감
    Available = 1,   // 지금 고를 수 있음
    Cleared   = 2,   // 이미 지나감
}

/// <summary>
/// 지도의 칸 하나. Slay the Spire 처럼 아래에서 위로 올라가며,
/// 각 칸은 윗줄의 칸 1~2개로 이어진다.
/// </summary>
[Serializable]
public class MapNode
{
    public string title;
    public string story;
    public bool deadEnd;
    public string id;                 // "r03n01" 형태. 저장/참조용 고유 키
    public int row;                   // 0 = 맨 아래(시작), rows-1 = 보스
    public int column;                // 가로 위치 (0 ~ columns-1)
    public int battleNumber;          // 보스 전투 순번. 휴식/이전 저장 지도는 0.
    public NodeType type;
    public string stageId;            // RunConfig 의 스테이지 id. 비어 있으면 GameScene 기본 적 사용
    public List<string> next = new List<string>();   // 윗줄로 이어지는 칸들

    [NonSerialized] public NodeStatus status;

    public bool IsBattle
    {
        get { return type == NodeType.Battle || type == NodeType.Elite || type == NodeType.Boss; }
    }
}

/// <summary>지도 한 판 전체.</summary>
[Serializable]
public class MapData
{
    public int storyVersion;
    public int rows;
    public int columns;
    public List<MapNode> nodes = new List<MapNode>();

    public MapNode Find(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        for (int i = 0; i < nodes.Count; i++)
            if (nodes[i].id == id) return nodes[i];
        return null;
    }

    public List<MapNode> RowNodes(int row)
    {
        var list = new List<MapNode>();
        for (int i = 0; i < nodes.Count; i++)
            if (nodes[i].row == row) list.Add(nodes[i]);
        return list;
    }
}

/// <summary>
/// 지도 생성 규칙. RunConfig 에셋에서 인스펙터로 조절한다.
/// 숫자만 바꿔도 지도 모양이 완전히 달라지므로 밸런스 조정은 여기서 한다.
/// </summary>
[Serializable]
public class MapSettings
{
    [Header("크기")]
    [Tooltip("층 수. 맨 아래(0층)가 시작, 맨 위가 보스.")]
    public int rows = 12;
    [Tooltip("가로로 놓을 수 있는 칸 자리 수.")]
    public int columns = 6;
    [Tooltip("0층에 놓을 시작 칸 개수.")]
    public int entryNodes = 3;
    public int minNodesPerRow = 2;
    public int maxNodesPerRow = 4;

    [Header("갈래")]
    [Tooltip("한 칸에서 위로 두 갈래가 갈릴 확률.")]
    [Range(0f, 1f)] public float branchChance = 0.45f;

    [Header("칸 종류 가중치 (중간 층에만 적용)")]
    public float battleWeight = 55f;
    public float eliteWeight  = 15f;
    public float restWeight   = 10f;
    public float eventWeight  = 12f;
    public float shopWeight   =  8f;
    [Tooltip("이 층 아래로는 강적이 나오지 않는다.")]
    public int eliteMinRow = 3;
}
