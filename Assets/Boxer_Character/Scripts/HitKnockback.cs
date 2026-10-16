using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 철권식 피격 넉백 (테스트용 별도 버전).
/// 기존 피격 모션(HIT_HEAD / HIT_STOMACH / HIT_LEG / HIT_JAB)은 그대로 재생하고,
/// 그 위에 "뒤로 밀려나면서 뒷발 → 앞발 순서로 뒷걸음질"하는 효과만 덧씌운다.
/// 기존 HIT 버튼은 그대로 두고, 화면 오른쪽 열에 "+넉백" 버튼을 따로 만든다.
/// 끄고 싶으면 이 컴포넌트만 비활성화하면 원래대로 돌아간다.
/// </summary>
[DefaultExecutionOrder(50)] // FighterStance(-100)의 발 IK 다음에 실행되어야 발 보정이 덮이지 않음
[RequireComponent(typeof(Animator))]
public class HitKnockback : MonoBehaviour
{
    [System.Serializable]
    public class Setting
    {
        [Tooltip("밀려나는 거리 (미터)")] [Range(0f, 1.5f)] public float distance = 0.4f;
        [Tooltip("밀려나는 시간 (초)")] [Range(0.1f, 1.5f)] public float duration = 0.45f;
        [Tooltip("맞은 뒤 밀리기 시작할 때까지 (초)")] [Range(0f, 1f)] public float delay = 0.02f;
        [Tooltip("뒷발을 뒤로 딛을 때 들리는 높이 (미터)")] [Range(0f, 0.2f)] public float rearStepHeight = 0.07f;
        [Tooltip("앞발이 끌려올 때 들리는 높이 (미터)")] [Range(0f, 0.2f)] public float frontStepHeight = 0.03f;
        [Tooltip("맞는 순간 상체가 뒤로 젖혀지는 각도")] [Range(0f, 20f)] public float recoilAngle = 5f;

        public Setting(float d, float t, float rear, float front, float recoil)
        { distance = d; duration = t; rearStepHeight = rear; frontStepHeight = front; recoilAngle = recoil; }
    }

    [Header("전체")]
    [Tooltip("모든 넉백 거리에 곱해지는 배율")]
    [Range(0f, 2f)] public float strength = 1f;
    [Tooltip("밀림 감속 정도. 클수록 처음에 확 밀리고 빨리 멈춤")]
    [Range(1f, 6f)] public float pushSharpness = 3f;
    [Tooltip("뒷발이 뒤로 딛는 구간 (밀림 진행 0~1)")]
    public Vector2 rearStepWindow = new Vector2(0.0f, 0.55f);
    [Tooltip("앞발이 따라오는 구간 (밀림 진행 0~1)")]
    public Vector2 frontStepWindow = new Vector2(0.25f, 0.9f);

    [Header("피격별 설정")]
    public Setting head = new Setting(0.45f, 0.42f, 0.07f, 0.03f, 6f);
    public Setting stomach = new Setting(0.55f, 0.50f, 0.08f, 0.03f, 3f);
    public Setting leg = new Setting(0.18f, 0.30f, 0.04f, 0.02f, 2f);
    public Setting jab = new Setting(0.30f, 0.32f, 0.05f, 0.02f, 4f);

    [Header("가드 피격 (막았을 때) - 피격보다 덜 밀림")]
    [Tooltip("잽을 가드로 막음 (HIT JAB 0.30m 보다 약하게)")]
    public Setting guardJab = new Setting(0.05f, 0.20f, 0.02f, 0.01f, 1.5f) { delay = 0.04f };
    [Tooltip("크로스를 가드로 막음 (HIT HEAD 0.45m 보다 약하게)")]
    public Setting guardCross = new Setting(0.08f, 0.25f, 0.03f, 0.015f, 2.5f) { delay = 0.04f };
    [Tooltip("하이킥을 다리로 막음 - 다리가 올라간 뒤(약 0.6초) 밀림")]
    public Setting kickGuard = new Setting(0.10f, 0.30f, 0.02f, 0.015f, 2f) { delay = 0.6f };
    [Tooltip("로우킥을 다리로 막음 (HIT LEG 0.18m 보다 약하게)")]
    public Setting kickGuardLow = new Setting(0.04f, 0.22f, 0.015f, 0.01f, 1f) { delay = 0.6f };

    [Tooltip("발이 바닥에서 이만큼(미터) 넘게 들려 있으면 그 발은 붙잡지 않음 (킥 가드로 든 다리)")]
    public Vector2 liftedFootFade = new Vector2(0.2f, 0.4f);

    [Header("킥 가드 +밀림 연출: 다리 빨리 올림 → 정강이로 맞음 → 밀림")]
    [Tooltip("다리를 올리는 구간 재생 배속 (클수록 빨리 올림)")]
    [Range(1f, 5f)] public float kickGuardRaiseSpeed = 3f;
    [Tooltip("다리가 다 올라간 지점 (동작 진행 0~1)")]
    [Range(0.1f, 0.8f)] public float kickGuardLegUpAt = 0.4f;
    [Tooltip("다리를 다 올린 뒤 정강이에 맞기까지 (초)")]
    [Range(0f, 0.3f)] public float kickGuardImpactWait = 0.06f;
    [Tooltip("정강이에 맞는 순간 멈칫하는 시간 (히트스톱, 초)")]
    [Range(0f, 0.2f)] public float kickGuardHitStop = 0.07f;
    [Tooltip("맞는 순간 정강이가 뒤로 젖혀지는 거리 (미터)")]
    [Range(0f, 0.2f)] public float shinJolt = 0.07f;
    [Tooltip("맞는 순간 상체가 움찔하는 각도")]
    [Range(0f, 10f)] public float shinImpactFlinch = 3f;

    [Tooltip("원본 킥 가드 클립은 다리를 올리며 몸이 뒤로 45cm쯤 빠짐. 다리 올리는 동안 그 이동을 얼마나 지울지 (1 = 완전히 제자리)")]
    [Range(0f, 1f)] public float kickGuardRaiseHold = 1f;
    [Tooltip("몸을 앞에 붙잡아 두는 만큼 디딤다리 무릎을 굽혀 낮춤. 안 낮추면 디딤다리가 닿지 않아 IK가 몸을 다시 뒤로 끌어당김")]
    [Range(0f, 1f)] public float kickGuardSink = 0.4f;
    [Tooltip("정강이에 맞고 밀린 뒤 남길 보정 정도. 낮을수록 맞은 뒤 디딤발 위로 체중이 더 실리며 뒤로 감 (1 = 계속 제자리)")]
    [Range(0f, 1f)] public float kickGuardStayInPlace = 1f;
    [Tooltip("킥 가드가 끝나고 IDLE로 돌아갈 때 IDLE을 어디부터 재생할지 (0~1). IDLE 0~0.3 구간은 몸이 뒤로 16cm 빠지는 동작이라, 처음부터 재생하면 한 번 더 밀린 것처럼 보임")]
    [Range(0f, 1f)] public float kickGuardReturnIdlePhase = 0.65f;
    [Tooltip("킥 가드 동작이 이만큼(0~1) 진행되면 IDLE로 넘어가기 시작")]
    [Range(0.5f, 0.95f)] public float kickGuardReturnAt = 0.86f;

    // 원본 클립(LegCheck / LegCheckLow)에서 미리 뽑아둔 "동작 진행(0~1) → 엉덩이가 뒤로 빠지는 거리" 곡선
    [HideInInspector] public AnimationCurve kickGuardHipsDrift = new AnimationCurve();
    [HideInInspector] public AnimationCurve kickGuardLowHipsDrift = new AnimationCurve();

    Coroutine kickSeq;
    float joltStart = -10f, joltAmount;
    bool holdActive, holdInit, holdAfterImpact;
    string holdState;
    float bodyZ0, lZ0, rZ0;



    [Header("시연 UI")]
    public bool showButtons = true;

    Animator anim;
    HanbokDemoControl demo;
    Transform spine, chest, hipsT;
    float lastBodyLocalZ, hbOffset; // 무게중심(bodyPosition)과 실제 엉덩이 뼈의 앞뒤 차이 (매 프레임 측정)

    Vector3 homePos;
    Quaternion homeRot;

    bool active;
    float timer;
    Setting cur;
    Vector3 pushDir, kbStart, disp;
    float u; // 밀림 진행 0~1

    bool rearDecided, rearIsLeft;
    Vector3 lastOffL, lastOffR;   // 직전 프레임 발 보정량 (연타 시 발이 튀지 않게 이어받음)
    Vector3 residualL, residualR;

    void Awake()
    {
        anim = GetComponent<Animator>();
        demo = GetComponent<HanbokDemoControl>();
        spine = anim.GetBoneTransform(HumanBodyBones.Spine);
        chest = anim.GetBoneTransform(HumanBodyBones.Chest);
        hipsT = anim.GetBoneTransform(HumanBodyBones.Hips);
        homePos = transform.position;
        homeRot = transform.rotation;
    }

    // ───── 버튼/키에서 호출하는 함수 ─────
    public void HitHeadKB()    { if (demo) demo.HitHead();    else anim.SetTrigger("HitHead");    Push(head); }
    public void HitStomachKB() { if (demo) demo.HitStomach(); else anim.SetTrigger("HitStomach"); Push(stomach); }
    public void HitLegKB()     { if (demo) demo.HitLeg();     else anim.SetTrigger("HitLeg");     Push(leg); }
    public void HitJabKB()     { if (demo) demo.HitJab();     else anim.SetTrigger("HitJab");     Push(jab); }
    public void GuardJabKB()      { if (demo) demo.GuardJab();         else anim.SetTrigger("GuardJab");     Push(guardJab); }
    public void GuardCrossKB()    { if (demo) demo.GuardCross();       else anim.SetTrigger("GuardCross");   Push(guardCross); }
    public void KickGuardKB()     { if (demo) demo.KickGuardMove();    else anim.SetTrigger("KickGuard");    StartKickSeq("KICK_GUARD", kickGuard, 1f); }
    public void KickGuardLowKB()  { if (demo) demo.KickGuardLowMove(); else anim.SetTrigger("KickGuardLow"); StartKickSeq("KICK_GUARD_LOW", kickGuardLow, 0.7f); }

    void StartKickSeq(string state, Setting s, float joltScale)
    {
        if (kickSeq != null) StopCoroutine(kickSeq);
        anim.speed = 1f;
        kickSeq = StartCoroutine(KickGuardSequence(state, s, joltScale));
    }

    bool InState(string state)
    {
        if (anim.GetCurrentAnimatorStateInfo(0).IsName(state)) return true;
        return anim.IsInTransition(0) && anim.GetNextAnimatorStateInfo(0).IsName(state);
    }

    float StateTime(string state)
    {
        var curInfo = anim.GetCurrentAnimatorStateInfo(0);
        if (curInfo.IsName(state)) return curInfo.normalizedTime;
        var next = anim.GetNextAnimatorStateInfo(0);
        return next.IsName(state) ? next.normalizedTime : 1f;
    }

    // 1) 다리를 빠르게 올리고 2) 잠깐 버티다 정강이에 맞아 멈칫(히트스톱 + 정강이 젖혀짐) 3) 그다음 뒤로 밀림
    System.Collections.IEnumerator KickGuardSequence(string state, Setting s, float joltScale)
    {
        float wait = 0f;
        while (!InState(state) && wait < 0.3f) { wait += Time.deltaTime; yield return null; }
        if (!InState(state)) { kickSeq = null; yield break; }
        holdState = state; holdInit = false; holdActive = true; holdAfterImpact = false;

        anim.speed = kickGuardRaiseSpeed;
        while (InState(state) && StateTime(state) < kickGuardLegUpAt) yield return null;
        anim.speed = 1f;

        if (kickGuardImpactWait > 0f) yield return new WaitForSeconds(kickGuardImpactWait);

        // 정강이에 맞는 순간
        joltStart = Time.time;
        joltAmount = shinJolt * joltScale;
        anim.speed = 0f;
        if (kickGuardHitStop > 0f) yield return new WaitForSeconds(kickGuardHitStop);
        anim.speed = 1f;

        // 맞은 뒤에 밀림 (대기 시간 없이 바로)
        Push(s);
        timer = s.delay;
        holdAfterImpact = true;

        // 다리를 내리고 IDLE로 돌아갈 때, 몸이 뒤로 빠지는 IDLE 앞부분을 건너뛰고 중립 자세 지점부터 이어서 재생
        while (InState(state) && !anim.IsInTransition(0) && StateTime(state) < kickGuardReturnAt) yield return null;
        if (InState(state) && !anim.IsInTransition(0))
            anim.CrossFade("IDLE", 0.2f, 0, kickGuardReturnIdlePhase);
        kickSeq = null;
    }

    // 지금 애니메이션에서 킥 가드 클립 때문에 엉덩이가 얼마나 뒤로 빠져 있는지 (음수 = 뒤). 전환 블렌드 비율까지 반영
    float HoldDrift()
    {
        var curve = holdState == "KICK_GUARD_LOW" ? kickGuardLowHipsDrift : kickGuardHipsDrift;
        if (curve == null || curve.length == 0) return 0f;
        var c = anim.GetCurrentAnimatorStateInfo(0);
        bool tr = anim.IsInTransition(0);
        float w, nt;
        if (c.IsName(holdState)) { nt = c.normalizedTime; w = tr ? 1f - anim.GetAnimatorTransitionInfo(0).normalizedTime : 1f; }
        else if (tr && anim.GetNextAnimatorStateInfo(0).IsName(holdState)) { nt = anim.GetNextAnimatorStateInfo(0).normalizedTime; w = anim.GetAnimatorTransitionInfo(0).normalizedTime; }
        else return 0f;
        return curve.Evaluate(Mathf.Clamp01(nt)) * Mathf.Clamp01(w);
    }

    
// 정강이 충격 곡선: 0.03초에 확 젖혀지고, 히트스톱 동안 버티다가, 0.25초에 걸쳐 돌아옴
    float JoltPulse()
    {
        float t = Time.time - joltStart;
        if (t < 0f) return 0f;
        if (t < 0.03f) return t / 0.03f;
        t -= 0.03f + kickGuardHitStop;
        if (t < 0f) return 1f;
        if (t > 0.25f) return 0f;
        float k = t / 0.25f;
        return 1f - k * k * (3f - 2f * k);
    }

    void OnDisable()
    {
        if (anim) anim.speed = 1f;
    }

    public void ResetPosition()
    {
        if (kickSeq != null) { StopCoroutine(kickSeq); kickSeq = null; }
        anim.speed = 1f;
        joltStart = -10f;
        holdActive = false;
        active = false;
        transform.SetPositionAndRotation(homePos, homeRot);
        lastOffL = lastOffR = residualL = residualR = Vector3.zero;
    }

    /// <summary>다른 스크립트(실제 대전)에서 방향을 지정해 밀 때 사용. dir = 맞은 쪽에서 밀려나는 방향</summary>
    public void Push(Setting s, Vector3? dir = null)
    {
        // 밀리는 도중 또 맞으면, 지금 발 위치를 이어받아 자연스럽게 다시 딛게 함
        residualL = active ? lastOffL : Vector3.zero;
        residualR = active ? lastOffR : Vector3.zero;

        cur = s;
        Vector3 d = dir ?? -transform.forward;
        d.y = 0f;
        pushDir = d.sqrMagnitude > 1e-4f ? d.normalized : -transform.forward;
        kbStart = transform.position;
        disp = Vector3.zero;
        timer = 0f;
        u = 0f;
        rearDecided = false;
        active = true;
    }

    void Update()
    {
        ReadKeys();

        if (!active) return;
        timer += Time.deltaTime;
        u = Mathf.Clamp01((timer - cur.delay) / Mathf.Max(0.01f, cur.duration));
        float eased = 1f - Mathf.Pow(1f - u, pushSharpness); // 처음에 확 밀리고 점점 멈춤
        disp = pushDir * (cur.distance * strength * eased);
        transform.position = kbStart + disp;
        if (u >= 1f && timer > cur.delay)
        {
            active = false;
            lastOffL = lastOffR = residualL = residualR = Vector3.zero;
        }
    }

    void ReadKeys()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb.digit5Key.wasPressedThisFrame) HitHeadKB();
        if (kb.digit6Key.wasPressedThisFrame) HitStomachKB();
        if (kb.digit7Key.wasPressedThisFrame) HitLegKB();
        if (kb.digit8Key.wasPressedThisFrame) HitJabKB();
        if (kb.digit9Key.wasPressedThisFrame) GuardJabKB();
        if (kb.digit0Key.wasPressedThisFrame) GuardCrossKB();
        if (kb.nKey.wasPressedThisFrame) KickGuardKB();
        if (kb.mKey.wasPressedThisFrame) KickGuardLowKB();
        if (kb.rKey.wasPressedThisFrame) ResetPosition();
#else
        if (Input.GetKeyDown(KeyCode.Alpha5)) HitHeadKB();
        if (Input.GetKeyDown(KeyCode.Alpha6)) HitStomachKB();
        if (Input.GetKeyDown(KeyCode.Alpha7)) HitLegKB();
        if (Input.GetKeyDown(KeyCode.Alpha8)) HitJabKB();
        if (Input.GetKeyDown(KeyCode.Alpha9)) GuardJabKB();
        if (Input.GetKeyDown(KeyCode.Alpha0)) GuardCrossKB();
        if (Input.GetKeyDown(KeyCode.N)) KickGuardKB();
        if (Input.GetKeyDown(KeyCode.M)) KickGuardLowKB();
        if (Input.GetKeyDown(KeyCode.R)) ResetPosition();
#endif
    }

    static float Step(float x, Vector2 w)
    {
        float t = Mathf.Clamp01((x - w.x) / Mathf.Max(0.001f, w.y - w.x));
        return t * t * (3f - 2f * t);
    }

    // 몸은 뒤로 밀리는데 발은 바닥에 붙어 있다가 → 뒷발 먼저, 앞발 나중에 원래 자리로 딛는다
    // 몸은 뒤로 밀리는데 발은 바닥에 붙어 있다가 → 뒷발 먼저, 앞발 나중에 원래 자리로 딛는다
    void OnAnimatorIK(int layerIndex)
    {
        if (layerIndex != 0) return;
        lastBodyLocalZ = Vector3.Dot(anim.bodyPosition - transform.position, transform.forward);
        float jolt = JoltPulse() * joltAmount;
        bool pushing = active && cur != null;
        if (holdActive && holdInit && !InState(holdState)) holdActive = false;
        if (!pushing && jolt <= 0f && !holdActive) return;

        Vector3 lPos = anim.GetIKPosition(AvatarIKGoal.LeftFoot);
        Vector3 rPos = anim.GetIKPosition(AvatarIKGoal.RightFoot);

        // 킥 가드처럼 공중에 든 다리는 바닥에 붙잡지 않고 몸과 같이 움직이게
        float baseY = transform.position.y;
        float range = Mathf.Max(0.01f, liftedFootFade.y - liftedFootFade.x);
        float fadeL = 1f - Mathf.Clamp01((lPos.y - baseY - anim.leftFeetBottomHeight - liftedFootFade.x) / range);
        float fadeR = 1f - Mathf.Clamp01((rPos.y - baseY - anim.rightFeetBottomHeight - liftedFootFade.x) / range);

        Vector3 offL = Vector3.zero, offR = Vector3.zero;
        if (pushing)
        {
            if (!rearDecided)
            {
                Vector3 fwd = transform.forward;
                float lz = Vector3.Dot(lPos - transform.position, fwd);
                float rz = Vector3.Dot(rPos - transform.position, fwd);
                rearIsLeft = lz < rz - 0.02f; // 거의 나란하면 오른발을 뒷발로 (오소독스)
                rearDecided = true;
            }

            float pRear = Step(u, rearStepWindow);
            float pFront = Step(u, frontStepWindow);

            Vector3 slideRear = (-disp + (rearIsLeft ? residualL : residualR)) * (1f - pRear);
            Vector3 slideFront = (-disp + (rearIsLeft ? residualR : residualL)) * (1f - pFront);
            Vector3 liftRear = Vector3.up * cur.rearStepHeight * Mathf.Sin(pRear * Mathf.PI);
            Vector3 liftFront = Vector3.up * cur.frontStepHeight * Mathf.Sin(pFront * Mathf.PI);

            offL = (rearIsLeft ? slideRear : slideFront) * fadeL;
            offR = (rearIsLeft ? slideFront : slideRear) * fadeR;
            lastOffL = offL;
            lastOffR = offR;
            offL += (rearIsLeft ? liftRear : liftFront) * fadeL;
            offR += (rearIsLeft ? liftFront : liftRear) * fadeR;
        }

        // 킥 가드: 원본 클립이 다리 올리며 몸을 뒤로 빼는 이동을 지워서 제자리에서 다리를 올리게
        if (holdActive)
        {
            Vector3 fwd = transform.forward;
            float lz = Vector3.Dot(lPos - transform.position, fwd);
            float rz = Vector3.Dot(rPos - transform.position, fwd);
            if (!holdInit) { lZ0 = lz; rZ0 = rz; holdInit = true; }
            // 다리 올릴 땐 완전히 제자리 → 맞고 밀리는 동안에만 (설정 시) 체중이 디딤발 쪽으로 넘어감
            float ratio = holdAfterImpact ? Mathf.Lerp(kickGuardRaiseHold, kickGuardStayInPlace, active ? u : 1f) : kickGuardRaiseHold;
            // 미리 뽑아둔 곡선으로 지금 프레임의 뒤로 빠짐을 정확히 지움 (측정 지연 없음, 끝날 때 튀지 않음)
            Vector3 bodyFix = fwd * (-HoldDrift() * ratio);
            Vector3 sink = Vector3.down * (Mathf.Max(0f, Vector3.Dot(bodyFix, fwd)) * kickGuardSink);
            anim.bodyPosition += bodyFix + sink;
            // 디딤발은 처음 자리에 그대로, 든 다리는 몸과 같이 앞으로
            offL += Vector3.Lerp(bodyFix + sink, fwd * ((lZ0 - lz) * ratio), fadeL);
            offR += Vector3.Lerp(bodyFix + sink, fwd * ((rZ0 - rz) * ratio), fadeR);
        }

        // 정강이로 막는 순간: 들고 있는 다리만 뒤로 젖혀짐
        if (jolt > 0f)
        {
            Vector3 back = -transform.forward * jolt;
            offL += back * (1f - fadeL);
            offR += back * (1f - fadeR);
        }

        anim.SetIKPositionWeight(AvatarIKGoal.LeftFoot, 1f);
        anim.SetIKPositionWeight(AvatarIKGoal.RightFoot, 1f);
        anim.SetIKPosition(AvatarIKGoal.LeftFoot, lPos + offL);
        anim.SetIKPosition(AvatarIKGoal.RightFoot, rPos + offR);
    }

    // 맞는 순간 상체가 살짝 뒤로 젖혀졌다 돌아오는 반동
    // 맞는 순간 상체가 살짝 뒤로 젖혀졌다 돌아오는 반동 (+ 정강이 막을 때 움찔)
    void LateUpdate()
    {
        if (!hipsT && anim) hipsT = anim.GetBoneTransform(HumanBodyBones.Hips);
        if (hipsT) hbOffset = Vector3.Dot(hipsT.position - transform.position, transform.forward) - lastBodyLocalZ;
        float ang = 0f;
        if (active && cur != null && cur.recoilAngle > 0f)
        {
            float pulse = u < 0.25f ? Mathf.Sin(u / 0.25f * Mathf.PI * 0.5f) : 1f - Step(u, new Vector2(0.25f, 0.8f));
            ang -= cur.recoilAngle * pulse;
        }
        ang -= shinImpactFlinch * JoltPulse() * (joltAmount > 0f ? 1f : 0f);
        if (Mathf.Abs(ang) < 0.01f) return;
        Vector3 axis = transform.right;
        if (spine) spine.rotation = Quaternion.AngleAxis(ang * 0.4f, axis) * spine.rotation;
        if (chest) chest.rotation = Quaternion.AngleAxis(ang * 0.6f, axis) * chest.rotation;
    }

    void OnGUI()
    {
        if (!showButtons) return;
        // HanbokDemoControl 버튼 열 바로 오른쪽에 같은 크기로 배치
        float s = Mathf.Clamp(Screen.height / 900f, 0.4f, 1.25f);
        float w = 230 * s, h = 46 * s, gap = 6 * s, head = 34 * s;
        float x = 20 * s + 210 * s + 16 * s, y = 20 * s;
        GUI.Box(new Rect(x, y, w, head), "피격 + 넉백 (철권식)");
        y += head + gap;
        if (GUI.Button(new Rect(x, y, w, h), "HIT HEAD +넉백 (5)")) HitHeadKB();
        if (GUI.Button(new Rect(x, y += h + gap, w, h), "HIT STOMACH +넉백 (6)")) HitStomachKB();
        if (GUI.Button(new Rect(x, y += h + gap, w, h), "HIT LEG +넉백 (7)")) HitLegKB();
        if (GUI.Button(new Rect(x, y += h + gap, w, h), "HIT JAB +넉백 (8)")) HitJabKB();
        y += h + gap * 3;
        GUI.Box(new Rect(x, y, w, head), "가드 + 밀림 (약하게)");
        y += head + gap;
        if (GUI.Button(new Rect(x, y, w, h), "GUARD JAB +밀림 (9)")) GuardJabKB();
        if (GUI.Button(new Rect(x, y += h + gap, w, h), "GUARD CROSS +밀림 (0)")) GuardCrossKB();
        if (GUI.Button(new Rect(x, y += h + gap, w, h), "KICK GUARD +밀림 (N)")) KickGuardKB();
        if (GUI.Button(new Rect(x, y += h + gap, w, h), "KICK GUARD LOW +밀림 (M)")) KickGuardLowKB();
        y += gap * 2;
        if (GUI.Button(new Rect(x, y += h + gap, w, h), "위치 리셋 (R)")) ResetPosition();
    }
}
