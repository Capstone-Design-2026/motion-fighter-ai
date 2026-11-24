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
    private BattleIntroOverlay overlay;
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
        if (overlay == null) overlay = BattleIntroOverlay.Create(transform);
        Phase = IntroPhase.Settling;
        elapsed = 0f;
        overlay.Hide();
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
        bool fight = Phase == IntroPhase.Fight;
        overlay.Show(fight, elapsed, fight ? fightSeconds : readySeconds, waitingForInput);
    }

    private void Finish()
    {
        HoldRound = false;
        Phase = IntroPhase.Complete;
        overlay?.Hide();
        foreach (var fighter in fighters)
            if (fighter != null) fighter.SetRoundIntroLock(false);
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        Finish();
    }
}
