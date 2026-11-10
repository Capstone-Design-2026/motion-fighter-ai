/// <summary>파운장 애니메이션과 판정이 공유하는 상태 이름·시간(초). 클립은 QiPalmAnimationBaker 가 이 값으로 굽는다.</summary>
public static class QiPalmTiming
{
    public const string ChargeState = "SPECIAL_CHARGE";
    public const string ReleaseState = "SPECIAL_RELEASE";
    /// <summary>SPECIAL_RELEASE 시작부터 양손이 끝까지 뻗어 기파가 손을 떠나는 순간까지.</summary>
    public const float ReleaseImpact = .18f;
    /// <summary>SPECIAL_RELEASE 전체 길이(가드 자세로 돌아오기까지).</summary>
    public const float ReleaseLength = .82f;
}
