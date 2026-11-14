using System;
using UnityEngine;

public static class RedboolRenderPressureSelfCheck
{
    static int checks;
    static void Check(bool ok, string why) { checks++; if (!ok) throw new Exception(why); }
    static bool Bad(RedboolRenderPressure policy, int generation = 1)
    { return policy.Observe(generation, true, true, 5, 13, 35); }

    public static void Run()
    {
        checks = 0;
        var p = new RedboolRenderPressure();
        Check(!Bad(p) && p.BadWindows == 1, "One bad window cannot lower rendering");
        Check(Bad(p), "Two consecutive joint slow windows latch the local budget");
        Check(p.Observe(1, true, true, 5, 30, 16), "Healthy frames keep the same session latch stable");
        Check(p.Observe(1, true, false, 5, 0, 16), "A pause or C does not oscillate an existing latch");
        Check(!p.Observe(1, false, false, 5, 13, 35), "Stopping the owned child clears the latch");
        Bad(p); Bad(p);
        Check(!p.Observe(2, true, true, 5, 30, 16) && p.BadWindows == 0, "A new camera generation starts clean");
        foreach (double fps in new[] { 0d, -1, double.NaN, double.PositiveInfinity, 18, 30 })
        {
            p.Reset(1); Bad(p);
            Check(!p.Observe(1, true, true, 5, fps, 35) && !Bad(p), "Unknown or healthy camera breaks consecutive pressure: " + fps);
        }
        foreach (double ms in new[] { 0d, double.NaN, double.PositiveInfinity, 16, 28 })
        {
            p.Reset(1); Bad(p);
            Check(!p.Observe(1, true, true, 5, 13, ms) && !Bad(p), "Camera slowness alone cannot trigger: " + ms);
        }
        p.Reset(1); Bad(p);
        Check(!p.Observe(1, true, false, 5, 13, 35) && !Bad(p), "Stale/disconnected/paused/background windows reset pending pressure");
        foreach (double elapsed in new[] { 0d, 4.9, 8, double.NaN })
        {
            p.Reset(1); Bad(p);
            Check(!p.Observe(1, true, true, elapsed, 13, 35) && !Bad(p), "Incomplete or sleep-sized windows cannot trigger: " + elapsed);
        }
        Check(!RedboolRenderPressure.LowBudget(false, false, false), "Fast automatic mode keeps normal render budget");
        Check(RedboolRenderPressure.LowBudget(false, true, false), "Explicit low quality works without camera pressure");
        Check(RedboolRenderPressure.LowBudget(true, false, false), "Existing backend latency warning still lowers rendering");
        Check(RedboolRenderPressure.LowBudget(false, false, true), "Joint pressure feeds the existing render owner");
        Check(!RedboolRenderPressure.LowBudget(false, false, false), "Turning off manual low is not itself a latch");
        Debug.Log("REDBOOL RENDER PRESSURE PASS " + checks + " deterministic checks; no camera or game packets");
    }
}
