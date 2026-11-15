using System;
using System.Diagnostics;
using UnityEngine;

// Keep expensive first-use CPU work in scene preparation. The opt-out is for
// repeatable cold/warm diagnostics only; it does not alter combat rules.
public static class RedboolEffectPreparation
{
    public static bool Enabled => Array.IndexOf(Environment.GetCommandLineArgs(), "--redbool-cold-effects") < 0;
    public static void Measure(string label, Action action)
    {
        var timer = Stopwatch.StartNew(); action(); timer.Stop();
        UnityEngine.Debug.Log(FormattableString.Invariant($"[REDBOOL PREWARM] {label} cpu_ms={timer.Elapsed.TotalMilliseconds:F2} frame={Time.frameCount}"));
    }
}
