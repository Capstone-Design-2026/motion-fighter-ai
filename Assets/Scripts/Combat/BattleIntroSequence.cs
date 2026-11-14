using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Scene-local READY/FIGHT presentation, installed by every BattleSceneBridge.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-450)]
public sealed class BattleIntroSequence : MonoBehaviour
{
    public enum IntroPhase { Settling, Ready, Fight, Complete }

    [SerializeField, Min(0f)] private float sceneSettleSeconds = .3f;
    [SerializeField, Min(.1f)] private float readySeconds = 1.1f;
    [SerializeField, Min(.1f)] private float fightSeconds = .55f;

    public bool HoldRound { get; private set; } = true;
    public IntroPhase Phase { get; private set; } = IntroPhase.Settling;

    private readonly List<Fighter> fighters = new List<Fighter>();
    private BattleSceneBridge battle;
    private RedboolBattlePreparation preparation;
    private Canvas overlay;
    private CanvasGroup overlayGroup;
    private Text title;
    private float elapsed;

    // Called in BattleSceneBridge.Awake, after stage prefab replacement, before any input Update.
    public void Prepare()
    {
        HoldRound = true;
        foreach (var root in gameObject.scene.GetRootGameObjects())
            foreach (var fighter in root.GetComponentsInChildren<Fighter>())
            {
                if (!fighters.Contains(fighter)) fighters.Add(fighter);
                fighter.SetRoundIntroLock(true);
            }
    }

    private void Start() => Restart();

    public void Restart()
    {
        StopAllCoroutines();
        Prepare();
        battle = GetComponent<BattleSceneBridge>();
        preparation = null;
        foreach (var gate in FindObjectsByType<RedboolBattlePreparation>())
            if (gate.gameObject.scene == gameObject.scene) { preparation = gate; break; }
        if (overlay == null) BuildOverlay();
        Phase = IntroPhase.Settling;
        elapsed = 0f;
        overlay.gameObject.SetActive(false);
        StartCoroutine(PlayIntro());
    }

    private IEnumerator PlayIntro()
    {
        // Let fighter visuals, grounding, camera framing and HUD finish their Start methods.
        yield return null;
        while (HoldRound)
        {
            if (battle != null && battle.IsResolved) break;
            bool inputsReady = preparation == null || !preparation.HoldRound;
            if (!inputsReady)
            {
                // Never announce FIGHT while camera calibration/tracking still holds the round.
                Phase = IntroPhase.Ready;
                elapsed = 0f;
                DrawTitle(true);
                yield return null;
                continue;
            }
            if (Time.timeScale <= 0f) { yield return null; continue; }

            // A loading hitch must not skip an entire title without displaying it.
            elapsed += Mathf.Min(Time.unscaledDeltaTime, .1f);
            float duration = Phase == IntroPhase.Settling ? sceneSettleSeconds
                : Phase == IntroPhase.Ready ? readySeconds : fightSeconds;
            if (elapsed >= duration)
            {
                if (Phase == IntroPhase.Fight) break;
                Phase = Phase == IntroPhase.Settling ? IntroPhase.Ready : IntroPhase.Fight;
                elapsed = 0f;
            }
            if (Phase != IntroPhase.Settling) DrawTitle(false);
            yield return null;
        }
        Finish();
    }

    private void DrawTitle(bool waitingForInput)
    {
        overlay.gameObject.SetActive(true);
        bool fight = Phase == IntroPhase.Fight;
        string headline = fight ? "FIGHT" : "READY";
        if (title.text != headline)
        {
            title.text = headline;
            title.fontSize = fight ? 196 : 176;
            title.resizeTextMaxSize = title.fontSize;
            CombatAnnouncementStyle.Apply(title, fight
                ? CombatAnnouncementStyle.Palette.Fight : CombatAnnouncementStyle.Palette.Ready);
        }
        float duration = fight ? fightSeconds : readySeconds;
        float fadeIn = Mathf.Clamp01(elapsed / .08f);
        float fadeOut = Mathf.Clamp01((duration - elapsed) / .13f);
        overlayGroup.alpha = waitingForInput ? 1f : Mathf.Min(fadeIn, fadeOut);
        float pop = waitingForInput ? 1f : Mathf.Lerp(fight ? 1.2f : 1.08f, 1f,
            Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / .18f)));
        title.rectTransform.localScale = Vector3.one * pop;
    }

    private void Finish()
    {
        HoldRound = false;
        Phase = IntroPhase.Complete;
        if (overlay != null) overlay.gameObject.SetActive(false);
        foreach (var fighter in fighters)
            if (fighter != null) fighter.SetRoundIntroLock(false);
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        Finish();
    }

    private void BuildOverlay()
    {
        var root = new GameObject("BattleIntroCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        root.transform.SetParent(transform, false);
        overlay = root.GetComponent<Canvas>();
        overlay.renderMode = RenderMode.ScreenSpaceOverlay;
        overlay.sortingOrder = 180;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = .5f;
        overlayGroup = root.GetComponent<CanvasGroup>();
        overlayGroup.blocksRaycasts = false;
        overlayGroup.interactable = false;

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
        title = UIFactory.CreateText("ReadyFightTitle", root.transform, "", 176, TextAnchor.MiddleCenter);
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
    }
}
