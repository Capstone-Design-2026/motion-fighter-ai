using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// UI 를 코드로 만들 때 쓰는 공용 도구. 기존 CombatHUD 와 같은 방식이다.
/// 나중에 디자인을 입힐 때는 여기서 만든 오브젝트에 스프라이트/폰트만 갈아끼우면 되고,
/// 통째로 프리팹으로 대체하고 싶으면 각 씬 UI 스크립트의 prefab 필드를 쓰면 된다.
/// </summary>
public static class UIFactory
{
    private static Font cachedFont;

    public static Font DefaultFont
    {
        get
        {
            if (cachedFont == null) cachedFont = LoadFont();
            return cachedFont;
        }
    }

    /// <summary>버튼 클릭을 받으려면 씬에 EventSystem 이 반드시 있어야 한다. 없으면 만들어 준다.</summary>
    public static void EnsureEventSystem()
    {
        if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null) return;
        var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        go.transform.SetSiblingIndex(0);
    }

    public static Canvas CreateCanvas(string name, Transform parent, int sortingOrder = 0)
    {
        var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        if (parent != null) go.transform.SetParent(parent, false);

        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        return canvas;
    }

    public static RectTransform CreateRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    public static Image CreateImage(string name, Transform parent, Color color, bool raycastTarget = false)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = raycastTarget;
        return img;
    }

    public static Text CreateText(string name, Transform parent, string content, int size,
                                  TextAnchor anchor, Color? color = null)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        go.transform.SetParent(parent, false);

        var text = go.GetComponent<Text>();
        text.font = DefaultFont;
        text.fontSize = size;
        text.alignment = anchor;
        text.text = content;
        text.color = color ?? Color.white;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    /// <summary>배경 이미지 + 가운데 정렬 라벨을 가진 기본 버튼.</summary>
    public static Button CreateButton(string name, Transform parent, string label, int fontSize,
                                      Color background, UnityAction onClick)
    {
        var img = CreateImage(name, parent, background, true);
        var button = img.gameObject.AddComponent<Button>();
        button.targetGraphic = img;

        // 마우스를 올리거나 눌렀을 때 살짝 밝아지게. 디자인이 들어오면 SpriteSwap 으로 바꾸면 된다.
        var colors = button.colors;
        colors.normalColor      = Color.white;
        colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
        colors.pressedColor     = new Color(0.75f, 0.75f, 0.75f, 1f);
        colors.disabledColor    = new Color(1f, 1f, 1f, 0.35f);
        colors.fadeDuration     = 0.08f;
        button.colors = colors;

        var text = CreateText("Label", img.transform, label, fontSize, TextAnchor.MiddleCenter);
        Stretch(text.rectTransform);

        if (onClick != null) button.onClick.AddListener(onClick);
        return button;
    }

    /// <summary>부모 영역에 꽉 채운다.</summary>
    public static void Stretch(RectTransform rect, float left = 0f, float bottom = 0f,
                               float right = 0f, float top = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    /// <summary>앵커/피벗/위치/크기를 한 번에 지정한다.</summary>
    public static void Place(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
    {
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = pivot;
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
    }

    private static Font LoadFont()
    {
        Font f = null;
        try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
        if (f == null) { try { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { } }
        if (f == null) { try { f = Font.CreateDynamicFontFromOSFont("Malgun Gothic", 24); } catch { } }
        if (f == null) Debug.LogWarning("[UIFactory] 폰트를 찾지 못했습니다. 텍스트가 안 보일 수 있습니다.");
        return f;
    }
}
