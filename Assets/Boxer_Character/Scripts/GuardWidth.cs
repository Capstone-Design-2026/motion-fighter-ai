using UnityEngine;

/// <summary>
/// 1) IDLE 같은 가드 상태에서 양팔을 바깥으로 살짝 벌려 글러브끼리 붙어 보이지 않게 한다.
/// 2) 어떤 동작이든 손(글러브)이 몸통·얼굴에 너무 붙으면 바깥으로 밀어내서 몸에 파묻히지 않게 한다.
/// </summary>
[RequireComponent(typeof(Animator))]
public class GuardWidth : MonoBehaviour
{
    [Header("가드 자세 팔 간격")]
    [Tooltip("손 하나당 바깥으로 벌릴 거리 (미터). 0.045 = 4.5cm씩")]
    [Range(0f, 0.15f)] public float spreadPerHand = 0.045f;

    [Tooltip("팔을 벌릴 애니메이터 상태 이름")]
    public string[] guardStates = { "IDLE" };

    [Tooltip("벌어지고 돌아오는 속도. 클수록 빠르게 전환")]
    public float blendSpeed = 8f;

    [Tooltip("머리를 감싸는 방어 상태(GUARD_*)에서는 주먹이 얼굴에 바짝 붙어야 하므로 몸 파묻힘 최소 거리를 이 값으로 줄인다 (미터)")]
    [Range(0f, 0.3f)] public float guardClearance = 0.09f;

    [Header("몸 파묻힘 방지")]
    [Tooltip("손이 몸 중심선에서 최소 이만큼은 떨어져 있게 한다 (미터). 글러브가 아직 몸에 박히면 올리세요")]
    [Range(0f, 0.4f)] public float minBodyClearance = 0.19f;

    [Tooltip("밀어내기 강도. 0이면 끔")]
    [Range(0f, 1f)] public float clearanceWeight = 1f;

    Animator anim;
    Transform lUpper, rUpper, lHand, rHand, hips, head;
    int[] stateHashes;
    float weight;

    void Awake()
    {
        anim = GetComponent<Animator>();
        lUpper = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        rUpper = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
        lHand = anim.GetBoneTransform(HumanBodyBones.LeftHand);
        rHand = anim.GetBoneTransform(HumanBodyBones.RightHand);
        hips = anim.GetBoneTransform(HumanBodyBones.Hips);
        head = anim.GetBoneTransform(HumanBodyBones.Head);
        stateHashes = new int[guardStates.Length];
        for (int i = 0; i < guardStates.Length; i++) stateHashes[i] = Animator.StringToHash(guardStates[i]);
    }

    bool IsGuardState(AnimatorStateInfo info)
    {
        foreach (var h in stateHashes) if (info.shortNameHash == h) return true;
        return false;
    }

    void LateUpdate()
    {
        if (lUpper == null || rUpper == null || lHand == null || rHand == null) return;

        // 1) 가드 자세 팔 간격
        AnimatorStateInfo info = anim.IsInTransition(0) ? anim.GetNextAnimatorStateInfo(0) : anim.GetCurrentAnimatorStateInfo(0);
        float target = IsGuardState(info) ? 1f : 0f;
        weight = Mathf.MoveTowards(weight, target, blendSpeed * Time.deltaTime);
        if (weight > 0.0001f)
        {
            Vector3 side = (rUpper.position - lUpper.position).normalized;
            float d = spreadPerHand * weight;
            Rotate(lUpper, lHand, -side * d);
            Rotate(rUpper, rHand, side * d);
        }

        // 2) 몸 파묻힘 방지 (모든 동작에 적용)
        if (clearanceWeight > 0f && hips != null && head != null)
        {
            KeepClear(lUpper, lHand);
            KeepClear(rUpper, rHand);
        }
    }

    static readonly int[] GuardMotionHashes = {
        Animator.StringToHash("GUARD_JAB"), Animator.StringToHash("GUARD_CROSS"),
        Animator.StringToHash("GUARD_HIGHKICK"), Animator.StringToHash("GUARD_LOWKICK") };

    static bool IsGuardMotion(int hash)
    {
        foreach (var h in GuardMotionHashes) if (h == hash) return true;
        return false;
    }

    
void KeepClear(Transform upper, Transform hand)
    {
        // 골반~머리를 잇는 몸 중심선에서 손까지의 거리를 재서, 가까우면 바깥으로 민다.
        // 어깨를 축으로 팔을 돌리는 방식이라 한 번에 다 안 빠질 수 있어 몇 번 반복한다.
        float goal = minBodyClearance;
        var cur = anim.IsInTransition(0) ? anim.GetNextAnimatorStateInfo(0) : anim.GetCurrentAnimatorStateInfo(0);
        if (IsGuardMotion(cur.shortNameHash)) goal = Mathf.Min(goal, guardClearance);
        for (int iter = 0; iter < 6; iter++)
        {
            Vector3 a = hips.position, b = head.position, ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(hand.position - a, ab) / ab.sqrMagnitude);
            Vector3 closest = a + ab * t;
            Vector3 away = hand.position - closest;
            float dist = away.magnitude;
            if (dist >= goal - 0.002f || dist < 0.0001f) return;
            Vector3 push = away / dist * (goal - dist) * clearanceWeight;
            Rotate(upper, hand, push);
        }
    }

    static void Rotate(Transform upper, Transform hand, Vector3 offset)
    {
        Vector3 from = hand.position - upper.position;
        Vector3 to = from + offset;
        upper.rotation = Quaternion.FromToRotation(from, to) * upper.rotation;
    }
}
