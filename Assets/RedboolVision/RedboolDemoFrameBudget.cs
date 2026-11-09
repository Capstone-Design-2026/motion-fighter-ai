using UnityEngine;

// Leave CPU/GPU time for the camera on the same machine. The project uses
// vSyncCount=0; without an explicit cap its standalone UI renders unbounded.
public static class RedboolDemoFrameBudget
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Apply()
    {
        if (Application.isEditor) return;
        Application.targetFrameRate = 60;
        // Routine combat/performance logs do not need expensive full call stacks.
        // Preserve warning/error/exception stacks for debugging.
        Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
        Debug.Log("[REDBOOL] Standalone render limit: 60 fps");
        var metrics = new GameObject("REDBOOL Frame Metrics");
        Object.DontDestroyOnLoad(metrics);
        metrics.AddComponent<RedboolFrameMetrics>();
    }
}
