using UnityEngine;

/// <summary>
/// 대기 자세 발 방향 정리.
/// 믹사모 Boxing_idle은 뒷발 발끝이 몸 바깥쪽으로 60~75° 돌아가 있고 무릎은 30° 정도만 돌아가 있어서
/// 발목이 비틀린 것처럼 보인다 (부츠 신은 캐릭터에서 특히 티가 남).
/// 바닥에 붙어 있는 발만:
///  1) 뒷발이 maxRearOutward보다 많이 바깥으로 돌아가 있으면 그만큼 안쪽으로 돌림 (발목 기준)
///  2) 무릎이 발끝과 같은 방향을 보도록 무릎 힌트를 줌
/// 발이 떠 있으면(킥 등) 효과가 사라진다. FighterStance(-100)·HitKnockback(50) 다음에 실행.
/// </summary>
[DefaultExecutionOrder(60)]
[RequireComponent(typeof(Animator))]
public class StanceFootAlign : MonoBehaviour
{
    [Range(0f, 1f)] public float weight = 1f;
    [Tooltip("뒷발 발끝이 몸 정면에서 바깥으로 돌아갈 수 있는 최대 각도")]
    [Range(0f, 90f)] public float maxRearOutward = 40f;
    [Tooltip("앞발 발끝 최대 바깥 각도")]
    [Range(0f, 90f)] public float maxFrontOutward = 25f;
    [Tooltip("무릎을 발끝 방향으로 맞추는 정도")]
    [Range(0f, 1f)] public float kneeFollow = 0.8f;
    [Tooltip("발이 이 높이(미터)보다 뜨면 보정을 줄임")]
    public float footLiftFade = 0.12f;

    Animator anim;
    LegCheckBalance legCheck;
    Transform lFoot, rFoot, lToe, rToe, lKnee, rKnee;

    void Awake()
    {
        anim = GetComponent<Animator>();
        legCheck = GetComponent<LegCheckBalance>();
        lFoot = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
        rFoot = anim.GetBoneTransform(HumanBodyBones.RightFoot);
        lToe = anim.GetBoneTransform(HumanBodyBones.LeftToes);
        rToe = anim.GetBoneTransform(HumanBodyBones.RightToes);
        lKnee = anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        rKnee = anim.GetBoneTransform(HumanBodyBones.RightLowerLeg);
    }

    void OnAnimatorIK(int layerIndex)
    {
        if (layerIndex != 0 || weight <= 0f || !lFoot || !rFoot || !lToe || !rToe) return;
        Vector3 fwd = transform.forward;
        float lz = Vector3.Dot(anim.GetIKPosition(AvatarIKGoal.LeftFoot) - transform.position, fwd);
        float rz = Vector3.Dot(anim.GetIKPosition(AvatarIKGoal.RightFoot) - transform.position, fwd);
        bool leftRear = lz < rz;
        Fix(AvatarIKGoal.LeftFoot, AvatarIKHint.LeftKnee, lFoot, lToe, lKnee, -1f, leftRear ? maxRearOutward : maxFrontOutward, anim.leftFeetBottomHeight);
        Fix(AvatarIKGoal.RightFoot, AvatarIKHint.RightKnee, rFoot, rToe, rKnee, 1f, leftRear ? maxFrontOutward : maxRearOutward, anim.rightFeetBottomHeight);
    }

    // side: 왼발 -1, 오른발 +1 (바깥쪽 = transform.right * side)
    void Fix(AvatarIKGoal goal, AvatarIKHint hint, Transform foot, Transform toe, Transform knee, float side, float maxOut, float bottom)
    {
        Vector3 pos = anim.GetIKPosition(goal);
        float lift = pos.y - (transform.position.y + bottom);
        float fade = (1f - Mathf.Clamp01(lift / Mathf.Max(0.01f, footLiftFade))) * weight;
        // 로우킥 막기 중에는 LegCheckBalance가 발·무릎 방향을 정하므로 그만큼 빠짐
        if (legCheck != null && legCheck.enabled) fade *= 1f - legCheck.Weight;
        if (fade <= 0f) return;

        Vector3 fwd = transform.forward;
        Vector3 toeDir = Vector3.ProjectOnPlane(toe.position - foot.position, Vector3.up);
        if (toeDir.sqrMagnitude < 1e-6f) return;
        float yaw = Vector3.SignedAngle(fwd, toeDir, Vector3.up);   // + = 오른쪽
        float outward = yaw * side;                                  // + = 몸 바깥쪽
        float target = Mathf.Min(outward, maxOut);
        float delta = (target - outward) * side * fade;              // 돌릴 각도 (월드 Y축)

        Quaternion q = Quaternion.AngleAxis(delta, Vector3.up);
        anim.SetIKRotationWeight(goal, 1f);
        anim.SetIKRotation(goal, q * anim.GetIKRotation(goal));

        if (knee && kneeFollow > 0f)
        {
            Vector3 dir = (q * toeDir).normalized;
            anim.SetIKHintPosition(hint, knee.position + dir * 0.5f);
            anim.SetIKHintPositionWeight(hint, kneeFollow * fade);
        }
    }
}
