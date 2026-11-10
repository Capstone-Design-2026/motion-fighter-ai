using System;
public static class RedboolPreparationChecks
{
    private static int count;
    private static void Check(bool value, string message) { count++; if (!value) throw new Exception(message); }
    public static int Run()
    {
        count = 0;
        var s = new RedboolPreparationState();
        s.Tick(0); Check(!s.Running && !s.Rehearsal, "New scene waits for current stream");
        s.Observe(0, false, "VALID", "READY", false, "NEUTRAL");
        Check(!s.Running, "Pose READY alone cannot replace calibration");
        s.Observe(.1, true, "VALID", "READY", false, "NEUTRAL");
        Check(s.Running, "Completed calibration starts without extra countdown");
        s.Observe(.2, true, "VALID", "ACTIVE", false, "LEFT");
        Check(s.Running, "Normal actions retain calibration");
        s.Observe(.3, true, "LOST", "UNAVAILABLE", false, "NEUTRAL");
        Check(s.Running, "Brief loss must not flicker gate");
        s.Observe(.8, true, "LOST", "UNAVAILABLE", false, "NEUTRAL");
        Check(!s.Running, "Sustained loss holds combat");
        s.Observe(.9, true, "VALID", "PREPARING", false, "NEUTRAL");
        Check(s.Running, "Recovered tracking resumes using preserved calibration");
        s.Reset();
        s.Observe(1, true, "VALID", "ACTIVE", false, "RIGHT");
        Check(s.Running, "Next scene must not demand another guard hold");
        s.Tick(2); Check(!s.Running && !s.Connected, "No packets means hold");
        s.Observe(2.1, true, "VALID", "READY", false, "NEUTRAL");
        Check(s.Running, "Fresh calibrated packet reconnects without C");
        s.Observe(2.2, false, "VALID", "READY", false, "NEUTRAL");
        Check(!s.Running && !s.Calibrated, "Manual C/X invalidates calibration immediately");
        s.Reset(); s.Observe(3, false, "VALID", "READY", false, "NEUTRAL");
        Check(!s.Running, "New uncalibrated Python cannot inherit a previous session");
        s.Observe(3.1, true, "SYNTHETIC", "READY", false, "NEUTRAL");
        Check(s.Rehearsal && !s.Running, "Probe keeps enemies held");
        s.Observe(3.2, true, "VALID", "READY", false, "NEUTRAL");
        Check(s.Running && !s.Rehearsal, "Calibrated camera can follow probe");
        s.Observe(3.3, false, "STOPPED", "UNAVAILABLE", false, "NEUTRAL");
        Check(!s.Running && !s.Rehearsal, "Stop holds immediately");
        Check(RedboolGestureMap.Resolve("GESTURE","SPECIAL","BOTH")=="SPECIAL", "Bilateral cross maps to special");
        Check(RedboolGestureMap.Resolve("GESTURE","SPECIAL","LEFT")==null, "Single arm cannot map to special");
        Check(RedboolGestureMap.Resolve("ATTACK",null,"BOTH")==null, "Bilateral legacy attack is invalid");
        Check(RedboolGestureMap.Resolve("GESTURE","LEFT_RAISE","RIGHT")==null, "Side mismatch rejected");
        Check(RedboolGestureMap.Resolve("GESTURE","LEFT_RAISE","LEFT")=="HIGH_KICK", "Left raise remains high kick");
        Check(RedboolGestureMap.Resolve("GESTURE","RIGHT_RAISE","RIGHT")=="LOW_KICK", "Right raise remains low kick");
        Check(RedboolGestureMap.Resolve("ATTACK",null,"LEFT")=="JAB", "Legacy left punch retained");
        Check(RedboolGestureMap.Resolve("GESTURE","RIGHT_PUNCH","RIGHT")=="STRAIGHT", "Right punch retained");
        return count;
    }
}
