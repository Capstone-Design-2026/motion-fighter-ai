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
    public bool HoldRound => EffectsPreparing || !KeyboardTest && (!isActiveAndEnabled || !State.Running);
    public bool EffectsPreparing { get; internal set; }
    public bool AllowsPlayer => !EffectsPreparing && isActiveAndEnabled && (KeyboardTest || State.Running || State.Rehearsal);

    /// <summary>
    /// 키보드 테스트 모드. 카메라(Python) 없이 키보드로 전투를 시험할 때 쓴다. 기본은 꺼져 있어 카메라 대기가 유지된다.
    /// 전투 중 F8 로 켜고 끄며, 실행 인자 -redbool-keyboard 로 처음부터 켤 수 있다(배치 검사 포함).
    /// </summary>
    public static bool KeyboardTest { get; private set; }
    public const KeyCode KeyboardTestKey = KeyCode.F8;
    public static void SetKeyboardTest(bool on) => KeyboardTest = on;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetKeyboardTest() => KeyboardTest = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-redbool-keyboard") >= 0;

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
        if (RedboolPvp.Active) return;
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
        if (RedboolEffectPreparation.Enabled)
        {
            preparation.EffectsPreparing = true;
            receiver.gameObject.AddComponent<RedboolRenderWarmup>().preparation = preparation;
        }
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
        if (Input.GetKeyDown(KeyboardTestKey)) SetKeyboardTest(!KeyboardTest);
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
            if (fighter != null) fighter.SetVisionPreparationLock(EffectsPreparing || !KeyboardTest && !State.Running && !(State.Rehearsal && fighter == playerFighter));
    }

    private void OnGUI()
    {
        // The result screen already explains the stopped round; do not cover it
        // with a misleading camera prompt after the recording is closed.
        foreach (var fighter in fighters)
            if (fighter != null && fighter.Health != null && fighter.Health.IsDead) return;
        if (KeyboardTest) return;   // 키보드로 시험하는 중에는 화면에 아무것도 띄우지 않는다 (F8 로 켜고 끄는 것은 그대로)
        if (EffectsPreparing) { GUI.Box(new Rect(Screen.width / 2f - 200, 110, 400, 30), "PREPARING COMBAT EFFECTS"); return; }
        if (State.Running) return;
        bool integrated=RedboolCameraRuntime.Instance!=null;
        string text = State.Rehearsal ? "CONNECTION TEST — enemy and round timer held"
            : integrated&&!State.Connected ? "카메라 준비 중 · 오른쪽 내 동작 화면을 확인해주세요"
            : integrated&&!State.Calibrated ? "중앙에서 가드를 잡고 보정 버튼 또는 C를 누르세요"
            : !State.Connected ? "WAITING FOR CAMERA — start Python"
            : !State.Calibrated ? "CALIBRATE — focus camera window and press C"
            : "TRACKING PAUSED — keep both arms and shoulders visible";
        GUI.Box(new Rect(Screen.width / 2f - 300, 110, 600, 50), text);
    }

    // Fail closed if accidentally disabled during battle. Scene teardown destroys the fighters.
    private void OnDisable() { State.Reset(); ApplyLocks(); }
}
