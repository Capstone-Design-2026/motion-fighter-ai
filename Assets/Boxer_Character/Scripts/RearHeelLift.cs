using UnityEngine;

/// <summary>
/// 뒷발 뒤꿈치 들기 (부츠 신은 적 캐릭터용).
/// 복싱 대기 자세에서 뒷발은 앞꿈치로 서야 하는데, 발목뼈가 낮은 부츠 모델은
/// 뒤꿈치가 바닥에 붙은 채 정강이만 앞으로 기울어서 발목(부츠)이 꺾여 보인다.
/// 몸보다 뒤에 있는 발이 바닥에 붙어 있을 때, 발끝을 축으로 발을 세워 뒤꿈치를 들어준다.
/// (발끝 위치는 그대로라 발이 미끄러지지 않고, 그만큼 무릎이 덜 굽혀져 발목 꺾임이 줄어든다)
/// FighterStance(-100) 다음, HitKnockback(50) 다음에 실행.
/// </summary>
[DefaultExecutionOrder(60)]
[RequireComponent(typeof(Animator))]
public class RearHeelLift : MonoBehaviour
{
    [Tooltip("뒷발 뒤꿈치를 드는 각도")]
    [Range(0f, 35f)] public float liftAngle = 18f;
    [Tooltip("앞발도 조금 들기 (앞꿈치에 체중 싣는 느낌). 0이면 앞발은 그대로")]
    [Range(0f, 15f)] public float frontLiftAngle = 0f;
    [Tooltip("발이 이 높이(미터)보다 뜨면 (킥 등) 보정을 줄임")]
    public float footLiftFade = 0.12f;
    [Range(0f, 1f)] public float weight = 1f;

    Animator anim;
    Transform lFoot, rFoot, lToe, rToe;

    void Awake()
    {
        anim = GetComponent<Animator>();
        lFoot = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
        rFoot = anim.GetBoneTransform(HumanBodyBones.RightFoot);
        lToe = anim.GetBoneTransform(HumanBodyBones.LeftToes);
        rToe = anim.GetBoneTransform(HumanBodyBones.RightToes);
    }

    void OnAnimatorIK(int layerIndex)
    {
        if (layerIndex != 0 || weight <= 0f || !lFoot || !rFoot || !lToe || !rToe) return;
        Vector3 fwd = transform.forward;
        float lz = Vector3.Dot(anim.GetIKPosition(AvatarIKGoal.LeftFoot) - transform.position, fwd);
        float rz = Vector3.Dot(anim.GetIKPosition(AvatarIKGoal.RightFoot) - transform.position, fwd);
        bool leftIsRear = lz < rz;
        Lift(AvatarIKGoal.LeftFoot, lFoot, lToe, leftIsRear ? liftAngle : frontLiftAngle, anim.leftFeetBottomHeight);
        Lift(AvatarIKGoal.RightFoot, rFoot, rToe, leftIsRear ? frontLiftAngle : liftAngle, anim.rightFeetBottomHeight);
    }

    void Lift(AvatarIKGoal goal, Transform foot, Transform toe, float angle, float bottom)
    {
        if (angle <= 0f) return;
        Vector3 pos = anim.GetIKPosition(goal);
        Quaternion rot = anim.GetIKRotation(goal);
        float lift = pos.y - (transform.position.y + bottom);
        float fade = 1f - Mathf.Clamp01(lift / Mathf.Max(0.01f, footLiftFade));
        if (fade <= 0f) return;
        float a = angle * fade * weight;

        // 발끝(앞꿈치) 위치: 지금 발 자세에서 발목 → 발가락 뼈 벡터를 IK 목표 기준으로 옮김
        Vector3 toeOffset = toe.position - foot.position; // 애니메이션 자세 기준 (FighterStance는 발 회전을 바꾸지 않음)
        Vector3 pivot = pos + toeOffset;
        Vector3 dirFlat = Vector3.ProjectOnPlane(toeOffset, Vector3.up);
        if (dirFlat.sqrMagnitude < 1e-6f) return;
        Vector3 axis = Vector3.Cross(Vector3.up, dirFlat.normalized); // 이 축으로 돌리면 뒤꿈치가 올라감
        Quaternion q = Quaternion.AngleAxis(-a, axis);
        if ((q * (pos - pivot)).y < (pos - pivot).y) q = Quaternion.AngleAxis(a, axis); // 방향 확인: 발목이 올라가야 함

        anim.SetIKPositionWeight(goal, 1f);
        anim.SetIKRotationWeight(goal, 1f);
        anim.SetIKPosition(goal, pivot + q * (pos - pivot));
        anim.SetIKRotation(goal, q * rot);
    }
}
