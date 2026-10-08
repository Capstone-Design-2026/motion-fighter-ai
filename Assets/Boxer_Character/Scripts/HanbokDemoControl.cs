using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// 복싱 5동작 시연용: 화면 버튼 또는 키보드로 동작을 바꿔 보는 스크립트
// 기본 자세 = Boxing_idle / J = 잽 / K = 크로스 / U = 하이킥 / I = 로우킥
[RequireComponent(typeof(Animator))]
public class HanbokDemoControl : MonoBehaviour
{
    static readonly int JabId = Animator.StringToHash("Jab");
    static readonly int CrossId = Animator.StringToHash("Cross");
    static readonly int HighKickId = Animator.StringToHash("HighKick");
    static readonly int LowKickId = Animator.StringToHash("LowKick");
    static readonly int GuardJabId = Animator.StringToHash("GuardJab");
    static readonly int GuardCrossId = Animator.StringToHash("GuardCross");
    static readonly int GuardHighKickId = Animator.StringToHash("GuardHighKick");
    static readonly int GuardLowKickId = Animator.StringToHash("GuardLowKick");

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
        if (kb.digit3Key.wasPressedThisFrame) GuardHighKick();
        if (kb.digit4Key.wasPressedThisFrame) GuardLowKick();
#else
        if (Input.GetKeyDown(KeyCode.J)) Jab();
        if (Input.GetKeyDown(KeyCode.K)) Cross();
        if (Input.GetKeyDown(KeyCode.U)) HighKick();
        if (Input.GetKeyDown(KeyCode.I)) LowKick();
        if (Input.GetKeyDown(KeyCode.Alpha1)) GuardJab();
        if (Input.GetKeyDown(KeyCode.Alpha2)) GuardCross();
        if (Input.GetKeyDown(KeyCode.Alpha3)) GuardHighKick();
        if (Input.GetKeyDown(KeyCode.Alpha4)) GuardLowKick();
#endif
    }

    public void Jab() { anim.SetTrigger(JabId); }
    public void Cross() { anim.SetTrigger(CrossId); }
    public void HighKick() { anim.SetTrigger(HighKickId); }
    public void LowKick() { anim.SetTrigger(LowKickId); }
    public void GuardJab() { anim.SetTrigger(GuardJabId); }
    public void GuardCross() { anim.SetTrigger(GuardCrossId); }
    public void GuardHighKick() { anim.SetTrigger(GuardHighKickId); }
    public void GuardLowKick() { anim.SetTrigger(GuardLowKickId); }

    static readonly string[,] Names = {
        { "JAB",      "JAB (잽)" },
        { "CROSS",    "CROSS (크로스)" },
        { "HIGHKICK", "HIGH KICK (하이킥)" },
        { "LOWKICK",  "LOW KICK (로우킥)" },
        { "GUARD_JAB",      "GUARD vs JAB" },
        { "GUARD_CROSS",    "GUARD vs CROSS" },
        { "GUARD_HIGHKICK", "GUARD vs HIGH KICK" },
        { "GUARD_LOWKICK",  "GUARD vs LOW KICK" },
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
        if (GUI.Button(new Rect(x, y += h + gap, w, h), "GUARD HIGH KICK (3)")) GuardHighKick();
        if (GUI.Button(new Rect(x, y += h + gap, w, h), "GUARD LOW KICK (4)")) GuardLowKick();
        GUI.Box(new Rect(x, y + h + gap * 2, w, 40 * uiScale), "NOW: " + CurrentName());
    }
}
