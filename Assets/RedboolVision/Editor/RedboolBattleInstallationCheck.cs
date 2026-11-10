using System;
using UnityEditor;
using UnityEngine;

public static class RedboolBattleInstallationCheck
{
    [MenuItem("Tools/REDBOOL/Check Battle Auto Connection (Play Mode)")]
    public static void Run()
    {
        if (!EditorApplication.isPlaying) throw new Exception("Open a combat scene and enter Play Mode first.");
        var player = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
        if (player == null) throw new Exception("No combat player in the loaded scene.");
        int receivers = 0;
        RedboolDemoReceiver selected = null;
        foreach (var r in UnityEngine.Object.FindObjectsByType<RedboolDemoReceiver>(FindObjectsSortMode.None))
            if (r.isActiveAndEnabled) { receivers++; selected = r; }
        if (receivers != 1 || selected.player != player || selected.Preparation == null)
            throw new Exception("Expected one active receiver attached to this combat player and preparation gate.");
        foreach (var old in UnityEngine.Object.FindObjectsByType<UdpActionReceiver>(FindObjectsSortMode.None))
            if (old.enabled) throw new Exception("Legacy receiver is still enabled.");
        if (!selected.Preparation.AllowsPlayer && !player.GetComponent<Fighter>().VisionPreparationLocked)
            throw new Exception("Unprepared player is not locked.");
        foreach (var enemy in UnityEngine.Object.FindObjectsByType<EnemyAI>(FindObjectsSortMode.None))
            if (selected.Preparation.HoldRound && !enemy.GetComponent<Fighter>().VisionPreparationLocked)
                throw new Exception("Enemy can act before preparation.");
        Debug.Log("REDBOOL automatic receiver and preparation wiring passed. Verify actual calibration/countdown, timer, reconnect and scene transitions separately.");
    }
}
