using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 캐릭터 머리 위에 붙는 화면 표시(AttackWarning, OpeningIndicator)의 공용 도구.
///
/// 화면 위 UI(Screen Space Overlay)로 그리므로 2D/3D 카메라와 상관없이 항상 정면을 보고,
/// 배경이나 다른 캐릭터에 가려지지 않는다. 위치는 매 프레임 캐릭터 렌더러의 머리 꼭대기를
/// 화면 좌표로 바꿔 따라간다.
/// </summary>
public static class OverheadUI
{
    /// <summary>
    /// 머리 위 표시용 캔버스. 캐릭터 밑에 두지 않는다 —
    /// 캐릭터가 뒤집히거나 크기가 바뀌어도 UI 가 따라 변하지 않게 하려는 것.
    /// </summary>
    public static Canvas CreateCanvas(string name, int sortingOrder)
    {
        var canvas = UIFactory.CreateCanvas(name, null, sortingOrder);
        Object.Destroy(canvas.GetComponent<GraphicRaycaster>());   // 클릭을 가로채지 않게
        return canvas;
    }

    /// <summary>캐릭터의 렌더러. 3D 모델을 갈아끼울 수 있으니(Fighter3DView.ApplyVisual) 표시할 때마다 다시 부른다.</summary>
    public static Renderer[] BodyRenderers(Component owner)
    {
        return owner.GetComponentsInChildren<Renderer>();
    }

    /// <summary>캐릭터 머리 꼭대기(월드 좌표). 3D 모델이면 메시, 2D 면 스프라이트 범위, 둘 다 없으면 콜라이더를 쓴다.</summary>
    public static Vector3 HeadTop(Component owner, Renderer[] renderers)
    {
        bool found = false;
        Bounds bounds = default;
        if (renderers != null)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                // 이펙트는 머리 높이 계산에서 뺀다.
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;

                if (!found) { bounds = r.bounds; found = true; }
                else bounds.Encapsulate(r.bounds);
            }
        }

        if (!found)
        {
            var body = owner.GetComponent<Collider2D>();
            if (body != null) { bounds = body.bounds; found = true; }
        }

        if (!found) return owner.transform.position + Vector3.up * 2f;
        return new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
    }

    /// <summary>mark 를 world 지점 위 screenOffset(기준 해상도 픽셀)에 둔다. 카메라가 없거나 카메라 뒤면 false.</summary>
    public static bool PlaceAbove(RectTransform canvasRect, RectTransform mark, Vector3 world, float screenOffset)
    {
        Camera cam = Camera.main;
        if (cam == null) return false;

        Vector3 screen = cam.WorldToScreenPoint(world);
        if (screen.z <= 0f) return false;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out Vector2 local);
        mark.anchoredPosition = local + new Vector2(0f, screenOffset);
        return true;
    }

    /// <summary>검은 테두리를 두른 굵은 글자. 밝은 배경에서도 잘 보인다.</summary>
    public static Text CreateOutlinedText(string name, Transform parent, string content, int size,
                                          Color color, Color outlineColor, float outlineWidth)
    {
        var text = UIFactory.CreateText(name, parent, content, size, TextAnchor.MiddleCenter, color);
        text.fontStyle = FontStyle.Bold;
        var outline = text.gameObject.AddComponent<Outline>();
        outline.effectColor = outlineColor;
        outline.effectDistance = new Vector2(outlineWidth, -outlineWidth);
        return text;
    }
}
