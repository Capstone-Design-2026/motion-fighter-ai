using System;
using System.Collections.Generic;
using System.IO;
#if !REDBOOL_PRESENTATION_STANDALONE
using UnityEngine;
#endif

/// <summary>Pure UI sequences; no scene, camera, socket or gameplay mutation.</summary>
public static class RedboolCombatPresentationSelfCheck
{
    static readonly List<string> checks = new List<string>();
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks.Add(message); }
    static string Text(RedboolCombatFeedbackCode code) { return RedboolCombatFeedback.Text(code); }
    const string InterruptedText = "피격으로 중단", NotChargedText = "필살기 기 부족";
    public static void Run()
    {
        checks.Clear();
        var p = new RedboolCombatPresentation();
        var interrupted = RedboolCombatFeedbackCode.Interrupted;
        var charge = RedboolCombatFeedbackCode.NotCharged;
        var ready = RedboolCombatFeedbackCode.Ready;
        foreach (RedboolCombatFeedbackCode code in Enum.GetValues(typeof(RedboolCombatFeedbackCode)))
        {
            if (code == interrupted || code == charge) continue;
            p.Reset();
            Check(p.Present(code, false, 10) == "", "Minimal idle/routine/error state has no duplicate combat label: " + code);
        }
        p.Reset();
        Check(p.Present(interrupted, false, 10) == InterruptedText, "First actual interruption uses the brief combat wording");
        Check(p.Present(interrupted, false, 10.5) == InterruptedText, "Same reason is visible inside its bounded notice");
        Check(p.Present(interrupted, false, 11.1) == "", "Persistent notice stops at its 1.1 second deadline");
        Check(p.Present(ready, false, 11.15) == "", "Routine state stays blank after a transient notice");
        Check(p.Present(interrupted, false, 11.2) == "", "New same-reason edge is suppressed within three seconds");
        Check(p.Present(interrupted, false, 14) == "", "Persistent reason never retriggers just because cooldown expired");
        p.Present(ready, false, 14.1);
        Check(p.Present(interrupted, false, 14.2) == InterruptedText, "New edge after cooldown can be shown again");
        Check(p.Present(charge, false, 14.3) == NotChargedText, "Not-charged edge uses brief combat wording and its own cooldown");
        Check(p.Present(interrupted, false, 14.4) == "", "Rapid alternate reasons cannot bypass the same-reason cooldown");
        p.Present(ready, false, 17.2);
        Check(p.Present(charge, false, 17.3) == NotChargedText, "Not-charged edge becomes eligible at its three-second boundary");
        Check(p.Present(charge, false, 25) == "", "Persistently uncharged X never produces recurring reminders");

        p.Reset(); p.Present(interrupted, false, 26);
        Check(p.Present(RedboolCombatFeedbackCode.JabStarted, false, 26.1) == "", "A real later attack immediately clears an obsolete interruption label");
        p.Reset(); p.Present(charge, false, 27);
        Check(p.Present(ready, false, 27.1) == "", "Ready immediately clears a previous not-charged label");

        var barriers = new[] { RedboolCombatFeedbackCode.None, RedboolCombatFeedbackCode.RoundOver,
            RedboolCombatFeedbackCode.ViewLost, RedboolCombatFeedbackCode.InputDelayed,
            RedboolCombatFeedbackCode.TrackingLost, RedboolCombatFeedbackCode.Preparing, (RedboolCombatFeedbackCode)255 };
        foreach (var barrier in barriers)
        {
            p.Reset(); p.Present(interrupted, false, 30);
            Check(p.Present(barrier, false, 30.1) == "", "Inactive/unsafe state immediately clears stale notice: " + barrier);
            Check(p.Present(ready, false, 30.2) == "", "Notice cannot return after barrier: " + barrier);
        }
        p.Reset();
        foreach (RedboolCombatFeedbackCode code in Enum.GetValues(typeof(RedboolCombatFeedbackCode)))
            Check(p.Present(code, true, 40) == Text(code), "Detailed tutorial/operator mode preserves existing text: " + code);
        Check(p.Present(charge, true, 40.1) == Text(charge), "Detailed mode shows current not-charged information");
        Check(p.Present(charge, false, 40.2) == "", "Returning from detailed mode cannot replay its current notice");
        Check(p.Present(charge, false, 44) == "", "The same notice stays hidden after returning from tutorial");
        p.Present(ready, false, 44.1);
        Check(p.Present(charge, false, 44.2) == NotChargedText, "A later real edge works after leaving detailed mode");
        Check(p.Present(ready, true, 44.3) == Text(ready), "Entering detailed mode clears old minimal notice");
        Check(p.Present(ready, false, 44.4) == "", "Returning again does not leak the earlier transient notice");

        p.Reset(); p.Present(interrupted, false, 100);
        Check(p.Present(interrupted, false, 0) == "", "Clock restart drops the old notice");
        Check(p.Present(interrupted, false, 4) == "", "Clock restart does not turn persistence into a new edge");
        p.Present(ready, false, 4.1);
        Check(p.Present(interrupted, false, 4.2) == InterruptedText, "New epoch accepts a later real edge without old cooldown");
        Check(p.Present(charge, false, double.NaN) == "", "Invalid time cannot keep a notice alive");
        Check(p.Present(charge, false, double.PositiveInfinity) == "", "Infinite time is safely suppressed");
        p.Reset();
        Check(p.Present(interrupted, false, 0) == InterruptedText, "Explicit reset starts a fresh display session");
        Check(InterruptedText.Length <= 9 && NotChargedText.Length <= 9 && !InterruptedText.Contains("\n") && !NotChargedText.Contains("\n"),
            "Both combat labels remain short single-line strings");

        string result = "PASS " + checks.Count + " combat presentation checks\n" + string.Join("\n", checks.ToArray());
        string path = Environment.GetEnvironmentVariable("REDBOOL_COMBAT_PRESENTATION_CHECK_OUTPUT");
        if (!string.IsNullOrEmpty(path)) File.WriteAllText(path, result);
#if REDBOOL_PRESENTATION_STANDALONE
        Console.WriteLine(result);
#else
        Debug.Log(result);
#endif
    }
#if REDBOOL_PRESENTATION_STANDALONE
    public static void Main() { Run(); }
#endif
}
