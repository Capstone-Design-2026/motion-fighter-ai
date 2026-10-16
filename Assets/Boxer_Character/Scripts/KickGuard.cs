using UnityEngine;

/// <summary>
/// 킥을 차는 동안 양팔은 IDLE 가드 자세를 유지하고, 몸통·골반·머리는 원래 킥 동작을 그대로 따른다.
/// 애니메이터의 팔 전용 레이어(UpperGuard, 팔+손가락 마스크 + IDLE 동작)를 킥 상태일 때만 켠다.
/// 킥 중에는 Head Look(시선 고정)을 약하게 줄여서 목이 비틀리지 않게 한다.
/// 피격(배 맞음 등) 중에도 Head Look을 줄여서, 맞은 쪽으로 고개가 정면으로 되돌려지지 않고 몸을 따라 숙여지게 한다.
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

    [Header("피격")]
    [Tooltip("Head Look을 줄일 피격 상태 이름. 여기 없는 피격 상태는 기존 그대로 정면 시선 고정")]
    public string[] hitStates = { "HIT_STOMACH" };

    [Tooltip("피격 중 Head Look 강도 (0~1). 낮을수록 맞은 동작대로 고개가 깊이 숙여지고, 높을수록 정면을 보려고 고개가 들림. 1이면 계속 정면 고정")]
    [Range(0f, 1f)] public float headLookDuringHit = 0.6f;

    [Tooltip("피격 시작·끝에서 Head Look이 풀리고 돌아오는 속도. 클수록 빠르게 전환")]
    public float hitBlendSpeed = 30f;

    [Header("가드 자세 (레그체크 등)")]
    [Tooltip("Head Look을 줄여서 애니메이션의 고개 숙임을 살릴 상태 이름")]
    public string[] guardPoseStates = { "KICK_GUARD", "KICK_GUARD_LOW" };

    [Tooltip("가드 자세 중 Head Look 강도 (0~1). 0이면 애니메이션 고개 그대로")]
    [Range(0f, 1f)] public float headLookDuringGuardPose = 0f;

    [Tooltip("가드 자세 시작·끝에서 Head Look이 풀리고 돌아오는 속도. 애니메이터 전환 시간과 맞추는 게 자연스럽다 (3.3 = 약 0.3초)")]
    public float guardBlendSpeed = 3.3f;

    Animator anim;
    HeadLook headLook;
    float baseHeadLook = 1f;
    int layer = -1;
    int[] hashes;
    int[] hitHashes;
    float w;
    float wHit;
    int[] guardHashes;
    float wGuard;

    void Awake()
    {
        anim = GetComponent<Animator>();
        headLook = GetComponent<HeadLook>();
        if (headLook != null) baseHeadLook = headLook.weight;
        layer = anim.GetLayerIndex(guardLayerName);
        hashes = Hashes(kickStates);
        hitHashes = Hashes(hitStates);
        guardHashes = Hashes(guardPoseStates);
    }

    static int[] Hashes(string[] names)
    {
        var result = new int[names != null ? names.Length : 0];
        for (int i = 0; i < result.Length; i++) result[i] = Animator.StringToHash(names[i]);
        return result;
    }

    static bool In(int[] list, int hash)
    {
        foreach (var h in list) if (h == hash) return true;
        return false;
    }

    // 지금 해당 상태이거나, 해당 상태로 들어가는 전환 중이면 true (해당 상태에서 다른 상태로 나가는 전환 중이면 false)
    bool Active(int[] list)
    {
        bool on = In(list, anim.GetCurrentAnimatorStateInfo(0).shortNameHash)
                  || (anim.IsInTransition(0) && In(list, anim.GetNextAnimatorStateInfo(0).shortNameHash));
        if (anim.IsInTransition(0) && !In(list, anim.GetNextAnimatorStateInfo(0).shortNameHash)) on = false;
        return on;
    }

    void Update()
    {
        bool kicking = Active(hashes);
        w = Mathf.MoveTowards(w, kicking ? 1f : 0f, blendSpeed * Time.deltaTime);
        if (layer >= 0) anim.SetLayerWeight(layer, w * strength);

        bool hit = Active(hitHashes);
        wHit = Mathf.MoveTowards(wHit, hit ? 1f : 0f, hitBlendSpeed * Time.deltaTime);
        bool guardPose = Active(guardHashes);
        wGuard = Mathf.MoveTowards(wGuard, guardPose ? 1f : 0f, guardBlendSpeed * Time.deltaTime);

        if (headLook != null)
            headLook.weight = baseHeadLook * Mathf.Lerp(1f, headLookDuringKick, w) * Mathf.Lerp(1f, headLookDuringHit, wHit) * Mathf.Lerp(1f, headLookDuringGuardPose, wGuard);
    }
}
