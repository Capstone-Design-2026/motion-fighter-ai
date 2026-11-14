using System;

/// <summary>Local presentation budget only. Never changes vision models or input deadlines.</summary>
public sealed class RedboolRenderPressure
{
    public const double WindowSeconds = 5;
    public bool Latched { get; private set; }
    public int BadWindows { get; private set; }
    int generation;

    public void Reset(int sessionGeneration)
    {
        generation = sessionGeneration; Latched = false; BadWindows = 0;
    }

    // Eligible means a whole active window with connected, advancing camera
    // frames and no user pause/modal/background interval. Calibration is active.
    public bool Observe(int sessionGeneration, bool running, bool eligible,
        double elapsedSeconds, double cameraFps, double gameMeanFrameMs)
    {
        if (sessionGeneration != generation || !running) Reset(sessionGeneration);
        if (!running) return false;
        if (Latched) return true;
        bool valid = eligible && Finite(elapsedSeconds) && elapsedSeconds >= WindowSeconds
            && elapsedSeconds <= WindowSeconds * 1.5 && Finite(cameraFps) && cameraFps > 0
            && Finite(gameMeanFrameMs) && gameMeanFrameMs > 0;
        BadWindows = valid && cameraFps < 18 && gameMeanFrameMs > 28 ? BadWindows + 1 : 0;
        if (BadWindows >= 2) Latched = true;
        return Latched;
    }

    static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    public static bool LowBudget(bool inferencePressure, bool manualLow, bool jointPressure)
    { return inferencePressure || manualLow || jointPressure; }
}
