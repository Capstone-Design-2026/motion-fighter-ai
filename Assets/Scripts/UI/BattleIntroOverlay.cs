using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// READY / FIGHT 글씨판. 스토리 전투(BattleIntroSequence)와 PvP(RedboolPvp)가 같은 모양을 쓴다.
/// 가운데 어두운 띠, 위아래 금색 줄, 기울어진 테두리 글씨(CombatAnnouncementStyle).
/// </summary>
public sealed class BattleIntroOverlay
{
    readonly Canvas canvas;
    readonly CanvasGroup group;
    readonly Text title;

    public bool Visible => canvas != null && canvas.gameObject.activeSelf;

    BattleIntroOverlay(Canvas canvas, CanvasGroup group, Text title)
    {
        this.canvas = canvas; this.group = group; this.title = title;
    }

    public static BattleIntroOverlay Create(Transform parent, int sortingOrder = 180)
    {
        var root = new GameObject("BattleIntroCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        root.transform.SetParent(parent, false);
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = .5f;
        var group = root.GetComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;

        var band = UIFactory.CreateImage("IntroShade", root.transform, new Color(.025f, .018f, .012f, .46f));
        band.rectTransform.anchorMin = new Vector2(0f, .5f);
        band.rectTransform.anchorMax = new Vector2(1f, .5f);
        band.rectTransform.sizeDelta = new Vector2(0f, 208f);
        band.rectTransform.anchoredPosition = Vector2.zero;
        foreach (float side in new[] { -1f, 1f })
        {
            var line = UIFactory.CreateImage("IntroRule", root.transform, new Color(.94f, .71f, .32f, .8f));
            line.rectTransform.anchorMin = new Vector2(.07f, .5f);
            line.rectTransform.anchorMax = new Vector2(.93f, .5f);
            line.rectTransform.sizeDelta = new Vector2(0f, 2f);
            line.rectTransform.anchoredPosition = new Vector2(0f, side * 104f);
        }
        var title = UIFactory.CreateText("ReadyFightTitle", root.transform, "", 176, TextAnchor.MiddleCenter);
        CombatAnnouncementStyle.Apply(title, CombatAnnouncementStyle.Palette.Ready);
        title.resizeTextForBestFit = true;
        title.resizeTextMinSize = 48;
        title.resizeTextMaxSize = 176;
        title.horizontalOverflow = HorizontalWrapMode.Wrap;
        title.verticalOverflow = VerticalWrapMode.Truncate;
        title.rectTransform.anchorMin = new Vector2(.06f, .5f);
        title.rectTransform.anchorMax = new Vector2(.94f, .5f);
        title.rectTransform.sizeDelta = new Vector2(0f, 260f);
        title.rectTransform.anchoredPosition = new Vector2(0f, 3f);
        root.SetActive(false);
        return new BattleIntroOverlay(canvas, group, title);
    }

    /// <summary>
    /// fight: FIGHT 글씨인가(아니면 READY). elapsed/duration: 그 글씨가 뜬 지 몇 초, 몇 초 동안 뜨는가.
    /// waiting: 카메라 준비를 기다리는 중이라 READY 를 흐리지 않고 붙잡아 둔다.
    /// </summary>
    public void Show(bool fight, float elapsed, float duration, bool waiting)
    {
        if (canvas == null) return;
        if (!canvas.gameObject.activeSelf) canvas.gameObject.SetActive(true);
        string headline = fight ? "FIGHT" : "READY";
        if (title.text != headline)
        {
            title.text = headline;
            title.fontSize = fight ? 196 : 176;
            title.resizeTextMaxSize = title.fontSize;
            CombatAnnouncementStyle.Apply(title, fight
                ? CombatAnnouncementStyle.Palette.Fight : CombatAnnouncementStyle.Palette.Ready);
        }
        float fadeIn = Mathf.Clamp01(elapsed / .08f);
        float fadeOut = Mathf.Clamp01((duration - elapsed) / .13f);
        group.alpha = waiting ? 1f : Mathf.Min(fadeIn, fadeOut);
        float pop = waiting ? 1f : Mathf.Lerp(fight ? 1.2f : 1.08f, 1f,
            Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / .18f)));
        title.rectTransform.localScale = Vector3.one * pop;
    }

    public void Hide()
    {
        if (canvas != null && canvas.gameObject.activeSelf) canvas.gameObject.SetActive(false);
    }
}
