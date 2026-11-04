using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Camera-local treatment of selected scenery; never modifies fighter rendering.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(Camera))]
public sealed class SelectiveSceneryAtmosphere : MonoBehaviour
{
    [Serializable] public sealed class Surface
    {
        public Renderer renderer;
        [Range(0, 2)] public int kind; // Stone, simple roof, simple timber.
        public Material[] originalMaterials;
    }
    public Surface[] surfaces = Array.Empty<Surface>();
    [Range(0, 6)] public float radiusAt1600 = 3.5f;
    [Range(0, 1)] public float softening = .90f;
    [Range(0, 1)] public float desaturation = .08f;
    [Range(0, 1)] public float haze = .07f;
    public Color hazeColor = new Color(.52f, .55f, .53f);
    [NonSerialized] public bool showMask;
    public const uint ReservedLayers = (1u << 24) | (1u << 25) | (1u << 26);
    public static uint Layer(int kind) => 1u << (24 + Mathf.Clamp(kind, 0, 2));
    readonly Dictionary<Renderer, uint> previousLayers = new Dictionary<Renderer, uint>();
    public bool HasTargets => surfaces != null && surfaces.Length > 0;

    void OnEnable()
    {
        if (surfaces == null) return;
        foreach (var s in surfaces)
        {
            if (s == null || !s.renderer) continue;
            if (!previousLayers.ContainsKey(s.renderer)) previousLayers.Add(s.renderer, s.renderer.renderingLayerMask);
            // Retain all existing light/rendering layers.
            s.renderer.renderingLayerMask |= Layer(s.kind);
        }
    }
    void OnDisable()
    {
        foreach (var pair in previousLayers) if (pair.Key) pair.Key.renderingLayerMask = pair.Value;
        previousLayers.Clear();
    }
}
