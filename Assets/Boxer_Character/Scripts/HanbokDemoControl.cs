using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// 복싱 5동작 시연용: 화면 버튼 또는 키보드로 동작을 바꿔 보는 스크립트
// 기본 자세 = Boxing_idle / J = 잽 / K = 크로스 / U = 하이킥 / I = 로우킥 / H = 머리 피격 / B = 복부 피격 / V = 킥 가드 / C = 킥 가드(낮게)
[RequireComponent(typeof(Animator))]
public class HanbokDemoControl : MonoBehaviour
{
    static readonly int JabId = Animator.StringToHash("Jab");
    static readonly int CrossId = Animator.StringToHash("Cross");
    static readonly int HighKickId = Animator.StringToHash("HighKick");
    static readonly int LowKickId = Animator.StringToHash("LowKick");
    static readonly int GuardJabId = Animator.StringToHash("GuardJab");
    static readonly int GuardCrossId = Animator.StringToHash("GuardCross");
    static readonly int KickGuardLowTrig = Animator.StringToHash("KickGuardLow");
    static readonly int HitHeadId = Animator.StringToHash("HitHead");
    static readonly int HitStomachId = Animator.StringToHash("HitStomach");
    static readonly int HitLegId = Animator.StringToHash("HitLeg");
    static readonly int HitJabId = Animator.StringToHash("HitJab");
    static readonly int HighGuardV2Id = Animator.StringToHash("HighGuardV2");
    static readonly int KickGuardTrig = Animator.StringToHash("KickGuard");
    bool highGuardV2;

    Animator anim;

    void Awake() { anim = GetComponent<Animator>(); }

    void Update()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb.jKey.wasPressedThisFrame) Jab();
        if (kb.kKey.wasPressedThisFrame) Cross();
        if (kb.uKey.wasPressedThisFrame) HighKick();
        if (kb.iKey.wasPressedThisFrame) LowKick();
        if (kb.digit1Key.wasPressedThisFrame) GuardJab();
        if (kb.digit2Key.wasPressedThisFrame) GuardCross();
        if (kb.cKey.wasPressedThisFrame) KickGuardLowMove();
        if (kb.gKey.wasPressedThisFrame) ToggleHighGuardV2();
        if (kb.vKey.wasPressedThisFrame) KickGuardMove();
        if (kb.hKey.wasPressedThisFrame) HitHead();
        if (kb.bKey.wasPressedThisFrame) HitStomach();
        if (kb.lKey.wasPressedThisFrame) HitLeg();
        if (kb.jKey.wasPressedThisFrame) HitJab();
#else
        if (Input.GetKeyDown(KeyCode.J)) Jab();
        if (Input.GetKeyDown(KeyCode.K)) Cross();
        if (Input.GetKeyDown(KeyCode.U)) HighKick();
        if (Input.GetKeyDown(KeyCode.I)) LowKick();
        if (Input.GetKeyDown(KeyCode.Alpha1)) GuardJab();
        if (Input.GetKeyDown(KeyCode.Alpha2)) GuardCross();
        if (Input.GetKeyDown(KeyCode.C)) KickGuardLowMove();
        if (Input.GetKeyDown(KeyCode.G)) ToggleHighGuardV2();
        if (Input.GetKeyDown(KeyCode.V)) KickGuardMove();
        if (Input.GetKeyDown(KeyCode.H)) HitHead();
        if (Input.GetKeyDown(KeyCode.B)) HitStomach();
        if (Input.GetKeyDown(KeyCode.L)) HitLeg();
        if (Input.GetKeyDown(KeyCode.J)) HitJab();
#endif
    }

    public void ToggleHighGuardV2() { highGuardV2 = !highGuardV2; anim.SetBool(HighGuardV2Id, highGuardV2); }
    public void Jab() { anim.SetTrigger(JabId); }
    public void Cross() { anim.SetTrigger(CrossId); }
    public void HighKick() { anim.SetTrigger(HighKickId); }
    public void LowKick() { anim.SetTrigger(LowKickId); }
    public void GuardJab() { anim.SetTrigger(GuardJabId); }
    public void GuardCross() { anim.SetTrigger(GuardCrossId); }
    public void KickGuardLowMove() { anim.SetTrigger(KickGuardLowTrig); }
    public void KickGuardMove() { anim.SetTrigger(KickGuardTrig); }
    public void HitHead() { anim.SetTrigger(HitHeadId); }
    public void HitStomach() { anim.SetTrigger(HitStomachId); }
    public void HitLeg() { anim.SetTrigger(HitLegId); }
    public void HitJab() { anim.SetTrigger(HitJabId); }

    static readonly string[,] Names = {
        { "JAB",      "JAB (잽)" },
        { "CROSS",    "CROSS (크로스)" },
        { "HIGHKICK", "HIGH KICK (하이킥)" },
        { "LOWKICK",  "LOW KICK (로우킥)" },
        { "GUARD_JAB",      "GUARD vs JAB" },
        { "GUARD_CROSS",    "GUARD vs CROSS" },
        { "KICK_GUARD_LOW", "KICK GUARD LOW (킥 가드 낮게)" },
        { "KICK_GUARD", "KICK GUARD (킥 가드)" },
        { "HIT_HEAD",    "HIT (머리 피격)" },
        { "HIT_STOMACH", "HIT (복부 피격)" },
        { "HIT_LEG", "HIT (다리 피격)" },
        { "HIT_JAB", "HIT (잽 피격)" },
        { "HIGHGUARD_V2", "HIGH GUARD v2 (하이가드)" },
    };

    string CurrentName()
    {
        var info = anim.GetCurrentAnimatorStateInfo(0);
        for (int i = 0; i < Names.GetLength(0); i++)
            if (info.IsName(Names[i, 0])) return Names[i, 1];
        return "IDLE";
    }

    void OnGUI()
    {
        // 창 높이에 맞춰 버튼 크기 자동 조절 (기준 높이 900px)
        float uiScale = Mathf.Clamp(Screen.height / 900f, 0.4f, 1.25f);
        GUI.skin.button.fontSize = Mathf.RoundToInt(20 * uiScale);
        GUI.skin.box.fontSize = Mathf.RoundToInt(18 * uiScale);
        float w = 210 * uiScale, h = 50 * uiScale, x = 20 * uiScale, y = 20 * uiScale, gap = 7 * uiScale;
        if (GUI.Button(new Rect(x, y, w, h), "JAB (J)")) Jab();
        if (GUI.Button(new Rect(x, y += h + gap, w, h), "CROSS (K)")) Cross();
        if (GUI.Button(new Rect(x, y += h + gap, w, h), "HIGH KICK (U)")) HighKick();
        if (GUI.Button(new Rect(x, y += h + gap, w, h), "LOW KICK (I)")) LowKick();
        y += gap * 2;
        if (GUI.Button(new Rect(x, y += h + gap, w, h), "GUARD JAB (1)")) GuardJab();
        if (GUI.Button(new Rect(x, y += h + gap, w, h), "GUARD CROSS (2)")) GuardCross();
        if (GUI.Button(new Rect(x, y += h + gap, w, h), "KICK GUARD LOW (C)")) KickGuardLowMove();
        if (GUI.Button(new Rect(x, y += h + gap, w, h), "KICK GUARD (V)")) KickGuardMove();
        y += gap * 2;
        if (GUI.Button(new Rect(x, y += h + gap, w, h), "HIT HEAD (H)")) HitHead();
        if (GUI.Button(new Rect(x, y += h + gap, w, h), "HIT STOMACH (B)")) HitStomach();
        if (GUI.Button(new Rect(x, y += h + gap, w, h), "HIT LEG (L)")) HitLeg();
        if (GUI.Button(new Rect(x, y += h + gap, w, h), "HIT JAB (J)")) HitJab();
        if (GUI.Button(new Rect(x, y += h + gap, w, h), highGuardV2 ? "HIGH GUARD v2: ON (G)" : "HIGH GUARD v2: OFF (G)")) ToggleHighGuardV2();
        GUI.Box(new Rect(x, y + h + gap * 2, w, 40 * uiScale), "NOW: " + CurrentName());
    }
}
