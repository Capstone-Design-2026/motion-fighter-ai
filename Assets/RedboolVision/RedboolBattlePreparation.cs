using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

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

    // 전투를 멈춰 둔 이유를 화면 위쪽 가운데에 한 줄로 알린다(게임의 다른 안내와 같은 먹색 판).
    private Canvas noticeCanvas; private Text noticeText; private string shownNotice;
    /// <summary>지금 띄우는 안내 글. 없으면 null (검사용으로도 쓴다).</summary>
    public string Notice
    {
        get
        {
            // 결과 화면이 멈춘 이유를 이미 알려 준다. 쓰러진 뒤에는 카메라 안내로 가리지 않는다.
            foreach (var fighter in fighters)
                if (fighter != null && fighter.Health != null && fighter.Health.IsDead) return null;
            if (KeyboardTest) return null;   // 키보드로 시험하는 중에는 아무것도 띄우지 않는다 (F8 로 켜고 끄는 것은 그대로)
            if (EffectsPreparing) return "전투 준비 중 · 잠시만 기다려 주세요";
            if (State.Running) return null;
            bool integrated = RedboolCameraRuntime.Instance != null;   // 게임 안에서 카메라를 켜는 패키지
            return State.Rehearsal ? "연결 확인 중 · 적과 시간은 잠시 멈춰 있어요"
                : integrated && !State.Connected ? "카메라 준비 중 · 오른쪽 내 동작 화면을 확인해주세요"
                : integrated && !State.Calibrated ? "중앙에서 가드를 잡고 보정 버튼 또는 C를 누르세요"
                : !State.Connected ? "카메라 신호를 기다리는 중 · 카메라 프로그램이 켜져 있는지 확인해 주세요"
                : !State.Calibrated ? "카메라 창을 누른 뒤, 가운데서 가드를 잡고 C를 눌러 보정하세요"
                : "어깨와 두 팔이 카메라에 보이게 서 주세요";
        }
    }

    private void LateUpdate()
    {
        string text = Notice;
        if (text == shownNotice) return;
        shownNotice = text;
        if (noticeCanvas == null)
        {
            if (text == null) return;
            noticeCanvas = UIFactory.CreateCanvas("CameraWaitNotice", transform, 150);
            var plaque = UIFactory.CreateRect("Plaque", noticeCanvas.transform);
            UIFactory.Place(plaque, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -150), new Vector2(900, 58));
            var ink = WuxiaTheme.Ornament("Ink", plaque, WuxiaGraphic.Shape.Plaque, new Color(.03f, .045f, .055f, .9f)); UIFactory.Stretch(ink.rectTransform);
            var frame = WuxiaTheme.Ornament("Frame", plaque, WuxiaGraphic.Shape.PlaqueFrame, new Color(WuxiaTheme.Gold.r, WuxiaTheme.Gold.g, WuxiaTheme.Gold.b, .8f)); UIFactory.Stretch(frame.rectTransform);
            noticeText = WuxiaTheme.Text("Notice", plaque, "", 24, TextAnchor.MiddleCenter, WuxiaTheme.Paper);
            noticeText.raycastTarget = false; UIFactory.Stretch(noticeText.rectTransform);
        }
        noticeCanvas.gameObject.SetActive(text != null);
        if (text != null) noticeText.text = text;
    }

    // Fail closed if accidentally disabled during battle. Scene teardown destroys the fighters.
    private void OnDisable() { State.Reset(); ApplyLocks(); }
}
