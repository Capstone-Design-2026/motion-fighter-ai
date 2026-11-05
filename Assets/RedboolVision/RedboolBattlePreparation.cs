using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Scene-local preparation owns a separate lock; never clears KO or story locks.</summary>
[DefaultExecutionOrder(-700)]
public sealed class RedboolBattlePreparation : MonoBehaviour
{
    public RedboolDemoReceiver receiver;
    public PlayerController player;
    public readonly RedboolPreparationState State = new RedboolPreparationState();
    private readonly List<Fighter> fighters = new List<Fighter>();
    private double nextRefresh;
    public bool HoldRound => !isActiveAndEnabled || !State.Running;
    public bool AllowsPlayer => isActiveAndEnabled && (State.Running || State.Rehearsal);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InstallHook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // sceneLoaded runs after scene Awake and before Start/Update. Stage replacements
        // created by BattleSceneBridge.Awake are included in the first lock pass.
        PlayerController player = null;
        RedboolDemoReceiver receiver = null;
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var p in root.GetComponentsInChildren<PlayerController>())
                if (p.isActiveAndEnabled && p.GetComponent<Fighter>() != null) { player = p; break; }
            foreach (var r in root.GetComponentsInChildren<RedboolDemoReceiver>())
                if (r.isActiveAndEnabled && receiver == null) receiver = r;
        }
        if (player == null) return; // Menu/map/tutorial creation remains scene-owned.
        if (receiver == null)
        {
            var go = new GameObject("REDBOOL Vision Demo");
            go.SetActive(false);
            SceneManager.MoveGameObjectToScene(go, scene);
            receiver = go.AddComponent<RedboolDemoReceiver>();
            receiver.player = player;
            go.SetActive(true);
        }
        var preparation = receiver.GetComponent<RedboolBattlePreparation>();
        if (preparation == null) preparation = receiver.gameObject.AddComponent<RedboolBattlePreparation>();
        preparation.player = player;
        preparation.receiver = receiver;
        receiver.player = player;
        receiver.Preparation = preparation;
        preparation.ResetPreparation();
    }

    public void Observe(bool calibrated, string tracking, string phase, bool block, string movement)
    {
        if (!isActiveAndEnabled) { ResetPreparation(); return; }
        State.Observe(Time.realtimeSinceStartupAsDouble, calibrated, tracking, phase, block, movement);
        ApplyLocks();
    }

    public void ResetPreparation()
    {
        State.Reset();
        RefreshFighters();
        ApplyLocks();
    }

    private void Update()
    {
        if (receiver == null || !receiver.isActiveAndEnabled) State.Reset();
        else State.Tick(Time.realtimeSinceStartupAsDouble);
        if (Time.realtimeSinceStartupAsDouble >= nextRefresh) RefreshFighters();
        ApplyLocks();
    }

    private void RefreshFighters()
    {
        fighters.Clear();
        foreach (var root in gameObject.scene.GetRootGameObjects())
            fighters.AddRange(root.GetComponentsInChildren<Fighter>());
        nextRefresh = Time.realtimeSinceStartupAsDouble + .25;
    }

    private void ApplyLocks()
    {
        var playerFighter = player != null ? player.GetComponent<Fighter>() : null;
        foreach (var fighter in fighters)
            if (fighter != null) fighter.SetVisionPreparationLock(!State.Running && !(State.Rehearsal && fighter == playerFighter));
    }

    private void OnGUI()
    {
        if (State.Running) return;
        string text = State.Rehearsal ? "CONNECTION TEST — enemy and round timer held"
            : !State.Connected ? "WAITING FOR CAMERA — start Python"
            : !State.Calibrated ? "CALIBRATE — focus camera window and press C"
            : "TRACKING PAUSED — keep both arms and shoulders visible";
        GUI.Box(new Rect(Screen.width / 2f - 300, 110, 600, 50), text);
    }

    // Fail closed if accidentally disabled during battle. Scene teardown destroys the fighters.
    private void OnDisable() { State.Reset(); ApplyLocks(); }
}
