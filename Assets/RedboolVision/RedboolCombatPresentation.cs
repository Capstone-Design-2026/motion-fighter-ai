/// <summary>
/// Display policy only. It never changes the feedback reducer, recognition,
/// authoritative combat state, or network/input timing. Supply unscaled time.
/// </summary>
public sealed class RedboolCombatPresentation
{
    public const double NoticeSeconds = 1.1;
    public const double SameReasonCooldownSeconds = 3.0;

    bool initialized, previousDetailed;
    RedboolCombatFeedbackCode previous, notice;
    double lastNow, noticeUntil;
    double lastInterrupted = double.NegativeInfinity;
    double lastNotCharged = double.NegativeInfinity;

    public void Reset()
    {
        initialized = previousDetailed = false;
        previous = notice = RedboolCombatFeedbackCode.None;
        lastNow = noticeUntil = 0;
        lastInterrupted = lastNotCharged = double.NegativeInfinity;
    }

    public string Present(RedboolCombatFeedbackCode code, bool detailed, double now)
    {
        if (double.IsNaN(now) || double.IsInfinity(now)) { Reset(); return ""; }
        if (initialized && now < lastNow)
        {
            // A restarted scene/test clock must not re-play the previous notice.
            Reset(); Remember(code, detailed, now);
            return detailed ? RedboolCombatFeedback.Text(code) : "";
        }

        bool entered = !initialized || code != previous;
        bool modeChanged = initialized && detailed != previousDetailed;
        Remember(code, detailed, now);
        if (detailed)
        {
            ClearNotice();
            return RedboolCombatFeedback.Text(code);
        }
        // Returning from tutorial/operator UI cannot replay its current notice.
        if (modeChanged || (code != RedboolCombatFeedbackCode.Interrupted && code != RedboolCombatFeedbackCode.NotCharged))
        { ClearNotice(); return ""; }

        if (entered && (code == RedboolCombatFeedbackCode.Interrupted || code == RedboolCombatFeedbackCode.NotCharged))
        {
            double last = code == RedboolCombatFeedbackCode.Interrupted ? lastInterrupted : lastNotCharged;
            if (now - last >= SameReasonCooldownSeconds)
            {
                notice = code; noticeUntil = now + NoticeSeconds;
                if (code == RedboolCombatFeedbackCode.Interrupted) lastInterrupted = now;
                else lastNotCharged = now;
            }
            else ClearNotice();
        }
        return now < noticeUntil ? CompactText(notice) : "";
    }

    void Remember(RedboolCombatFeedbackCode code, bool detailed, double now)
    {
        initialized = true; previous = code; previousDetailed = detailed; lastNow = now;
    }

    void ClearNotice() { notice = RedboolCombatFeedbackCode.None; noticeUntil = 0; }

    static string CompactText(RedboolCombatFeedbackCode code)
    {
        if (code == RedboolCombatFeedbackCode.Interrupted) return "피격으로 중단";
        if (code == RedboolCombatFeedbackCode.NotCharged) return "필살기 기 부족";
        return "";
    }
}
