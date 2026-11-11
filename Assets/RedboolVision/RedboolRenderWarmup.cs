using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Render the actual materials through the actual arena camera/URP before input
// unlocks. Merely loading shaders does not prepare Metal's render pipeline states.
public sealed class RedboolRenderWarmup : MonoBehaviour
{
    public RedboolBattlePreparation preparation;
    readonly List<GameObject> visuals = new List<GameObject>();
    Camera cameraUsed;
    RenderTexture previous, target;
    bool restored;

    IEnumerator Start()
    {
        // Let normal Start methods prepare assets, camera and render templates.
        yield return null;
        cameraUsed = Camera.main;
        if (cameraUsed == null) { Restore(); if (preparation != null) preparation.EffectsPreparing = false; Destroy(this); yield break; }
        previous = cameraUsed.targetTexture;
        target = RenderTexture.GetTemporary(Mathf.Max(1, cameraUsed.pixelWidth), Mathf.Max(1, cameraUsed.pixelHeight), 24,
            cameraUsed.allowHDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default);
        cameraUsed.targetTexture = target;
        try
        {
            Vector3 point = cameraUsed.transform.position + cameraUsed.transform.forward * 4f;
            var hit = HitVfx.WarmVisual(point, false); if (hit != null) visuals.Add(hit);
            var guard = HitVfx.WarmVisual(point, true); if (guard != null) visuals.Add(guard);
            var assets = QiPalmAssets.Load();
            if (assets != null)
            {
                // Keep gameplay scripts/colliders off. These are visual samples,
                // never real attacks; HP, charge, time scale and saves stay untouched.
                foreach (var prefab in new[] { assets.ready, assets.charge, assets.wave, assets.muzzle, assets.impact })
                {
                    if (prefab == null) continue;
                    var holder = new GameObject("Effect warmup sample"); holder.SetActive(false); visuals.Add(holder);
                    var sample = Instantiate(prefab, holder.transform, false); sample.transform.position = point;
                    foreach (var script in sample.GetComponentsInChildren<MonoBehaviour>(true)) script.enabled = false;
                    foreach (var c in sample.GetComponentsInChildren<Collider>(true)) c.enabled = false;
                    foreach (var c in sample.GetComponentsInChildren<Collider2D>(true)) c.enabled = false;
                    foreach (var body in sample.GetComponentsInChildren<Rigidbody>(true)) body.isKinematic = true;
                    foreach (var audio in sample.GetComponentsInChildren<AudioSource>(true)) { audio.playOnAwake = false; audio.enabled = false; }
                    holder.SetActive(true);
                    foreach (var ps in sample.GetComponentsInChildren<ParticleSystem>()) { ps.Simulate(.15f, false, true); ps.Play(false); }
                }
                if (assets.brush != null)
                {
                    var cut = QiCutIn.Play(assets.brush, -1, assets.cutInArt); visuals.Add(cut.gameObject);
                    cut.enabled = false; cut.Seek(.2f);
                    var canvas = cut.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = cameraUsed; canvas.planeDistance = 1f;
                }
            }
            QiPostFx.PrepareRenderWeight(1f);
            // SRP renders into the temporary target during normal player frames.
            // More than one frame prepares both transient geometry and post effects.
            for (int i = 0; i < 4; i++) yield return null;
        }
        finally { Restore(); }
        // Also let the restored backbuffer render before accepting live input.
        yield return null; yield return null;
        if (preparation != null) preparation.EffectsPreparing = false;
        Debug.Log("[REDBOOL PREWARM] render complete; gameplay input unlocked; camera target restored");
        Destroy(this);
    }

    void Restore()
    {
        if (restored) return; restored = true;
        foreach (var go in visuals) if (go != null) { go.SetActive(false); Destroy(go); }
        if (cameraUsed != null) cameraUsed.targetTexture = previous;
        if (target != null) RenderTexture.ReleaseTemporary(target);
        QiPostFx.PrepareRenderWeight(0f);
    }
    void OnDisable() { Restore(); if (preparation != null) preparation.EffectsPreparing = false; }
}
