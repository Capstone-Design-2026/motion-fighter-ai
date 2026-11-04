using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 패배(DEFEAT) 동작 중에는 서 있는 자세용 보정 스크립트를 잠시 끈다.
/// (자세 낮추기·발 붙잡기·시선 고정·가드 폭 등이 쓰러지는 몸을 바닥에 박거나 비틀지 않게)
/// 패배 상태에서 벗어나면 다시 켠다.
/// </summary>
[DefaultExecutionOrder(-200)]
[RequireComponent(typeof(Animator))]
public class DefeatPose : MonoBehaviour
{
    public string defeatState = "DEFEAT";
    [Tooltip("패배 중에 끌 스크립트 이름")]
    public string[] disableDuringDefeat = { "FighterStance", "StanceFootAlign", "HeadLook", "PunchAim", "GuardWidth", "KickGuard" };

    Animator anim;
    int hash;
    readonly List<Behaviour> turnedOff = new List<Behaviour>();
    public bool IsDefeated { get; private set; }

    void Awake() { anim = GetComponent<Animator>(); hash = Animator.StringToHash(defeatState); }

    public void Defeat() { anim.ResetTrigger("Revive"); anim.SetTrigger("Defeat"); }
    public void Revive() { anim.ResetTrigger("Defeat"); anim.SetTrigger("Revive"); }

    void Update()
    {
        bool d = anim.GetCurrentAnimatorStateInfo(0).shortNameHash == hash
                 || (anim.IsInTransition(0) && anim.GetNextAnimatorStateInfo(0).shortNameHash == hash);
        if (anim.IsInTransition(0) && anim.GetCurrentAnimatorStateInfo(0).shortNameHash == hash && anim.GetNextAnimatorStateInfo(0).shortNameHash != hash) d = false;
        if (d == IsDefeated) return;
        IsDefeated = d;
        if (d)
        {
            foreach (var n in disableDuringDefeat)
            {
                var b = GetComponent(n) as Behaviour;
                if (b != null && b.enabled) { b.enabled = false; turnedOff.Add(b); }
            }
            // 위쪽 가드 레이어도 끔 (킥 중에만 켜지는 팔 레이어)
            int l = anim.GetLayerIndex("UpperGuard");
            if (l >= 0) anim.SetLayerWeight(l, 0f);
        }
        else
        {
            foreach (var b in turnedOff) if (b != null) b.enabled = true;
            turnedOff.Clear();
        }
    }
}
