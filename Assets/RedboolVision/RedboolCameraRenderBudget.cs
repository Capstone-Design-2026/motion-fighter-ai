using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Reserve GPU/memory bandwidth for local vision on high-DPI displays.
/// Only 3D render targets are scaled; the window, UI and camera input stay unchanged.</summary>
public sealed class RedboolCameraRenderBudget : MonoBehaviour
{
    const int MaxWidth = 1920, MaxHeight = 1200;
    const int LatencyWidth = 1280, LatencyHeight = 720;
    UniversalRenderPipelineAsset owned;
    float originalScale, appliedScale, nextCheck;
    public float CurrentScale => owned != null ? appliedScale : 1f;

    public static float ScaleFor(int width, int height, float configuredScale, bool inferencePressure = false)
    {
        if (width <= 0 || height <= 0) return configuredScale;
        int maxWidth = inferencePressure ? LatencyWidth : MaxWidth;
        int maxHeight = inferencePressure ? LatencyHeight : MaxHeight;
        float limit = Mathf.Min((float)maxWidth / width, (float)maxHeight / height);
        return Mathf.Min(configuredScale, Mathf.Clamp(limit, .1f, 1f));
    }

    void Update()
    {
        if (Time.unscaledTime < nextCheck) return;
        nextCheck = Time.unscaledTime + .5f;
        Refresh();
    }

    public void Refresh()
    {
        if (Application.isEditor) return;
        var runtime = RedboolCameraRuntime.Instance;
        var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        bool active = runtime != null && runtime.Running && runtime.ShowPanel;
        if (!active || pipeline != owned) Release();
        if (!active || pipeline == null) return;
        if (owned == null)
        {
            owned = pipeline; originalScale = appliedScale = owned.renderScale;
        }
        // Respect a lower scale selected by another game setting while active.
        if (!Mathf.Approximately(owned.renderScale, appliedScale)) originalScale = owned.renderScale;
        bool inferencePressure = runtime.InferencePressure || RedboolDemoSettings.LowQuality;
        float desired = ScaleFor(Screen.width, Screen.height, originalScale, inferencePressure);
        if (Mathf.Approximately(desired, owned.renderScale)) { appliedScale = desired; return; }
        owned.renderScale = desired; appliedScale = desired;
        Debug.Log(FormattableString.Invariant($"[REDBOOL RENDER BUDGET] display={Screen.width}x{Screen.height} render_scale={desired:F3} target={Mathf.RoundToInt(Screen.width * desired)}x{Mathf.RoundToInt(Screen.height * desired)} ui_native=true inference_pressure={inferencePressure}"));
    }

    void Release()
    {
        if (owned != null && Mathf.Approximately(owned.renderScale, appliedScale)) owned.renderScale = originalScale;
        owned = null;
    }
    void OnDisable() { Release(); }
    void OnDestroy() { Release(); }
}
