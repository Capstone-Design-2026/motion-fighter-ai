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
    void Start() { scene = SceneManager.GetActiveScene().name; started = Time.realtimeSinceStartupAsDouble; }
    void Update()
    {
        string current = SceneManager.GetActiveScene().name;
        if (current != scene) { Flush(); scene = current; started = Time.realtimeSinceStartupAsDouble; }
        intervals.Add(Time.unscaledDeltaTime * 1000f);
        if (Time.realtimeSinceStartupAsDouble - started >= 10) Flush();
    }
    void Flush()
    {
        double now = Time.realtimeSinceStartupAsDouble, elapsed = now - started;
        if (intervals.Count > 0 && elapsed > 0)
        {
            float[] sorted = intervals.ToArray(); Array.Sort(sorted);
            int index = Mathf.Clamp(Mathf.CeilToInt(sorted.Length * .95f) - 1, 0, sorted.Length - 1);
            Debug.Log(FormattableString.Invariant($"[REDBOOL PERF] scene={scene} frames={sorted.Length} seconds={elapsed:F2} fps={sorted.Length/elapsed:F2} frame_p95_ms={sorted[index]:F2} frame_max_ms={sorted[sorted.Length-1]:F2}"));
        }
        intervals.Clear(); started = now;
    }
    void OnApplicationQuit() { Flush(); }
}
