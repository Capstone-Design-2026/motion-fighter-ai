using System;

/// <summary>Local presentation budget only. Never changes vision models or input deadlines.</summary>
public sealed class RedboolRenderPressure
{
    public const double WindowSeconds = 5;
    public bool Latched { get; private set; }
    public int BadWindows { get; private set; }
    public int SevereGameWindows { get; private set; }
    public int SustainedGameWindows { get; private set; }
    public string Trigger { get; private set; } = "";
    int generation;

    public void Reset(int sessionGeneration)
    {
        generation = sessionGeneration; Latched = false; BadWindows = SevereGameWindows = SustainedGameWindows = 0; Trigger = "";
    }

    // Eligible means a whole active window with connected, advancing camera
    // frames and no user pause/modal/background interval. Calibration is active.
    public bool Observe(int sessionGeneration, bool running, bool eligible,
        double elapsedSeconds, double cameraFps, double gameMeanFrameMs, bool severeGameEligible = false)
    {
        if (sessionGeneration != generation || !running) Reset(sessionGeneration);
        if (!running) return false;
        if (Latched) return true;
        bool complete = Finite(elapsedSeconds) && elapsedSeconds >= WindowSeconds
            && elapsedSeconds <= WindowSeconds * 1.5
            && Finite(gameMeanFrameMs) && gameMeanFrameMs > 0;
        bool valid = eligible && complete && Finite(cameraFps) && cameraFps > 0;
        BadWindows = valid && cameraFps < 18 && gameMeanFrameMs > 28 ? BadWindows + 1 : 0;
        // Camera initialization can itself be starved by a very slow scene.
        // This independent branch only reduces rendering; it never changes
        // camera capture, recognition continuity or input expiry.
        SevereGameWindows = complete && severeGameEligible && gameMeanFrameMs > 50 ? SevereGameWindows + 1 : 0;
        // 743 field run: camera advances at 19fps but the active arena stays near
        // 41fps. Ten focused seconds of game pressure also deserve render relief.
        // Scene loads, a modal, backgrounding and missing camera frames cannot trigger it.
        SustainedGameWindows = valid && severeGameEligible && gameMeanFrameMs > 23
            ? SustainedGameWindows + 1 : 0;
        if (BadWindows >= 2 || SevereGameWindows >= 2 || SustainedGameWindows >= 2)
        {
            Latched = true;
            Trigger = BadWindows >= 2 ? "joint_camera_game" : SevereGameWindows >= 2 ? "severe_game" : "sustained_game";
        }
        return Latched;
    }

    public static bool IsStableGameFrame(bool running, bool visible, bool focused, bool modal,
        bool synthetic, bool paused, double intervalSeconds)
    {
        return running && visible && focused && !modal && !synthetic && !paused
            && Finite(intervalSeconds) && intervalSeconds > 0 && intervalSeconds < 1;
    }

    static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    public static bool LowBudget(bool inferencePressure, bool manualLow, bool jointPressure)
    { return inferencePressure || manualLow || jointPressure; }
}
