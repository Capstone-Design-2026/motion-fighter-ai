using UnityEngine;
using UnityEngine.UI;

/// <summary>밝은 배경에서도 이름과 타이머가 읽히도록 위쪽에만 얇은 그늘을 둔다.</summary>
[AddComponentMenu("")]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class WuxiaHUDShade : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper v)
    {
        v.Clear(); Rect r=rectTransform.rect;
        v.AddVert(new Vector2(r.xMin,r.yMin),new Color(0,0,0,0),Vector2.zero);
        v.AddVert(new Vector2(r.xMax,r.yMin),new Color(0,0,0,0),Vector2.zero);
        v.AddVert(new Vector2(r.xMax,r.yMax),new Color(.025f,.028f,.026f,.72f),Vector2.zero);
        v.AddVert(new Vector2(r.xMin,r.yMax),new Color(.025f,.028f,.026f,.72f),Vector2.zero);
        v.AddTriangle(0,1,2);v.AddTriangle(0,2,3);
    }
}
