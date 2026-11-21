using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Shared brush lettering for battle openings and results; regular HUD text stays unchanged.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Text))]
public sealed class CombatAnnouncementStyle : BaseMeshEffect
{
    public enum Palette { Ready, Fight, Victory, Defeat }

    private static Font announcementFont;
    [SerializeField] private Palette palette;
    private readonly List<UIVertex> source = new List<UIVertex>();
    private readonly List<UIVertex> decorated = new List<UIVertex>();
    private static readonly Vector2[] strokeDirections =
    {
        Vector2.up, Vector2.down, Vector2.left, Vector2.right,
        new Vector2(.7071f, .7071f), new Vector2(-.7071f, .7071f),
        new Vector2(.7071f, -.7071f), new Vector2(-.7071f, -.7071f)
    };

    public static void Apply(Text text, Palette colors)
    {
        if (announcementFont == null)
            announcementFont = Resources.Load<Font>("UI/Fonts/TradeWinds-Regular");
        text.font = announcementFont != null ? announcementFont : WuxiaTheme.DisplayFont;
        text.fontStyle = FontStyle.Normal;
        text.color = Color.white;
        text.supportRichText = false;
        text.raycastTarget = false;
        var effect = text.GetComponent<CombatAnnouncementStyle>();
        if (effect == null) effect = text.gameObject.AddComponent<CombatAnnouncementStyle>();
        effect.palette = colors;
        text.SetVerticesDirty();
    }

    public override void ModifyMesh(VertexHelper mesh)
    {
        if (!IsActive() || mesh.currentVertCount == 0) return;
        source.Clear();
        mesh.GetUIVertexStream(source);
        if (source.Count == 0) return;

        float bottom = float.MaxValue, top = float.MinValue;
        foreach (var vertex in source)
        {
            bottom = Mathf.Min(bottom, vertex.position.y);
            top = Mathf.Max(top, vertex.position.y);
        }
        float height = Mathf.Max(1f, top - bottom);
        float middle = (top + bottom) * .5f;
        GetColors(out var upper, out var lower);
        for (int i = 0; i < source.Count; i++)
        {
            var vertex = source[i];
            var tint = Color.Lerp(lower, upper, (vertex.position.y - bottom) / height);
            vertex.color = (Color)vertex.color * tint;
            // Lean the entire word forward without distorting its spacing or changing the font file.
            vertex.position.x += (vertex.position.y - middle) * .16f;
            source[i] = vertex;
        }

        float scale = Mathf.Clamp(height / 150f, .45f, 1.6f);
        decorated.Clear();
        // Build each stroke from the original glyphs, avoiding exponential copies from stacked Outlines.
        AppendCopy(new Vector2(6f, -9f) * scale, new Color(.025f, .008f, .005f, .85f));
        AppendStroke(5.5f * scale, new Color(1f, .96f, .79f));
        AppendStroke(2.4f * scale, new Color(.18f, .035f, .018f));
        decorated.AddRange(source);
        mesh.Clear();
        mesh.AddUIVertexTriangleStream(decorated);
    }

    private void AppendStroke(float radius, Color color)
    {
        foreach (var direction in strokeDirections) AppendCopy(direction * radius, color);
    }

    private void AppendCopy(Vector2 offset, Color color)
    {
        foreach (var original in source)
        {
            var vertex = original;
            vertex.position += new Vector3(offset.x, offset.y, 0f);
            var tint = color;
            tint.a *= original.color.a / 255f;
            vertex.color = tint;
            decorated.Add(vertex);
        }
    }

    private void GetColors(out Color upper, out Color lower)
    {
        switch (palette)
        {
            case Palette.Ready:
                upper = new Color(1f, 1f, .98f);
                lower = new Color(.64f, .76f, .84f);
                break;
            case Palette.Defeat:
                upper = new Color(.78f, .035f, .055f);
                lower = new Color(1f, .37f, .23f);
                break;
            default:
                upper = new Color(.94f, .055f, .015f);
                lower = new Color(1f, .86f, .12f);
                break;
        }
    }
}
