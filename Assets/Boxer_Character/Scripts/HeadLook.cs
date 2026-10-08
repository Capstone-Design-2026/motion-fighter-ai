using UnityEngine;

/// <summary>
/// 고개(와 상체 약간)를 정면 또는 지정한 상대를 바라보게 한다.
/// Animator 레이어의 IK Pass가 켜져 있어야 동작한다.
/// </summary>
[RequireComponent(typeof(Animator))]
public class HeadLook : MonoBehaviour
{
    [Tooltip("바라볼 대상 (상대 캐릭터의 머리 등). 비워두면 캐릭터 정면을 본다")]
    public Transform target;

    [Tooltip("전체 적용 강도. 0이면 원래 동작 그대로, 1이면 완전히 바라봄")]
    [Range(0f, 1f)] public float weight = 1f;

    [Tooltip("상체가 같이 돌아가는 정도. 0이면 고개만 돌림")]
    [Range(0f, 1f)] public float bodyWeight = 0.15f;

    [Tooltip("고개가 돌아가는 정도")]
    [Range(0f, 1f)] public float headWeight = 1f;

    [Tooltip("고개 회전 제한. 0이면 제한 없음, 클수록 덜 돌아감")]
    [Range(0f, 1f)] public float clampWeight = 0.5f;

    [Tooltip("정면을 볼 때 시선 높이 보정 (미터). 음수면 살짝 숙인 턱 당긴 시선")]
    public float lookHeightOffset = -0.05f;

    Animator anim;
    Transform head;

    void Awake()
    {
        anim = GetComponent<Animator>();
        head = anim.GetBoneTransform(HumanBodyBones.Head);
    }

    void OnAnimatorIK(int layerIndex)
    {
        if (head == null || weight <= 0f) return;

        Vector3 lookPos;
        if (target != null)
        {
            lookPos = target.position;
        }
        else
        {
            Vector3 headPos = head.position;
            lookPos = new Vector3(headPos.x, headPos.y + lookHeightOffset, headPos.z) + transform.forward * 5f;
        }

        anim.SetLookAtWeight(weight, bodyWeight, headWeight, 0f, clampWeight);
        anim.SetLookAtPosition(lookPos);
    }
}
