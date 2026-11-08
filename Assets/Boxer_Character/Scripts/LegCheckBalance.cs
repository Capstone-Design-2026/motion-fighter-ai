using UnityEngine;

/// <summary>
/// 로우킥 막기(KICK_GUARD_LOW) 때 한 발로 서는 뒷다리를 실제 사람처럼 정리한다.
/// 원래 동작은 뒷발이 몸보다 한참 뒤에 있고, 무릎·발목이 모두 꺾인 채(지그재그) 한 발로 서 있어서 불안정해 보인다.
/// 앞다리를 드는 동안:
///  - 뒷발을 골반 바로 밑으로 옮겨 딛고 (옮기는 순간만 살짝 들었다 놓음)
///  - 몸을 살짝 올려서 뒷다리가 거의 곧게 펴지게 하고 (무릎만 약간 굽음)
///  - 뒷발은 바닥에 평평하게, 발끝은 약간 바깥을 보게, 무릎은 발끝 방향을 보게 한다
///  - 앞다리는 너무 높이 들지 않게 줄인다
/// FighterStance 다음, StanceFootAlign 전에 실행.
/// </summary>
[DefaultExecutionOrder(-90)]
[RequireComponent(typeof(Animator))]
public class LegCheckBalance : MonoBehaviour
{
    public string stateName = "KICK_GUARD_LOW";

    [Header("몸 위치")]
    [Tooltip("골반을 뒤로 옮기는 거리 (미터)")]
    [Range(0f, 0.4f)] public float shiftBack = 0.12f;
    [Tooltip("골반을 뒷발 쪽 옆으로 옮기는 거리 (미터)")]
    [Range(0f, 0.15f)] public float shiftSide = 0.05f;

    [Header("받치는 뒷다리")]
    [Tooltip("뒷발을 골반 밑에서 얼마나 뒤에 딛을지 (미터). 0이면 골반 바로 밑")]
    [Range(-0.1f, 0.2f)] public float rearBehind = 0.05f;
    [Tooltip("뒷발을 몸 중심선 쪽으로 당기는 정도 (0=고관절 바로 밑, 1=몸 중심 밑). 한 발로 설 때는 발이 몸 중심 가까이 와야 안정적")]
    [Range(0f, 1f)] public float rearInward = 0.4f;
    [Tooltip("뒷다리를 펴는 정도. 1이면 완전히 곧게, 0.95면 무릎이 살짝 굽음")]
    [Range(0.85f, 1f)] public float rearStraight = 0.95f;
    [Tooltip("뒷발 발끝이 바깥을 보는 각도")]
    [Range(0f, 60f)] public float rearToeOut = 25f;
    [Tooltip("뒷발을 옮길 때 살짝 들었다 놓는 높이 (미터)")]
    [Range(0f, 0.08f)] public float rearStepLift = 0.04f;

    [Header("들어 올린 앞다리")]
    [Tooltip("앞다리를 드는 높이 비율. 1이면 원래 동작 그대로")]
    [Range(0.3f, 1f)] public float liftScale = 0.65f;
    [Tooltip("들어 올린 앞다리가 몸을 따라 뒤로 가는 비율")]
    [Range(0f, 1f)] public float frontFollow = 0.6f;

    [Header("상체")]
    [Tooltip("상체를 세우는 각도")]
    [Range(0f, 20f)] public float torsoUpright = 4f;

    [Header("타이밍 (동작 진행도 0~1)")]
    public Vector2 shiftIn = new Vector2(0.08f, 0.32f);
    public Vector2 shiftOut = new Vector2(0.70f, 0.97f);

    [Tooltip("뒷발이 오른발이면 켬 (오소독스 자세)")]
    public bool rearIsRight = true;

    Animator anim;
    FighterStance stance;
    Transform spine, chest, rUpper, rLower, rFoot;
    int hash;
    Vector3 hipOffsetLocal;   // 몸 중심(bodyPosition) → 뒷다리 고관절 (평소 자세에서 잰 값)
    float legLen;
    bool calibrated;
    Vector3 bodyPositionForCalibration;
    int bodyPositionFrame = -1;
    public float Weight { get; private set; }

    void Awake()
    {
        anim = GetComponent<Animator>();
        stance = GetComponent<FighterStance>();
        hash = Animator.StringToHash(stateName);
        spine = anim.GetBoneTransform(HumanBodyBones.Spine);
        chest = anim.GetBoneTransform(HumanBodyBones.Chest);
        rUpper = anim.GetBoneTransform(rearIsRight ? HumanBodyBones.RightUpperLeg : HumanBodyBones.LeftUpperLeg);
        rLower = anim.GetBoneTransform(rearIsRight ? HumanBodyBones.RightLowerLeg : HumanBodyBones.LeftLowerLeg);
        rFoot = anim.GetBoneTransform(rearIsRight ? HumanBodyBones.RightFoot : HumanBodyBones.LeftFoot);
    }

    static float Smooth(float a, float b, float x)
    {
        float t = Mathf.Clamp01((x - a) / Mathf.Max(1e-4f, b - a));
        return t * t * (3f - 2f * t);
    }

    float Envelope()
    {
        var cur = anim.GetCurrentAnimatorStateInfo(0);
        float nt;
        if (anim.IsInTransition(0))
        {
            var next = anim.GetNextAnimatorStateInfo(0);
            if (next.shortNameHash == hash) nt = next.normalizedTime;
            else if (cur.shortNameHash == hash) nt = cur.normalizedTime;
            else return 0f;
        }
        else if (cur.shortNameHash == hash) nt = cur.normalizedTime;
        else return 0f;
        nt = Mathf.Clamp01(nt);
        return Smooth(shiftIn.x, shiftIn.y, nt) * (1f - Smooth(shiftOut.x, shiftOut.y, nt));
    }

    void OnAnimatorIK(int layerIndex)
    {
        if (layerIndex != 0) return;
        // Animator.bodyPosition is an IK-pass API. Cache it here for the
        // final bone positions sampled in LateUpdate, without querying it there.
        bodyPositionForCalibration = anim.bodyPosition;
        bodyPositionFrame = Time.frameCount;
        Weight = Envelope();
        float w = Weight;
        if (w <= 0f) return;

        Vector3 fwd = transform.forward;
        Vector3 side = transform.right * (rearIsRight ? 1f : -1f);
        float crouch = stance != null && stance.enabled ? stance.crouch * stance.weight : 0f;

        // 1) 몸을 뒤·뒷발 쪽으로
        Vector3 shift = (-fwd * shiftBack + side * shiftSide) * w;
        anim.bodyPosition += shift;

        // 2) 받치는 뒷발을 골반 밑으로
        var rear = rearIsRight ? AvatarIKGoal.RightFoot : AvatarIKGoal.LeftFoot;
        var rearHint = rearIsRight ? AvatarIKHint.RightKnee : AvatarIKHint.LeftKnee;
        if (!calibrated) return;
        Vector3 hipJ = anim.bodyPosition + transform.TransformVector(hipOffsetLocal);
        Vector3 rp = anim.GetIKPosition(rear);
        Vector3 under = Vector3.Lerp(hipJ, anim.bodyPosition, rearInward) - fwd * rearBehind;
        under.y = rp.y;
        Vector3 target = Vector3.Lerp(rp, under, w);
        target.y += Mathf.Sin(w * Mathf.PI) * rearStepLift;

        // 3) 뒷다리가 거의 곧게 펴지도록 몸 높이 조절
        Vector3 d = hipJ - target; float horiz = new Vector2(d.x, d.z).magnitude;
        float reach = legLen * rearStraight;
        float wantY = target.y + Mathf.Sqrt(Mathf.Max(0f, reach * reach - horiz * horiz));
        float dy = Mathf.Clamp(wantY - hipJ.y, -0.06f, 0.15f) * w;
        anim.bodyPosition += Vector3.up * dy;

        // 4) 뒷발 평평하게 + 발끝 약간 바깥, 무릎은 발끝 방향
        Quaternion flat = transform.rotation * Quaternion.Euler(0f, rearIsRight ? rearToeOut : -rearToeOut, 0f);
        anim.SetIKPositionWeight(rear, 1f);
        anim.SetIKRotationWeight(rear, 1f);
        anim.SetIKPosition(rear, target);
        anim.SetIKRotation(rear, Quaternion.Slerp(anim.GetIKRotation(rear), flat, w));
        anim.SetIKHintPosition(rearHint, target + Vector3.up * 0.45f + (flat * Vector3.forward) * 0.5f);
        anim.SetIKHintPositionWeight(rearHint, w);

        // 5) 들어 올린 앞다리: 덜 높게, 몸을 따라감
        var front = rearIsRight ? AvatarIKGoal.LeftFoot : AvatarIKGoal.RightFoot;
        Vector3 fp = anim.GetIKPosition(front);
        float planted = transform.position.y + (rearIsRight ? anim.leftFeetBottomHeight : anim.rightFeetBottomHeight);
        if (fp.y > planted) fp.y = planted + (fp.y - planted) * Mathf.Lerp(1f, liftScale, w);
        fp += new Vector3(shift.x, dy, shift.z) * frontFollow;
        anim.SetIKPositionWeight(front, 1f);
        anim.SetIKPosition(front, fp);
    }

    void LateUpdate()
    {
        // 평소 자세일 때 고관절 위치와 다리 길이를 재 둠 (OnAnimatorIK 안에서는 뼈 위치가 이전 프레임 값이라 직접 못 씀)
        if (Weight <= 0.001f && bodyPositionFrame == Time.frameCount && rUpper && rLower && rFoot)
        {
            hipOffsetLocal = transform.InverseTransformVector(rUpper.position - bodyPositionForCalibration);
            legLen = Vector3.Distance(rUpper.position, rLower.position) + Vector3.Distance(rLower.position, rFoot.position);
            calibrated = true;
        }
        if (Weight <= 0f || torsoUpright <= 0f) return;
        float a = -torsoUpright * 0.5f * Weight;
        Vector3 axis = transform.right;
        if (spine != null) spine.rotation = Quaternion.AngleAxis(a, axis) * spine.rotation;
        if (chest != null) chest.rotation = Quaternion.AngleAxis(a, axis) * chest.rotation;
    }
}
