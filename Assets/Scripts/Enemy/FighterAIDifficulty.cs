using UnityEngine;

/// <summary>
/// FighterAI 난이도 수치. 쉬움/보통/어려움을 에셋으로 만들어 FighterAI 나 스테이지에 꽂는다.
/// 만드는 법: Project 창 우클릭 → Create → VisionStriker → Fighter AI Difficulty
/// </summary>
[CreateAssetMenu(menuName = "VisionStriker/Fighter AI Difficulty", fileName = "FighterAIDifficulty")]
public class FighterAIDifficulty : ScriptableObject
{
    [Header("공격 빈도")]
    [Tooltip("초당 공격 시도율. 매 프레임 p = 1 - exp(-attackRate * dt) 로 굴린다.")]
    [Min(0f)] public float attackRate = 0.5f;
    [Tooltip("공격이 끝난 뒤 이 시간 동안은 공격하지 않는다.")]
    [Min(0f)] public float minAttackGap = 1.2f;
    [Tooltip("공격이 끝난 뒤 이 시간이 지나면 무조건 공격한다.")]
    [Min(0f)] public float maxAttackGap = 3.5f;
    [Tooltip("공격 예비동작(Telegraph) 길이. 플레이어가 보고 반응할 시간.")]
    [Min(0f)] public float telegraphDuration = 0.75f;

    [Header("방어")]
    [Range(0f, 1f)] public float guardChance = 0.4f;
    [Tooltip("가드 성공 후 빠른 반격 확률.")]
    [Range(0f, 1f)] public float counterChance = 0.2f;
    [Tooltip("방어로 정한 뒤 가드 자세를 잡기까지. 플레이어 공격 선딜(Fighter.windup)보다 짧아야 한다.")]
    [Min(0f)] public float guardStartDelay = 0.05f;

    public static FighterAIDifficulty CreateDefault()
    {
        return CreateInstance<FighterAIDifficulty>();
    }
}
