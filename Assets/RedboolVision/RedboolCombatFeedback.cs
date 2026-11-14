// Presentation only: never schedules an input or changes a combat/recognition gate.
// These compact codes are also carried by authoritative PvP view snapshots.
public enum RedboolCombatFeedbackCode : byte
{
    None, Ready, Guarding, Attacking, Recovering, SpecialActive, HitStunned,
    Interrupted, NotCharged, Busy, TrackingLost, InputDelayed, Preparing,
    RoundOver, ViewLost, JabStarted, StraightStarted, HighKickStarted,
    LowKickStarted, SpecialStarted, TutorialRecognized, ReleaseGuard
}

public struct RedboolCombatFeedbackContext
{
    public bool Available, Calibrated, TrackingValid, InputDelayed, Preparing,
        Dead, HitStunned, Recovering, Attacking, SpecialActive, Guarding, CanAttack,
        Practicing;
}

/// <summary>Small deterministic reducer; monotonic unscaled time is supplied by its owner.</summary>
public sealed class RedboolCombatFeedback
{
    public const double NoticeSeconds = 1.1;
    RedboolCombatFeedbackCode notice;
    double noticeUntil, interruptedUntil;

    public void Reset() { notice = RedboolCombatFeedbackCode.None; noticeUntil = interruptedUntil = 0; }

    // Called once for a newly decided event, never for a duplicate/retried ACK.
    public void Acknowledge(string status, string action, double now)
    {
        var code = RedboolCombatFeedbackCode.None;
        if (status == "APPLIED")
        {
            code = action == "JAB" ? RedboolCombatFeedbackCode.JabStarted
                : action == "STRAIGHT" ? RedboolCombatFeedbackCode.StraightStarted
                : action == "HIGH_KICK" ? RedboolCombatFeedbackCode.HighKickStarted
                : action == "LOW_KICK" ? RedboolCombatFeedbackCode.LowKickStarted
                : action == "SPECIAL" ? RedboolCombatFeedbackCode.SpecialStarted
                : RedboolCombatFeedbackCode.None;
            interruptedUntil = 0;
        }
        else if (status == "TUTORIAL_APPLIED") code = RedboolCombatFeedbackCode.TutorialRecognized;
        else if (status == "NOT_CHARGED") code = RedboolCombatFeedbackCode.NotCharged;
        else if (status == "GAME_BUSY" || status == "INPUT_LOCKED") code = RedboolCombatFeedbackCode.Busy;
        else if (status == "EXPIRED" || status == "UNSYNCHRONIZED") code = RedboolCombatFeedbackCode.InputDelayed;
        if (code == RedboolCombatFeedbackCode.None) return;
        notice = code; noticeUntil = now + NoticeSeconds;
    }

    public void AttackInterrupted(double now) { interruptedUntil = now + NoticeSeconds; }

    public RedboolCombatFeedbackCode Resolve(RedboolCombatFeedbackContext state, double now)
    {
        if (!state.Available) return RedboolCombatFeedbackCode.None;
        // A successful command must never hide lost tracking, an expired lease,
        // calibration, a finished round or an authoritative preparation pause.
        if (!state.Calibrated) return RedboolCombatFeedbackCode.Preparing;
        if (!state.TrackingValid) return state.InputDelayed ? RedboolCombatFeedbackCode.InputDelayed : RedboolCombatFeedbackCode.TrackingLost;
        if (state.Dead) return RedboolCombatFeedbackCode.RoundOver;
        if (state.Preparing && !state.Practicing) return RedboolCombatFeedbackCode.Preparing;
        if (now < interruptedUntil) return RedboolCombatFeedbackCode.Interrupted;
        if (state.HitStunned) return RedboolCombatFeedbackCode.HitStunned;
        if (state.Recovering) return RedboolCombatFeedbackCode.Recovering;
        bool recent = now < noticeUntil;
        if (recent && notice == RedboolCombatFeedbackCode.Busy)
        {
            if (state.Guarding) return RedboolCombatFeedbackCode.ReleaseGuard;
            if (!state.CanAttack) return state.Attacking ? RedboolCombatFeedbackCode.Attacking
                : state.SpecialActive ? RedboolCombatFeedbackCode.SpecialActive : RedboolCombatFeedbackCode.Busy;
        }
        else if (recent) return notice;
        if (state.Practicing) return RedboolCombatFeedbackCode.None;
        if (state.SpecialActive) return RedboolCombatFeedbackCode.SpecialActive;
        if (state.Attacking) return RedboolCombatFeedbackCode.Attacking;
        if (state.Guarding) return RedboolCombatFeedbackCode.Guarding;
        return state.CanAttack ? RedboolCombatFeedbackCode.Ready : RedboolCombatFeedbackCode.Busy;
    }

    public static bool Valid(byte code) { return code <= (byte)RedboolCombatFeedbackCode.ReleaseGuard; }
    public static string Text(RedboolCombatFeedbackCode code)
    {
        switch (code)
        {
            case RedboolCombatFeedbackCode.Ready: return "다음 동작을 할 수 있어요";
            case RedboolCombatFeedbackCode.Guarding: return "블락 중 · 팔을 풀면 해제돼요";
            case RedboolCombatFeedbackCode.Attacking: return "기술 동작 중 · 끝나면 다음 공격";
            case RedboolCombatFeedbackCode.Recovering: return "기술 회복 중 · 잠시 뒤 다음 공격";
            case RedboolCombatFeedbackCode.SpecialActive: return "필살기 시전 중";
            case RedboolCombatFeedbackCode.HitStunned: return "피격 중 · 잠시 뒤 다시 공격하세요";
            case RedboolCombatFeedbackCode.Interrupted: return "피격으로 진행 중인 기술이 끊겼어요";
            case RedboolCombatFeedbackCode.NotCharged: return "X자 인식 · 필살기 충전이 필요해요";
            case RedboolCombatFeedbackCode.Busy: return "지금은 공격 대기 중이에요";
            case RedboolCombatFeedbackCode.TrackingLost: return "동작 추적 확인 중 · 카메라 안내를 봐주세요";
            case RedboolCombatFeedbackCode.InputDelayed: return "카메라 입력이 늦어요 · 잠시 대기";
            case RedboolCombatFeedbackCode.Preparing: return "준비가 끝나면 전투가 시작돼요";
            case RedboolCombatFeedbackCode.RoundOver: return "전투가 끝났어요";
            case RedboolCombatFeedbackCode.ViewLost: return "상대 게임 연결을 기다리고 있어요";
            case RedboolCombatFeedbackCode.JabStarted: return "잽 인식 · 기술 시작";
            case RedboolCombatFeedbackCode.StraightStarted: return "스트레이트 인식 · 기술 시작";
            case RedboolCombatFeedbackCode.HighKickStarted: return "하이킥 인식 · 기술 시작";
            case RedboolCombatFeedbackCode.LowKickStarted: return "로우킥 인식 · 기술 시작";
            case RedboolCombatFeedbackCode.SpecialStarted: return "X자 인식 · 필살기 시작";
            case RedboolCombatFeedbackCode.TutorialRecognized: return "수련 동작을 인식했어요";
            case RedboolCombatFeedbackCode.ReleaseGuard: return "동작 인식 · 블락을 풀면 공격 가능";
            default: return "";
        }
    }
}
