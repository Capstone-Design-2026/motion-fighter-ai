using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Permanent explored territories with GPU-animated mist. Does not change discovery or travel.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public class StoryMapFog : MaskableGraphic
{
    public readonly List<Vector2> revealed = new List<Vector2>();
    public readonly List<Vector2> routePoints = new List<Vector2>();
    public float radius = 170f;
    public float routeRadius = 220f;
    public bool ambientOnly;
    public float driftSpeed = 22f;

    struct Territory { public Vector2 point; public bool cleared; }
    struct Corridor { public Vector2 point; public float radius; }
    readonly List<Territory> territories = new List<Territory>();
    readonly List<Corridor> corridors = new List<Corridor>();
    bool fullyExplored;
    Material ownedMaterial;

    protected override void OnEnable()
    {
        base.OnEnable();
        var shader = Resources.Load<Shader>("UI/Wuxia/LivingMapFog");
        if (shader != null) { ownedMaterial = new Material(shader); material = ownedMaterial; }
        raycastTarget = false;
    }

    /// <summary>Called once per map rebuild. Uses existing saved completion, never writes it.</summary>
    public void Configure(RunData data, Dictionary<string, Vector2> positions)
    {
        revealed.Clear(); routePoints.Clear(); territories.Clear(); corridors.Clear();
        fullyExplored = data.finished;
        bool all = data.map.nodes.Count > 0;
        foreach (var node in data.map.nodes)
        {
            if (!positions.TryGetValue(node.id, out var point)) continue;
            bool done = data.clearedNodeIds.Contains(node.id);
            all &= done;
            territories.Add(new Territory { point = point, cleared = done });
            if (data.discoveredNodeIds.Contains(node.id)) revealed.Add(point);
            if (!done) continue;
            foreach (var nextId in node.next)
            {
                if (!positions.TryGetValue(nextId, out var next) || !data.discoveredNodeIds.Contains(nextId)) continue;
                bool nextDone = data.clearedNodeIds.Contains(nextId);
                // The approach opens with the completed place; the next place's far side stays concealed.
                float end = nextDone ? 1f : .86f;
                int steps = Mathf.Max(3, Mathf.CeilToInt(Vector2.Distance(point, next) / 45f));
                for (int i = 0; i <= steps; i++)
                {
                    float t = end * i / steps;
                    corridors.Add(new Corridor { point = StoryMapLayout.Point(point, next, t),
                        radius = Mathf.Lerp(280f, nextDone ? 280f : 150f, t) });
                }
            }
        }
        fullyExplored |= all;
        SetVerticesDirty();
    }

    /// <summary>Stable reveal strength: 0 hidden, 1 clear. Animation never re-covers cleared land.</summary>
    public float RevealAt(Vector2 point)
    {
        if (fullyExplored) return 1f;
        float reveal = 0f, doneDistance = float.MaxValue, unknownDistance = float.MaxValue;
        foreach (var site in territories)
        {
            float d = Vector2.Distance(point, site.point);
            if (site.cleared) doneDistance = Mathf.Min(doneDistance, d);
            else unknownDistance = Mathf.Min(unknownDistance, d);
        }
        if (doneDistance < float.MaxValue)
        {
            // Neighboring stages divide the painting into territories, including land off the route.
            reveal = unknownDistance == float.MaxValue ? 1f :
                Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-35f, 120f, unknownDistance - doneDistance));
            reveal = Mathf.Max(reveal, Disk(point, Vector2.zero, 0f, doneDistance, 290f));
        }
        foreach (var p in revealed) reveal = Mathf.Max(reveal, Disk(point, p, radius));
        foreach (var p in routePoints) reveal = Mathf.Max(reveal, Disk(point, p, routeRadius));
        foreach (var c in corridors) reveal = Mathf.Max(reveal, Disk(point, c.point, c.radius));
        return reveal;
    }

    static float Disk(Vector2 point, Vector2 center, float size, float distance = -1f, float overrideSize = -1f)
    {
        if (distance < 0f) distance = Vector2.Distance(point, center);
        if (overrideSize >= 0f) size = overrideSize;
        return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(size - 65f, size + 100f, distance));
    }

    void Update()
    {
        if (ownedMaterial == null) return;
        // Updating shader uniforms avoids rebuilding thousands of vertices every animation frame.
        var rendered = materialForRendering;
        rendered.SetFloat("_FlowTime", Time.unscaledTime);
        rendered.SetFloat("_DriftSpeed", driftSpeed);
        rendered.SetFloat("_Ambient", ambientOnly ? 1f : 0f);
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear(); var r = rectTransform.rect;
        if (r.width <= 0 || r.height <= 0) return;
        float cell = Mathf.Max(32f, Mathf.Sqrt(r.width * r.height / 14000f));
        int nx = Mathf.CeilToInt(r.width / cell), ny = Mathf.CeilToInt(r.height / cell);
        for (int y = 0; y <= ny; y++) for (int x = 0; x <= nx; x++)
        {
            Vector2 point = new Vector2(r.xMin + r.width * x / nx, r.yMin + r.height * y / ny);
            // Use normalized map coordinates so decorative and exploration layers share the same drift.
            var c = Color.white; c.a = ambientOnly ? 1f : 1f - RevealAt(point);
            vh.AddVert(point, c, new Vector2((float)x / nx, (float)y / ny));
        }
        for (int y = 0; y < ny; y++) for (int x = 0; x < nx; x++)
        {
            int i = y * (nx + 1) + x;
            vh.AddTriangle(i, i + nx + 1, i + 1); vh.AddTriangle(i + 1, i + nx + 1, i + nx + 2);
        }
    }

    protected override void OnDisable()
    {
        material = null;
        if (ownedMaterial != null) { Destroy(ownedMaterial); ownedMaterial = null; }
        base.OnDisable();
    }
}
