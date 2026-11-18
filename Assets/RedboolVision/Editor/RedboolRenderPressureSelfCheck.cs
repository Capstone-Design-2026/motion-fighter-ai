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

        // 025 ScoutPass preview windows, in recorded order. The first window
        // includes scene/model startup, so it must not count as stable pressure.
        p.Reset(10);
        Check(!p.Observe(10,true,false,5.019,0,111.543,false) && p.SevereGameWindows==0,
            "025 loading window is excluded even when the game is extremely slow");
        Check(!p.Observe(10,true,false,5.099,0,115.875,true) && p.SevereGameWindows==1 && p.BadWindows==0,
            "025 first stable slow window counts before a camera result exists");
        Check(p.Observe(10,true,false,5.069,0,115.201,true) && p.Trigger=="severe_game",
            "025 second stable slow window enables existing render relief without camera freshness");
        Check(p.Observe(10,true,false,5,0,16,false), "Severe relief stays latched while a modal or recalibration is open");
        Check(!p.Observe(11,true,false,5,0,16,true) && p.Trigger=="" && p.SevereGameWindows==0,
            "Starting a new camera session clears severe pressure and its diagnostic reason");
        foreach(double ms in new[]{16.7,28.0,30.553,33.34,50.0})
        {
            p.Reset(1);
            Check(!p.Observe(1,true,false,5,0,ms,true) && !p.Observe(1,true,false,5,0,ms,true),
                "Normal 30/60fps and the exact severe boundary do not trigger without joint evidence: "+ms);
        }
        p.Reset(1);
        Check(!Bad(p) && !p.Observe(1,true,false,5,0,100,true),
            "One joint window and one severe-only window cannot combine into a latch");
        Check(p.Observe(1,true,false,5,0,100,true), "Two severe-only windows still latch after a mixed start");
        p.Reset(1);p.Observe(1,true,false,5,0,100,true);
        Check(!p.Observe(1,true,false,5,0,100,false) && p.SevereGameWindows==0,
            "A scene transition, modal, pause or unfocused interval breaks pending severe pressure");
        Check(!p.Observe(1,true,false,5,0,100,true), "Recovery needs two new complete eligible windows");
        foreach(double elapsed in new[]{0d,4.9,8,double.NaN,double.PositiveInfinity})
        {
            p.Reset(1);p.Observe(1,true,false,5,0,100,true);
            Check(!p.Observe(1,true,false,elapsed,0,100,true) && p.SevereGameWindows==0,
                "Incomplete or sleep-sized windows also reset severe pressure: "+elapsed);
        }
        foreach(double ms in new[]{0d,-1,double.NaN,double.PositiveInfinity})
        {
            p.Reset(1);p.Observe(1,true,false,5,0,100,true);
            Check(!p.Observe(1,true,false,5,0,ms,true) && p.SevereGameWindows==0,
                "Invalid timing never supplies severe pressure: "+ms);
        }
        p.Reset(1);
        Check(!p.Observe(1,true,false,5,30,100,true) && p.Observe(1,true,false,5,30,100,true),
            "Severe game relief does not require an unhealthy camera or alter its frame cadence");
        Check(!p.Observe(1,false,false,5,0,100,true) && p.SevereGameWindows==0 && p.Trigger=="",
            "Stopping the child clears severe relief even if the caller reports an eligible window");
        Check(RedboolRenderPressure.IsStableGameFrame(true,true,true,false,false,false,.125),
            "Focused 8fps rendering can be eligible while the camera is loading");
        foreach(double interval in new[]{0d,-.1,1,2,double.NaN,double.PositiveInfinity})
            Check(!RedboolRenderPressure.IsStableGameFrame(true,true,true,false,false,false,interval),
                "Invalid, loading or suspended frame is excluded at the actual runtime predicate: "+interval);
        Check(!RedboolRenderPressure.IsStableGameFrame(false,true,true,false,false,false,.1), "No owned child means no severe-frame evidence");
        Check(!RedboolRenderPressure.IsStableGameFrame(true,false,true,false,false,false,.1), "Hidden camera gameplay means no severe-frame evidence");
        Check(!RedboolRenderPressure.IsStableGameFrame(true,true,false,false,false,false,.1), "Unfocused frames cannot lower rendering");
        Check(!RedboolRenderPressure.IsStableGameFrame(true,true,true,true,false,false,.1), "A modal interval cannot lower rendering");
        Check(!RedboolRenderPressure.IsStableGameFrame(true,true,true,false,true,false,.1), "Synthetic fixtures never create automatic field pressure");
        Check(!RedboolRenderPressure.IsStableGameFrame(true,true,true,false,false,true,.1), "Explicit camera pause cannot create severe pressure");
        p.Reset(743);
        Check(!p.Observe(743,true,true,5.01,19.3,24.1,true), "743 first steady 41fps window does not change rendering");
        Check(p.Observe(743,true,true,5.01,19.3,24.0,true) && p.Trigger=="sustained_game", "743 sustained game pressure lowers 3D cost without changing recognition");
        Check(p.Observe(743,true,true,5.01,19.3,16.7,true), "Recovered game keeps the budget stable until the next camera run");
        foreach (bool visible in new[]{false,true})
        {
            p.Reset(743);
            p.Observe(743,true,true,5,19,24,true);
            Check(!p.Observe(743,true,visible,5,19,24,false) && p.SustainedGameWindows==0, "Scene change/background breaks the sustained-game window");
        }
        foreach(double ms in new[]{16.7,19.0,23.0})
        {
            p.Reset(743);
            Check(!p.Observe(743,true,true,5,19,ms,true) && !p.Observe(743,true,true,5,19,ms,true), "Healthy or exact-boundary game does not lower resolution: "+ms);
        }
        Debug.Log("REDBOOL RENDER PRESSURE PASS " + checks + " deterministic checks; no camera or game packets");
    }
}
