using UnityEngine;
using UnityEngine.UI;

/// <summary>Subtle 2D mouth aperture, not phoneme/viseme synthesis. Original lips remain visible in silence.</summary>
public class StoryMouth : MaskableGraphic
{
    float amount, width;
    public static StoryMouth Create(Transform parent)
    {
        var go = new GameObject("SpeechMouth", typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(parent, false);
        var mouth = go.AddComponent<StoryMouth>(); mouth.raycastTarget = false;
        return mouth;
    }
    public void Pose(Vector2 position, float mouthWidth, float opening, float angle = 0)
    {
        amount = Mathf.Clamp01(opening); width = mouthWidth;
        rectTransform.anchorMin = rectTransform.anchorMax = new Vector2(.5f, .5f);
        rectTransform.pivot = new Vector2(.5f, .5f);
        rectTransform.anchoredPosition = position;
        rectTransform.localRotation = Quaternion.Euler(0, 0, angle);
        rectTransform.sizeDelta = new Vector2(width, width);
        enabled = amount > .065f && width > 0;
        SetVerticesDirty();
    }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear(); if (amount <= .065f) return;
        float h = width * .28f * amount;
        Ellipse(vh, Vector2.zero, new Vector2(width * .48f, h * .5f), new Color(.12f, .055f, .045f, .88f));
        // A tiny warm highlight reads as a lower lip, avoiding bright cartoon teeth.
        Ellipse(vh, new Vector2(0, -h * .35f), new Vector2(width * .28f, h * .14f), new Color(.40f, .20f, .16f, .65f));
    }
    static void Ellipse(VertexHelper vh, Vector2 center, Vector2 radius, Color tint)
    {
        int first = vh.currentVertCount;
        vh.AddVert(center, tint, Vector2.zero);
        const int count = 20;
        for (int i = 0; i <= count; i++)
        {
            float a = i * Mathf.PI * 2 / count;
            vh.AddVert(center + new Vector2(Mathf.Cos(a)*radius.x, Mathf.Sin(a)*radius.y), tint, Vector2.zero);
            if (i > 0) vh.AddTriangle(first, first+i, first+i+1);
        }
    }
}
