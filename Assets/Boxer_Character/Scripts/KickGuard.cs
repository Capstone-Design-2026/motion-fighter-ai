using UnityEngine;

/// <summary>
/// 킥을 차는 동안 양팔은 IDLE 가드 자세를 유지하고, 몸통·골반·머리는 원래 킥 동작을 그대로 따른다.
/// 애니메이터의 팔 전용 레이어(UpperGuard, 팔+손가락 마스크 + IDLE 동작)를 킥 상태일 때만 켠다.
/// 킥 중에는 Head Look(시선 고정)을 약하게 줄여서 목이 비틀리지 않게 한다.
/// </summary>
[RequireComponent(typeof(Animator))]
public class KickGuard : MonoBehaviour
{
    [Tooltip("팔 가드를 유지할 킥 상태 이름")]
    public string[] kickStates = { "LOWKICK", "HIGHKICK" };

    [Tooltip("팔 가드 레이어 이름 (애니메이터에 있는 레이어)")]
    public string guardLayerName = "UpperGuard";

    [Tooltip("팔을 가드로 고정하는 정도. 1이면 완전히 IDLE 팔, 낮추면 킥 팔 움직임이 섞임")]
    [Range(0f, 1f)] public float strength = 1f;

    [Tooltip("킥 중 Head Look 강도 (0~1). 0이면 머리가 몸통 따라 자연스럽게, 1이면 계속 정면 고정(목이 비틀릴 수 있음)")]
    [Range(0f, 1f)] public float headLookDuringKick = 0f;

    [Tooltip("가드로 바뀌고 풀리는 속도. 클수록 빠르게 전환")]
    public float blendSpeed = 10f;

    Animator anim;
    HeadLook headLook;
    float baseHeadLook = 1f;
    int layer = -1;
    int[] hashes;
    float w;

    void Awake()
    {
        anim = GetComponent<Animator>();
        headLook = GetComponent<HeadLook>();
        if (headLook != null) baseHeadLook = headLook.weight;
        layer = anim.GetLayerIndex(guardLayerName);
        hashes = new int[kickStates.Length];
        for (int i = 0; i < kickStates.Length; i++) hashes[i] = Animator.StringToHash(kickStates[i]);
    }

    bool IsKick(int hash)
    {
        foreach (var h in hashes) if (h == hash) return true;
        return false;
    }

    bool Kicking()
    {
        bool toKick = IsKick(anim.GetCurrentAnimatorStateInfo(0).shortNameHash)
                      || (anim.IsInTransition(0) && IsKick(anim.GetNextAnimatorStateInfo(0).shortNameHash));
        if (anim.IsInTransition(0) && !IsKick(anim.GetNextAnimatorStateInfo(0).shortNameHash)) toKick = false;
        return toKick;
    }

    void Update()
    {
        if (layer < 0) return;
        bool kicking = Kicking();
        w = Mathf.MoveTowards(w, kicking ? 1f : 0f, blendSpeed * Time.deltaTime);
        anim.SetLayerWeight(layer, w * strength);
        if (headLook != null) headLook.weight = Mathf.Lerp(baseHeadLook, baseHeadLook * headLookDuringKick, w);
    }
}
