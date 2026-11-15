using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 파운장 발동 컷인. 먹 붓자국이 자기 진영 쪽에서 쓸려 나오고, 그 위에 破雲掌 / 파운장 글씨가 찍힌다.
/// 일러스트(cutInArt)가 있으면 청운 상반신이 붓자국과 함께 쓸려 들어오고 글씨는 그 아래에 찍힌다.
/// 짧은 흰 섬광이 함께 지나간다. 실제 시간으로 움직여 발동 슬로우 중에도 흐른다.
/// </summary>
public sealed class QiCutIn : MonoBehaviour
{
    const float Duration = .95f;
    RectTransform stroke, title, portrait;
    RawImage strokeImage, flash, portraitImage;
    float bandY = 120f;
    Text hanja, hangul;
    CanvasGroup group;
    float t; int side;

    /// <param name="side">-1: 화면 왼쪽에서, +1: 오른쪽에서 쓸려 나온다(공격자 진영).</param>
    /// <param name="art">컷인 일러스트. 오른쪽을 보고 있는 그림이며 오른쪽 진영에서 나올 때 좌우를 뒤집는다.</param>
    public static QiCutIn Play(Texture2D brush, int side, Texture2D art = null)
    {
        var go = new GameObject("QiPalm CutIn");
        var canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 600;
        var scaler = go.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
        var cut = go.AddComponent<QiCutIn>(); cut.side = side >= 0 ? 1 : -1; cut.Build(brush, art);
        return cut;
    }

    public static void Prewarm()
    {
        var font = WuxiaTheme.MenuFont;
        if (font == null) return;
        // First dynamic CJK font creation/glyph rasterization belongs to scene preparation.
        foreach (int size in new[] { 128, 168 }) font.RequestCharactersInTexture("破雲掌", size, FontStyle.Normal);
        foreach (int size in new[] { 40, 46 }) font.RequestCharactersInTexture("파 운 장", size, FontStyle.Normal);
    }

    void Build(Texture2D brush, Texture2D art)
    {
        bool hasArt = art != null;
        if (hasArt) bandY = 70f;
        group = gameObject.AddComponent<CanvasGroup>(); group.blocksRaycasts = false; group.interactable = false;
        flash = NewImage("Flash", transform, null, new Color(.85f, .95f, 1f, 0f));
        Stretch(flash.rectTransform);
        strokeImage = NewImage("Stroke", transform, brush, new Color(.02f, .025f, .04f, .93f));
        stroke = strokeImage.rectTransform; stroke.sizeDelta = hasArt ? new Vector2(1840, 690) : new Vector2(1640, 470); stroke.anchoredPosition = new Vector2(0, bandY);
        if (side < 0) stroke.localScale = new Vector3(-1, 1, 1);
        if (hasArt)
        {
            portraitImage = NewImage("Portrait", transform, art, new Color(1f, 1f, 1f, 0f));
            portrait = portraitImage.rectTransform;
            portrait.sizeDelta = new Vector2(1660f, 1660f * art.height / art.width); portrait.anchoredPosition = new Vector2(0, bandY);
        }
        title = new GameObject("Title", typeof(RectTransform)).GetComponent<RectTransform>(); title.SetParent(transform, false);
        title.sizeDelta = new Vector2(1200, 300); title.anchoredPosition = hasArt ? new Vector2(side * -330f, -385f) : new Vector2(0, 130);
        hanja = NewText("Hanja", title, "破雲掌", hasArt ? 128 : 168, new Vector2(0, hasArt ? 18 : 26), new Color(.9f, .97f, 1f));
        hangul = NewText("Hangul", title, "파 운 장", hasArt ? 40 : 46, new Vector2(0, hasArt ? -72 : -92), new Color(.62f, .9f, 1f));
        foreach (var txt in new[] { hanja, hangul })
        {
            var o = txt.gameObject.AddComponent<Outline>(); o.effectColor = new Color(.1f, .55f, 1f, .75f); o.effectDistance = new Vector2(3, -3);
            var s = txt.gameObject.AddComponent<Shadow>(); s.effectColor = new Color(0, 0, 0, .8f); s.effectDistance = new Vector2(6, -6);
        }
    }

    static RawImage NewImage(string name, Transform parent, Texture tex, Color color)
    {
        var img = new GameObject(name, typeof(RectTransform)).AddComponent<RawImage>();
        img.transform.SetParent(parent, false); img.texture = tex; img.color = color; img.raycastTarget = false;
        return img;
    }
    static Text NewText(string name, Transform parent, string text, int size, Vector2 pos, Color color)
    {
        var t = new GameObject(name, typeof(RectTransform)).AddComponent<Text>();
        t.transform.SetParent(parent, false); t.text = text; t.fontSize = size; t.color = color; t.alignment = TextAnchor.MiddleCenter;
        t.font = WuxiaTheme.MenuFont; t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow; t.raycastTarget = false;
        var r = t.rectTransform; r.sizeDelta = new Vector2(1200, size * 1.4f); r.anchoredPosition = pos;
        return t;
    }
    static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }

    /// <summary>미리보기용: 지정 시각의 모습으로 맞춘다(지우지 않는다).</summary>
    public void Seek(float time) { t = time; Apply(); }

    void Update()
    {
        t += Time.unscaledDeltaTime;
        Apply();
        if (t >= Duration) Destroy(gameObject);
    }

    void Apply()
    {
        float u = t / Duration;
        // 붓자국: 0~0.18 쓸려 들어옴, 0.75 이후 흐려짐
        float sweep = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / .18f));
        stroke.anchoredPosition = new Vector2(Mathf.Lerp(side * 1500f, 0f, sweep) - side * 40f * u, bandY);
        if (portrait != null)
        {
            // 일러스트: 붓자국 바로 뒤따라 쓸려 들어와 천천히 밀리며 살짝 당겨진다(1.06 → 1.0). 붓자국과 함께 흐려진다.
            float slide = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - .02f) / .2f));
            portrait.anchoredPosition = new Vector2(Mathf.Lerp(side * 1700f, 0f, slide) - side * 55f * u, bandY);
            float zoom = Mathf.Lerp(1.06f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / .5f)));
            portrait.localScale = new Vector3(side > 0 ? -zoom : zoom, zoom, 1f);
            portraitImage.color = new Color(1f, 1f, 1f, Mathf.Clamp01((t - .03f) / .1f) * (1f - Mathf.Clamp01((u - .74f) / .26f)));
        }
        strokeImage.color = new Color(.02f, .025f, .04f, .93f * (1f - Mathf.Clamp01((u - .72f) / .28f)));
        // 글씨: 붓 뒤에 크게 찍혔다가 제자리로
        float stamp = Mathf.Clamp01((t - .08f) / .14f);
        title.localScale = Vector3.one * Mathf.Lerp(1.6f, 1f, Mathf.SmoothStep(0, 1, stamp));
        float textAlpha = stamp * (1f - Mathf.Clamp01((u - .7f) / .3f));
        hanja.color = new Color(.9f, .97f, 1f, textAlpha); hangul.color = new Color(.62f, .9f, 1f, textAlpha);
        // 섬광: 처음 0.12초
        flash.color = new Color(.85f, .95f, 1f, .45f * (1f - Mathf.Clamp01(t / .12f)));
    }
}
