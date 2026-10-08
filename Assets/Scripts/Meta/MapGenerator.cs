using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Slay the Spire 식 지도 생성기.
/// 아래에서 위로 층을 쌓고, 각 칸을 윗층 칸에 연결한다.
/// 연결은 "왼쪽 칸이 오른쪽 칸보다 왼쪽으로만 간다"는 규칙(단조성)을 지켜서
/// 선이 서로 교차하지 않게 만든다.
/// </summary>
public static class MapGenerator
{
    public static MapData Generate(MapSettings s, RunConfig config, int seed)
    {
        if (config != null && config.useStoryMap) return StoryMap.Create();
        if (config != null && config.useOrderedCampaign)
            return GenerateCampaign(config, seed);
        if (s == null) s = new MapSettings();
        var rng = new System.Random(seed);

        int rows    = Mathf.Max(3, s.rows);
        int columns = Mathf.Max(1, s.columns);

        var map = new MapData { rows = rows, columns = columns };
        var grid = new List<List<MapNode>>();

        // ── 1. 층마다 칸을 배치한다 ──
        for (int r = 0; r < rows; r++)
        {
            int count;
            if      (r == 0)          count = s.entryNodes;          // 시작 갈래
            else if (r == rows - 1)   count = 1;                     // 보스는 항상 하나
            else if (r == rows - 2)   count = 2;                     // 보스 직전은 휴식 줄
            else                      count = rng.Next(s.minNodesPerRow, s.maxNodesPerRow + 1);
            count = Mathf.Clamp(count, 1, columns);

            var cols = PickColumns(rng, columns, count);
            var line = new List<MapNode>();
            for (int i = 0; i < count; i++)
            {
                var node = new MapNode
                {
                    id     = string.Format("r{0:00}n{1:00}", r, i),
                    row    = r,
                    column = cols[i],
                    type   = PickType(rng, r, rows, s),
                };
                line.Add(node);
                map.nodes.Add(node);
            }
            grid.Add(line);
        }

        Connect(rng, grid, s);
        AssignStages(map, config, rng);
        return map;
    }

    // 각 경로는 모든 일반 보스를 한 번씩 + 휴식 두 번 + 최종 보스를 지난다.
    // 같은 층에서 처치한 보스 집합/휴식 횟수/현재 칸이 같은 경로만 합친다.
    // 따라서 합류 후 다른 길을 골라도 보스 중복이나 전투 건너뛰기가 없다.
    private static MapData GenerateCampaign(RunConfig config, int seed)
    {
        var ids = config.campaignStageIds;
        if (ids == null || ids.Count == 0)
            throw new System.InvalidOperationException("RunConfig: 보스 목록을 지정해야 합니다.");
        var seen = new HashSet<string>();
        foreach (var id in ids)
            if (config.Get(id) == null || !seen.Add(id))
                throw new System.InvalidOperationException("RunConfig: 없거나 중복된 보스 ID: " + id);

        int lanes = Mathf.Clamp(config.campaignBranches, 2, 3);
        int ordinary = ids.Count - 1;
        int restCount = Mathf.Min(2, ordinary);
        int rows = ids.Count + restCount;
        var rng = new System.Random(seed);
        var baseOrder = new List<int>();
        for (int i = 0; i < ordinary; i++) baseOrder.Add(i);
        // 초반/후반의 큰 난이도 구간은 유지하면서 구간 안의 순서를 바꾼다.
        if (ordinary >= 2 && rng.Next(2) == 0) Swap(baseOrder, 0, 1);
        if (ordinary >= 4 && rng.Next(2) == 0) Swap(baseOrder, ordinary - 2, ordinary - 1);

        var paths = new List<List<MapNode>>();
        var signatures = new List<List<string>>();
        for (int lane = 0; lane < lanes; lane++)
        {
            var order = new List<int>(baseOrder);
            if (lane == 1 && ordinary >= 2) Swap(order, ordinary - 2, ordinary - 1);
            if (lane == 2 && ordinary >= 2) Swap(order, 0, 1);
            var rests = new HashSet<int>();
            if (ordinary > 0)
            {
                rests.Add(Mathf.Min(lane == 0 ? 1 : 2, ordinary));
                if (restCount > 1) rests.Add(Mathf.Max(1, lane == 2 ? ordinary : ordinary - 1));
                for (int i = ordinary; rests.Count < restCount; i--) rests.Add(i);
            }
            var path = new List<MapNode>();
            var keys = new List<string>();
            var defeated = new List<int>();
            int rested = 0;
            for (int fight = 0; fight < ordinary; fight++)
            {
                int boss = order[fight];
                defeated.Add(boss);
                defeated.Sort();
                path.Add(new MapNode { type = NodeType.Battle, stageId = ids[boss], battleNumber = fight + 1 });
                keys.Add(string.Join(",", defeated) + "|" + rested + "|battle|" + boss);
                if (rests.Contains(fight + 1))
                {
                    rested++;
                    path.Add(new MapNode { type = NodeType.Rest, stageId = "" });
                    keys.Add(string.Join(",", defeated) + "|" + rested + "|rest");
                }
            }
            path.Add(new MapNode { type = NodeType.Boss, stageId = ids[ordinary], battleNumber = ids.Count });
            keys.Add("final");
            paths.Add(path);
            signatures.Add(keys);
        }
        // 좌우 의미는 고정하지 않되, 같은 seed에서는 같은 지도를 재현한다.
        if (rng.Next(2) == 0) { paths.Reverse(); signatures.Reverse(); }
        var map = new MapData { rows = rows, columns = lanes * 2 - 1 };
        var previous = new MapNode[lanes];
        for (int row = 0; row < rows; row++)
        {
            var current = new MapNode[lanes];
            for (int lane = 0; lane < lanes;)
            {
                int end = lane;
                while (end + 1 < lanes && signatures[end + 1][row] == signatures[lane][row]) end++;
                var node = paths[lane][row];
                node.id = "route_r" + row + "_l" + lane;
                node.row = row;
                node.column = lane + end;
                map.nodes.Add(node);
                for (int i = lane; i <= end; i++) current[i] = node;
                lane = end + 1;
            }
            if (row > 0)
                for (int lane = 0; lane < lanes; lane++)
                    if (!previous[lane].next.Contains(current[lane].id))
                        previous[lane].next.Add(current[lane].id);
            previous = current;
        }
        return map;
    }

    private static void Swap(List<int> items, int a, int b)
    {
        int temp = items[a]; items[a] = items[b]; items[b] = temp;
    }

    /// <summary>가로 자리 중 count 개를 겹치지 않게 골라 오름차순으로 돌려준다.</summary>
    private static List<int> PickColumns(System.Random rng, int columns, int count)
    {
        var all = new List<int>();
        for (int c = 0; c < columns; c++) all.Add(c);

        // 피셔-예이츠 셔플 후 앞에서 count 개
        for (int i = all.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            int tmp = all[i]; all[i] = all[j]; all[j] = tmp;
        }
        var picked = all.GetRange(0, Mathf.Min(count, all.Count));
        picked.Sort();
        return picked;
    }

    private static NodeType PickType(System.Random rng, int row, int rows, MapSettings s)
    {
        if (row == 0)        return NodeType.Battle;   // 첫 줄은 무조건 전투
        if (row == rows - 1) return NodeType.Boss;
        if (row == rows - 2) return NodeType.Rest;     // 보스 전 회복 기회
        if (row == 1)        return NodeType.Battle;

        float wElite = row >= s.eliteMinRow ? s.eliteWeight : 0f;
        float total  = s.battleWeight + wElite + s.restWeight + s.eventWeight + s.shopWeight;
        if (total <= 0f) return NodeType.Battle;

        float roll = (float)rng.NextDouble() * total;
        if ((roll -= s.battleWeight) < 0f) return NodeType.Battle;
        if ((roll -= wElite)         < 0f) return NodeType.Elite;
        if ((roll -= s.restWeight)   < 0f) return NodeType.Rest;
        if ((roll -= s.eventWeight)  < 0f) return NodeType.Event;
        return NodeType.Shop;
    }

    /// <summary>
    /// 아래층 칸을 윗층 칸에 잇는다.
    /// (1) 아래층 i 번째 칸은 윗층의 대응 위치 map[i] 에 반드시 연결한다. map 은 증가만 하므로 교차가 없다.
    /// (2) 확률적으로 map[i]+1 에도 이어서 갈래를 만든다. 단 다음 칸의 대응 위치를 넘지 않는다.
    /// (3) 부모가 하나도 없는 윗층 칸은 가장 가까운 아래층 칸에 붙여서 고립을 막는다.
    /// </summary>
    private static void Connect(System.Random rng, List<List<MapNode>> grid, MapSettings s)
    {
        for (int r = 0; r + 1 < grid.Count; r++)
        {
            var cur = grid[r];
            var nxt = grid[r + 1];
            if (cur.Count == 0 || nxt.Count == 0) continue;

            var slot = new int[cur.Count];
            for (int i = 0; i < cur.Count; i++)
            {
                slot[i] = cur.Count == 1
                    ? (nxt.Count - 1) / 2
                    : Mathf.RoundToInt(i * (nxt.Count - 1) / (float)(cur.Count - 1));
                slot[i] = Mathf.Clamp(slot[i], 0, nxt.Count - 1);
                cur[i].next.Add(nxt[slot[i]].id);
            }

            for (int i = 0; i < cur.Count; i++)
            {
                if (rng.NextDouble() > s.branchChance) continue;

                int upper = (i + 1 < cur.Count) ? slot[i + 1] : nxt.Count - 1;
                int cand  = slot[i] + 1;
                if (cand > upper || cand >= nxt.Count) continue;
                if (cur[i].next.Contains(nxt[cand].id)) continue;
                cur[i].next.Add(nxt[cand].id);
            }

            for (int j = 0; j < nxt.Count; j++)
            {
                bool hasParent = false;
                for (int i = 0; i < cur.Count && !hasParent; i++)
                    hasParent = cur[i].next.Contains(nxt[j].id);
                if (hasParent) continue;

                int best = 0, bestDist = int.MaxValue;
                for (int i = 0; i < cur.Count; i++)
                {
                    int d = Mathf.Abs(slot[i] - j);
                    if (d < bestDist) { bestDist = d; best = i; }
                }
                cur[best].next.Add(nxt[j].id);
            }
        }
    }

    /// <summary>
    /// 전투 계열 칸에 스테이지를 배정한다. 후보가 없으면 빈 문자열로 남기고,
    /// 그 칸은 GameScene 에 이미 있는 기본 적과 싸운다.
    /// 같은 적만 계속 나오지 않도록 "지금까지 적게 쓰인 스테이지"를 먼저 고른다.
    /// </summary>
    private static void AssignStages(MapData map, RunConfig config, System.Random rng)
    {
        if (config == null) return;

        var used = new Dictionary<string, int>();

        for (int i = 0; i < map.nodes.Count; i++)
        {
            var node = map.nodes[i];
            if (!node.IsBattle) continue;

            var pool = new List<StageDefinition>();
            for (int k = 0; k < config.stages.Count; k++)
            {
                var st = config.stages[k];
                if (st == null) continue;
                if (st.nodeType != node.type) continue;
                if (node.row < st.minRow) continue;
                pool.Add(st);
            }

            if (pool.Count == 0) { node.stageId = ""; continue; }

            int fewest = int.MaxValue;
            for (int k = 0; k < pool.Count; k++)
            {
                int c;
                if (!used.TryGetValue(pool[k].id, out c)) c = 0;
                if (c < fewest) fewest = c;
            }

            var candidates = new List<StageDefinition>();
            for (int k = 0; k < pool.Count; k++)
            {
                int c;
                if (!used.TryGetValue(pool[k].id, out c)) c = 0;
                if (c == fewest) candidates.Add(pool[k]);
            }

            var picked = candidates[rng.Next(candidates.Count)];
            node.stageId = picked.id;
            used[picked.id] = fewest + 1;
        }
    }
}
