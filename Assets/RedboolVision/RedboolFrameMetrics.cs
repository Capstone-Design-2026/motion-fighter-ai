using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Low-rate, local Player.log measurements. Unscaled timing includes special
// cinematics and paused tutorial frames; this is not motion-to-screen latency.
public sealed class RedboolFrameMetrics : MonoBehaviour
{
    readonly List<float> intervals = new List<float>(660);
    string scene;
    double started;
    int focusedFrames;
    float minTimeScale = float.PositiveInfinity, maxTimeScale;
    static double liveInputUntil;
    public static void NoteLiveInput() { liveInputUntil = Time.realtimeSinceStartupAsDouble + .5; }
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
    // Foundation's user-initiated activity keeps live camera input responsive
    // while the user is moving instead of generating keyboard/mouse events.
    // Allow normal system/display sleep; release on input loss or game exit.
    IntPtr activity;
    bool activityUnavailable;
    const string ObjC = "/usr/lib/libobjc.A.dylib";
    [System.Runtime.InteropServices.DllImport(ObjC)] static extern IntPtr objc_getClass(string name);
    [System.Runtime.InteropServices.DllImport(ObjC)] static extern IntPtr sel_registerName(string name);
    [System.Runtime.InteropServices.DllImport(ObjC, EntryPoint="objc_msgSend")] static extern IntPtr Send(IntPtr target, IntPtr selector);
    [System.Runtime.InteropServices.DllImport(ObjC, EntryPoint="objc_msgSend")] static extern IntPtr SendString(IntPtr target, IntPtr selector, string value);
    [System.Runtime.InteropServices.DllImport(ObjC, EntryPoint="objc_msgSend")] static extern IntPtr BeginActivity(IntPtr target, IntPtr selector, ulong options, IntPtr reason);
    [System.Runtime.InteropServices.DllImport(ObjC, EntryPoint="objc_msgSend")] static extern void EndActivity(IntPtr target, IntPtr selector, IntPtr value);
    void UpdateActivity(bool live)
    {
        if (activityUnavailable || live == (activity != IntPtr.Zero)) return;
        try {
            var process = Send(objc_getClass("NSProcessInfo"), sel_registerName("processInfo"));
            if (live) {
                var reason = SendString(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"), "REDBOOL live motion gameplay");
                // NSActivityUserInteractive minus idle-system-sleep inhibition,
                // from NSProcessInfo.h: live input has 150ms packet deadlines.
                activity = BeginActivity(process, sel_registerName("beginActivityWithOptions:reason:"), 0xFF00EFFFFFUL, reason);
                if (activity != IntPtr.Zero) Send(activity, sel_registerName("retain"));
            } else {
                EndActivity(process, sel_registerName("endActivity:"), activity);
                Send(activity, sel_registerName("release")); activity = IntPtr.Zero;
            }
            Debug.Log("[REDBOOL ACTIVITY] live_input=" + live + " active=" + (activity != IntPtr.Zero));
        } catch (Exception e) {
            activityUnavailable = true;
            Debug.LogWarning("[REDBOOL ACTIVITY] Unavailable: " + e.GetType().Name);
        }
    }
#else
    void UpdateActivity(bool live) { }
#endif
    void Start() { scene = SceneManager.GetActiveScene().name; started = Time.realtimeSinceStartupAsDouble; }
    void Update()
    {
        UpdateActivity(Time.realtimeSinceStartupAsDouble < liveInputUntil);
        string current = SceneManager.GetActiveScene().name;
        if (current != scene) { Flush(); scene = current; started = Time.realtimeSinceStartupAsDouble; }
        intervals.Add(Time.unscaledDeltaTime * 1000f);
        if (Application.isFocused) focusedFrames++;
        minTimeScale = Mathf.Min(minTimeScale, Time.timeScale);
        maxTimeScale = Mathf.Max(maxTimeScale, Time.timeScale);
        if (Time.realtimeSinceStartupAsDouble - started >= 10) Flush();
    }
    void Flush()
    {
        double now = Time.realtimeSinceStartupAsDouble, elapsed = now - started;
        if (intervals.Count > 0 && elapsed > 0)
        {
            float[] sorted = intervals.ToArray(); Array.Sort(sorted);
            int index = Mathf.Clamp(Mathf.CeilToInt(sorted.Length * .95f) - 1, 0, sorted.Length - 1);
            Debug.Log(FormattableString.Invariant($"[REDBOOL PERF] scene={scene} frames={sorted.Length} seconds={elapsed:F2} fps={sorted.Length/elapsed:F2} frame_p95_ms={sorted[index]:F2} frame_max_ms={sorted[sorted.Length-1]:F2} focused_frames={focusedFrames} resolution={Screen.width}x{Screen.height} scale_min={minTimeScale:F3} scale_max={maxTimeScale:F3} realtime_s={now:F3} utc={DateTime.UtcNow:O}"));
        }
        intervals.Clear(); focusedFrames = 0;
        minTimeScale = float.PositiveInfinity; maxTimeScale = 0;
        started = now;
    }
    void OnApplicationFocus(bool focused)
    {
        Debug.Log(FormattableString.Invariant($"[REDBOOL FOCUS] focused={focused} realtime_s={Time.realtimeSinceStartupAsDouble:F3} utc={DateTime.UtcNow:O}"));
    }
    void OnApplicationQuit() { UpdateActivity(false); Flush(); }
    void OnDestroy() { UpdateActivity(false); }
}
