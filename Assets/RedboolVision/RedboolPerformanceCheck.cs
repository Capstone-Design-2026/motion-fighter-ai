using System;
using System.Collections;
using UnityEngine;

// Explicit diagnostic mode only, no camera, no campaign save or fabricated recognition.
public sealed class RedboolPerformanceCheck : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "--redbool-performance-check") < 0) return;
        var go = new GameObject("REDBOOL Effect Performance Check");
        DontDestroyOnLoad(go); go.AddComponent<RedboolPerformanceCheck>();
    }
    IEnumerator Start()
    {
        while (!RedboolDuelTest.Active || UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != RedboolDuelTest.ArenaScene) yield return null;
        yield return new WaitForSecondsRealtime(3);
        var player = FindAnyObjectByType<PlayerController>();
        var gate = FindAnyObjectByType<RedboolBattlePreparation>();
        if (player == null || gate == null) { Debug.LogError("[REDBOOL CHECK] Missing battle"); Application.Quit(2); yield break; }
        while (gate.EffectsPreparing) yield return null;
        if (player.GetComponent<Health>().CurrentHealth != player.GetComponent<Health>().MaxHealth
            || player.GetComponent<SpecialMove>().Charge != 0 || Time.timeScale != 1f
            || (Camera.main != null && Camera.main.targetTexture != null)
            || GameObject.Find("Effect warmup sample") != null)
        { Debug.LogError("[REDBOOL CHECK] Warmup changed gameplay or leaked render resources"); Application.Quit(2); yield break; }
        Debug.Log("[REDBOOL CHECK] PASS warmup preserves health, charge, clock and camera target");
        for (int i = 0; i < 2; i++)
        {
            Debug.Log("[REDBOOL CHECK] hit=" + i + " frame=" + Time.frameCount);
            RedboolEffectPreparation.Measure("hit-spawn", () => HitVfx.Play(player.transform.position + Vector3.up, true));
            yield return new WaitForSecondsRealtime(2);
        }
        var special = player.GetComponent<SpecialMove>();
        for (int i = 0; i < 2; i++)
        {
            gate.Observe(true, "SYNTHETIC", "READY", false, "NEUTRAL");
            special.AddCharge(special.HitsToCharge);
            yield return null;
            gate.Observe(true, "SYNTHETIC", "READY", false, "NEUTRAL");
            bool applied = false;
            Debug.Log("[REDBOOL CHECK] special=" + i + " frame=" + Time.frameCount);
            RedboolEffectPreparation.Measure("special-activate", () => applied = special.Activate());
            if (!applied) { Debug.LogError("[REDBOOL CHECK] Special rejected"); Application.Quit(2); yield break; }
            double deadline = Time.realtimeSinceStartupAsDouble + 5;
            while (Time.realtimeSinceStartupAsDouble < deadline)
            {
                gate.Observe(true, "SYNTHETIC", "READY", false, "NEUTRAL");
                yield return null;
            }
            if (special.IsPlaying || Time.timeScale != 1f) { Debug.LogError("[REDBOOL CHECK] Special did not recover"); Application.Quit(2); yield break; }
        }
        Debug.Log("[REDBOOL CHECK] PASS effects and special recovery; camera_started=false");
        yield return new WaitForSecondsRealtime(2);
        Application.Quit(0);
    }
}
