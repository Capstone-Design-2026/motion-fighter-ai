using UnityEngine;

/// <summary>
/// 모든 동작 위에 파이터다운 자세를 덧씌운다.
/// - 무게중심 낮추기 (발은 바닥에 고정한 채 무릎이 더 굽혀짐)
/// - 발 폭 넓히기
/// - 상체를 상대 쪽으로 기울이기 (턱이 자연스럽게 당겨짐)
/// - 어깨 올려서 턱 보호
/// Animator 레이어의 IK Pass가 켜져 있어야 한다. 다른 팔 보정 스크립트보다 먼저 실행된다.
/// </summary>
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(Animator))]
public class FighterStance : MonoBehaviour
{
    [Tooltip("전체 적용 강도. 0이면 원래 동작 그대로")]
    [Range(0f, 1f)] public float weight = 1f;

    [Header("하체")]
    [Tooltip("무게중심을 낮추는 정도 (미터). 클수록 무릎이 더 굽혀짐")]
    [Range(0f, 0.2f)] public float crouch = 0.07f;

    [Tooltip("발 하나당 바깥으로 벌리는 거리 (미터)")]
    [Range(0f, 0.15f)] public float widenPerFoot = 0.05f;

    [Tooltip("발이 이 높이보다 높이 뜨면 (킥 등) 발 폭 보정을 줄인다")]
    public float footLiftFade = 0.15f;

    [Header("상체")]
    [Tooltip("상체를 앞으로 기울이는 각도")]
    [Range(0f, 25f)] public float forwardLean = 8f;

    [Tooltip("어깨를 올리는 높이 (미터). 턱 보호하는 느낌")]
    [Range(0f, 0.06f)] public float shoulderShrug = 0.02f;

    Animator anim;
    Transform hips, spine, chest, lShoulder, rShoulder, lUpper, rUpper;

    void Awake()
    {
        anim = GetComponent<Animator>();
        hips = anim.GetBoneTransform(HumanBodyBones.Hips);
        spine = anim.GetBoneTransform(HumanBodyBones.Spine);
        chest = anim.GetBoneTransform(HumanBodyBones.Chest);
        lShoulder = anim.GetBoneTransform(HumanBodyBones.LeftShoulder);
        rShoulder = anim.GetBoneTransform(HumanBodyBones.RightShoulder);
        lUpper = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        rUpper = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
    }

    void OnAnimatorIK(int layerIndex)
    {
        if (weight <= 0f) return;

        // 애니메이션 원래 발 위치를 먼저 기억해두고
        Vector3 lFoot = anim.GetIKPosition(AvatarIKGoal.LeftFoot);
        Vector3 rFoot = anim.GetIKPosition(AvatarIKGoal.RightFoot);
        Quaternion lRot = anim.GetIKRotation(AvatarIKGoal.LeftFoot);
        Quaternion rRot = anim.GetIKRotation(AvatarIKGoal.RightFoot);

        // 몸을 낮춘 뒤
        Vector3 body = anim.bodyPosition;
        body.y -= crouch * weight;
        anim.bodyPosition = body;

        // 발 폭 넓히기 (몸 중심선 기준으로 각 발을 바깥쪽으로)
        Vector3 right = transform.right;
        float baseY = transform.position.y;
        lFoot += Widen(lFoot, body, right, baseY + anim.leftFeetBottomHeight);
        rFoot += Widen(rFoot, body, right, baseY + anim.rightFeetBottomHeight);

        // 발은 원래 자리에 고정 → 무릎이 굽혀지면서 자세가 낮아짐
        anim.SetIKPositionWeight(AvatarIKGoal.LeftFoot, 1f);
        anim.SetIKPositionWeight(AvatarIKGoal.RightFoot, 1f);
        anim.SetIKRotationWeight(AvatarIKGoal.LeftFoot, 1f);
        anim.SetIKRotationWeight(AvatarIKGoal.RightFoot, 1f);
        anim.SetIKPosition(AvatarIKGoal.LeftFoot, lFoot);
        anim.SetIKPosition(AvatarIKGoal.RightFoot, rFoot);
        anim.SetIKRotation(AvatarIKGoal.LeftFoot, lRot);
        anim.SetIKRotation(AvatarIKGoal.RightFoot, rRot);
    }

    Vector3 Widen(Vector3 foot, Vector3 body, Vector3 right, float plantedY)
    {
        // plantedY = 발이 바닥에 붙어 있을 때의 발목 높이
        float lift = foot.y - plantedY;
        float fade = 1f - Mathf.Clamp01(lift / Mathf.Max(0.01f, footLiftFade));
        float sideSign = Mathf.Sign(Vector3.Dot(foot - body, right));
        return right * sideSign * widenPerFoot * fade * weight;
    }

    void LateUpdate()
    {
        if (weight <= 0f) return;

        // 상체 앞으로 기울이기: 허리와 가슴에 나눠서 자연스럽게
        float lean = forwardLean * weight;
        Vector3 axis = transform.right;
        if (spine != null) spine.rotation = Quaternion.AngleAxis(lean * 0.5f, axis) * spine.rotation;
        if (chest != null) chest.rotation = Quaternion.AngleAxis(lean * 0.5f, axis) * chest.rotation;

        // 어깨 올리기
        float shrug = shoulderShrug * weight;
        Shrug(lShoulder, lUpper, shrug);
        Shrug(rShoulder, rUpper, shrug);
    }

    static void Shrug(Transform shoulder, Transform upper, float amount)
    {
        if (shoulder == null || upper == null || amount <= 0f) return;
        Vector3 from = upper.position - shoulder.position;
        Vector3 to = from + Vector3.up * amount;
        shoulder.rotation = Quaternion.FromToRotation(from, to) * shoulder.rotation;
    }
}
