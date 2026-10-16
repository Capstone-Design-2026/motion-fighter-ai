using UnityEngine;

/// <summary>
/// 펀치 궤적을 가드 위치에서 타격점까지 곧게 뻗도록 맞춘다.
/// 펀치가 시작될 때 가드 위치를 기억하고, 주먹이 앞으로 나간 거리만큼
/// 가드 높이 → 타격 높이로 이어지는 직선을 따라가게 한다. 회수할 때도 같은 선을 따라 돌아온다.
/// </summary>
[DefaultExecutionOrder(50)]
[RequireComponent(typeof(Animator))]
public class PunchAim : MonoBehaviour
{
    [System.Serializable]
    public class Punch
    {
        [Tooltip("애니메이터 상태 이름")] public string stateName = "JAB";
        [Tooltip("이 펀치를 치는 손")] public bool leftHand = true;
        [Tooltip("이 펀치만 높이 추가 보정 (미터). +면 더 높게")] public float extraHeight = 0f;
        [HideInInspector] public int hash;
    }

    [Tooltip("보정할 펀치 목록")]
    public Punch[] punches =
    {
        new Punch { stateName = "JAB", leftHand = true },
        new Punch { stateName = "CROSS", leftHand = false },
    };

    [Tooltip("노릴 대상 (상대 캐릭터의 머리 본 등). 비워두면 내 머리 높이 기준으로 노린다")]
    public Transform target;

    [Tooltip("머리 높이 기준 타격 높이 보정 (미터). -0.05 = 턱 높이, 0 = 머리, -0.25 = 몸통")]
    public float aimHeightOffset = -0.05f;

    [Tooltip("궤적 모양. 1 = 직선, 1보다 작으면 초반에 빨리 올라감, 크면 끝에서 올라감")]
    [Range(0.3f, 2f)] public float pathCurve = 0.8f;

    [Tooltip("보정 강도. 0이면 원래 동작 그대로")]
    [Range(0f, 1f)] public float weight = 1f;

    Animator anim;
    Transform head, lUpper, rUpper, lLower, rLower, lHand, rHand;
    float lArmLen, rArmLen;

    // 지금 진행 중인 펀치
    Punch active;
    float guardHeightFromHead; // 펀치 시작 때 주먹 높이 (머리 기준)
    float guardReach;          // 펀치 시작 때 어깨~주먹 거리

    void Awake()
    {
        anim = GetComponent<Animator>();
        head = anim.GetBoneTransform(HumanBodyBones.Head);
        lUpper = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        rUpper = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
        lLower = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        rLower = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
        lHand = anim.GetBoneTransform(HumanBodyBones.LeftHand);
        rHand = anim.GetBoneTransform(HumanBodyBones.RightHand);
        lArmLen = Vector3.Distance(lUpper.position, lLower.position) + Vector3.Distance(lLower.position, lHand.position);
        rArmLen = Vector3.Distance(rUpper.position, rLower.position) + Vector3.Distance(rLower.position, rHand.position);
        foreach (var p in punches) p.hash = Animator.StringToHash(p.stateName);
    }

    Punch Find(int hash)
    {
        foreach (var p in punches) if (p.hash == hash) return p;
        return null;
    }

    void LateUpdate()
    {
        if (head == null) return;

        // 지금(또는 전환해 들어가는 중인) 펀치 상태 찾기
        Punch p = anim.IsInTransition(0) ? Find(anim.GetNextAnimatorStateInfo(0).shortNameHash) : null;
        if (p == null) p = Find(anim.GetCurrentAnimatorStateInfo(0).shortNameHash);

        if (p == null) { active = null; return; }

        Transform upper = p.leftHand ? lUpper : rUpper;
        Transform hand = p.leftHand ? lHand : rHand;
        float armLen = p.leftHand ? lArmLen : rArmLen;

        // 새 펀치가 시작되면 그 순간의 가드 위치를 기억
        if (active != p)
        {
            active = p;
            guardHeightFromHead = hand.position.y - head.position.y;
            guardReach = Vector3.Distance(hand.position, upper.position);
        }
        if (weight <= 0f) return;

        // 팔이 가드 상태에서 다 펴진 상태까지 얼마나 펴졌는지 (0~1)
        float reach = Vector3.Distance(hand.position, upper.position);
        float progress = Mathf.Clamp01((reach - guardReach) / Mathf.Max(0.05f, armLen * 0.97f - guardReach));
        progress = Mathf.Pow(progress, pathCurve);

        float headY = head.position.y;
        float aimY = (target != null ? target.position.y : headY + aimHeightOffset) + p.extraHeight;
        float lineY = Mathf.Lerp(headY + guardHeightFromHead, aimY, progress);
        float goalY = Mathf.Lerp(hand.position.y, lineY, weight);

        // 어깨를 축으로 팔을 돌려 주먹 높이를 맞춘다 (몇 번 반복해서 수렴)
        for (int i = 0; i < 4; i++)
        {
            float dy = goalY - hand.position.y;
            if (Mathf.Abs(dy) < 0.003f) break;
            Vector3 from = hand.position - upper.position;
            Vector3 to = from + Vector3.up * dy;
            upper.rotation = Quaternion.FromToRotation(from, to) * upper.rotation;
        }
    }
}
