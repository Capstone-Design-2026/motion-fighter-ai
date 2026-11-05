using System;

// The same policy checks can run in the Editor menu or a standalone C# runtime.
public static class RedboolPreparationChecks
{
    private static int count;
    private static void Check(bool value, string message) { count++; if (!value) throw new Exception(message); }
    private static void Ready(RedboolPreparationState s, double start)
    {
        for (int i = 0; i <= 21; i++) s.Observe(start + i * .1, true, "VALID", "READY", false, "NEUTRAL");
    }
    public static int Run()
    {
        count = 0;
        var s = new RedboolPreparationState();
        s.Tick(0); Check(!s.Running && !s.Rehearsal, "New scene must hold combat");
        for (int i = 0; i < 50; i++) s.Observe(i * .1, false, "VALID", "READY", false, "NEUTRAL");
        Check(!s.Running, "Uncalibrated READY cannot unlock combat");
        s.Reset();
        for (int i = 0; i < 10; i++) s.Observe(i * .1, true, "VALID", "READY", false, "NEUTRAL");
        Check(s.CountingDown && !s.Running, "Do not start before two seconds");
        s.Observe(1, true, "VALID", "READY", true, "NEUTRAL");
        Check(!s.CountingDown, "Block during countdown must require return");
        s.Observe(1.1, true, "VALID", "READY", false, "LEFT");
        Check(!s.CountingDown, "Leaning must not arm countdown");
        Ready(s, 1.2); Check(s.Running, "Stable calibrated neutral should start combat");
        s.Observe(3.4, true, "VALID", "ACTIVE", false, "LEFT");
        Check(s.Running, "Normal attacks/movement must not relock the battle");
        s.Observe(3.5, true, "LOST", "UNAVAILABLE", false, "NEUTRAL");
        Check(s.Running, "Brief tracking loss should not flicker the battle gate");
        s.Observe(4, true, "LOST", "UNAVAILABLE", false, "NEUTRAL");
        Check(!s.Running, "Continuous invalid tracking must hold combat");
        Ready(s, 4.1); Check(s.Running, "Recovery must require a new countdown");
        s.Observe(6.3, false, "VALID", "READY", false, "NEUTRAL");
        Check(!s.Running && !s.Calibrated, "Recalibration must hold immediately");
        Ready(s, 7); s.Tick(10);
        Check(!s.Running && !s.Connected, "Disconnect must hold combat");
        Ready(s, 11);
        s.Observe(14, true, "VALID", "READY", false, "NEUTRAL");
        Check(!s.Running, "First new packet cannot bridge a long silent gap");
        s.Reset(); s.Observe(20, true, "SYNTHETIC", "READY", false, "NEUTRAL");
        Check(s.Rehearsal && !s.Running, "Probe may enable only player; keep AI/timer held");
        s.Observe(20.1, true, "SYNTHETIC", "READY", true, "NEUTRAL");
        Check(s.Rehearsal && !s.Running, "Probe guard must retain rehearsal mode");
        s.Observe(20.2, true, "VALID", "READY", false, "NEUTRAL");
        Check(!s.Rehearsal && !s.Running, "Camera after probe must count down before combat");
        Ready(s, 20.3); Check(s.Running, "Camera can start after rehearsal");
        s.Observe(22.5, true, "STOPPED", "READY", false, "NEUTRAL");
        Check(!s.Running && !s.Rehearsal, "STOPPED must hold immediately");
        s.Reset(); Check(!s.Connected && !s.CountingDown, "New stream/scene must clear all readiness");
        return count;
    }
}
