using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Render the actual materials through the actual arena camera/URP before input
// unlocks. Merely loading shaders does not prepare Metal's render pipeline states.
[DefaultExecutionOrder(200)] // After HeroPunchSurfaceVisibility chooses the normal visible skin.
public sealed class RedboolRenderWarmup : MonoBehaviour
{
    public RedboolBattlePreparation preparation;
    public EnemySpecial[] characterSpecials;
    static RedboolRenderWarmup renderOwner;
    readonly List<EnemySpecial.GhostRenderSample> characterSamples = new List<EnemySpecial.GhostRenderSample>();
    readonly List<GameObject> visuals = new List<GameObject>();
    Camera cameraUsed;
    RenderTexture previous, target;
    bool restored, finished, preparedPostFx;
    sealed class PunchSurface
    {
        public SkinnedMeshRenderer renderer;
        public bool enabled, forceRenderingOff, updateWhenOffscreen;
    }
    readonly List<PunchSurface> punchSurfaces = new List<PunchSurface>();
    bool warmingPunchSurfaces;
    // 2026-11-16 watchdog: the Start coroutine sometimes never resumes after its first yield
    // (seen in batch PvP checks, more often when that frame does a little more work). PvP then
    // waits at "전투 효과 준비 중" forever. Retry the same steps once from Update, then give up
    // on the warmup rather than block the match (the first effect may hitch instead).
    const double StallSeconds = 1.5;
    double stepStarted = -1;
    int stepFrame = -1, attempts;
    bool progressed;

    void PreparePunchSurfaces()
    {
        // This warmup is installed by the existing story preparation gate only.
        // Leave remote view/controller ownership and its CurrentSurface untouched.
        if (RedboolPvp.Active) return;
        foreach (var skin in FindObjectsByType<HeroPunchSurfaceVisibility>(FindObjectsSortMode.None))
        {
            if (!skin.isActiveAndEnabled) continue;
            foreach (var renderer in new[] { skin.jabSurface, skin.straightSurface })
            {
                if (renderer == null || renderer.sharedMesh == null || !renderer.gameObject.activeInHierarchy) continue;
                if (punchSurfaces.Exists(saved => saved.renderer == renderer)) continue;
                punchSurfaces.Add(new PunchSurface { renderer = renderer, enabled = renderer.enabled,
                    forceRenderingOff = renderer.forceRenderingOff, updateWhenOffscreen = renderer.updateWhenOffscreen });
            }
        }
        warmingPunchSurfaces = true;
    }

    void LateUpdate()
    {
        if (!warmingPunchSurfaces || cameraUsed == null || cameraUsed.targetTexture != target) return;
        // Use the actual renderers, so their large mesh/skin GPU buffers are ready
        // before the first attack. Only the temporary camera target sees this.
        foreach (var saved in punchSurfaces) if (saved.renderer != null)
        {
            saved.renderer.enabled = true;
            saved.renderer.forceRenderingOff = false;
            saved.renderer.updateWhenOffscreen = true;
        }
    }

    void Start() => Begin();

    void Begin()
    {
        attempts++; progressed = false;
        stepStarted = Time.realtimeSinceStartupAsDouble; stepFrame = Time.frameCount;
        StartCoroutine(Warm());
    }

    void Update()
    {
        if (finished || progressed || stepStarted < 0) return;
        if (Time.realtimeSinceStartupAsDouble - stepStarted < StallSeconds || Time.frameCount - stepFrame < 10) return;
        StopAllCoroutines();
        if (attempts < 2)
        {
            Debug.LogWarning("[REDBOOL PREWARM] warmup did not resume after its first frame; retrying from Update");
            Begin();
        }
        else
        {
            Debug.LogWarning("[REDBOOL PREWARM] warmup did not resume twice; skipping it so the match can start");
            Finish();
        }
    }

    IEnumerator Warm()
    {
        // Let normal Start methods prepare assets, camera and render templates.
        yield return null;
        progressed = true;
        if (!isActiveAndEnabled || finished) { Finish(); yield break; }
        cameraUsed = Camera.main;
        if (cameraUsed == null) { Finish(); yield break; }
        // Only one preparation may own the arena camera. Cancel restores the
        // previous owner immediately, before this owner saves its target.
        if (renderOwner != null && renderOwner != this) renderOwner.Cancel();
        renderOwner = this;
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
            // Story preparation has the active scene opponent; PvP passes only
            // its two selected participants (including guest visual-only ones).
            var selectedSpecials = characterSpecials ?? FindObjectsByType<EnemySpecial>(FindObjectsSortMode.None);
            if (selectedSpecials != null)
                foreach (var special in selectedSpecials)
                {
                    if (special == null || !special.isActiveAndEnabled) continue;
                    var holder = new GameObject("Character warmup sample"); holder.SetActive(false); visuals.Add(holder);
                    // Centre the full-height selected avatar inside the actual
                    // arena camera. The sample owns only visual resources.
                    int firstSample = characterSamples.Count;
                    special.CreateGhostRenderSamples(holder.transform, point - cameraUsed.transform.up * .8f, characterSamples);
                    if (characterSamples.Count > firstSample)
                    {
                        holder.SetActive(true);
                        for (int i = firstSample; i < characterSamples.Count; i++) characterSamples[i].PrepareForRender();
                    }
                }
            QiPostFx.PrepareRenderWeight(1f); preparedPostFx = true;
            PreparePunchSurfaces();
            // SRP renders into the temporary target during normal player frames.
            // More than one frame prepares both transient geometry and post effects.
            for (int i = 0; i < 4; i++) yield return null;
            Debug.Log($"[REDBOOL PREWARM] punch-surfaces={punchSurfaces.Count} character-samples={characterSamples.Count} offscreen_frames=4 controller_unchanged=true");
        }
        finally { Restore(); }
        // Also let the restored backbuffer render before accepting live input.
        yield return null; yield return null;
        Debug.Log("[REDBOOL PREWARM] render complete; gameplay input unlocked; camera target restored");
        Finish();
    }

    void Restore()
    {
        if (restored) return; restored = true;
        warmingPunchSurfaces = false;
        foreach (var saved in punchSurfaces) if (saved.renderer != null)
        {
            saved.renderer.enabled = saved.enabled;
            saved.renderer.forceRenderingOff = saved.forceRenderingOff;
            saved.renderer.updateWhenOffscreen = saved.updateWhenOffscreen;
        }
        punchSurfaces.Clear();
        foreach (var sample in characterSamples) sample.Dispose();
        characterSamples.Clear();
        foreach (var go in visuals) if (go != null) { go.SetActive(false); Destroy(go); }
        if (cameraUsed != null && cameraUsed.targetTexture == target) cameraUsed.targetTexture = previous;
        if (target != null) RenderTexture.ReleaseTemporary(target);
        if (preparedPostFx && renderOwner == this) QiPostFx.PrepareRenderWeight(0f);
        if (renderOwner == this) renderOwner = null;
    }
    void Finish()
    {
        if (finished) return;
        finished = true;
        Restore();
        if (preparation != null) preparation.EffectsPreparing = false;
        // PvP waits for this component to disappear. A disabled/cancelled Start
        // must not leave a permanently non-null preparation gate behind.
        Destroy(this);
    }
    public void Cancel() { StopAllCoroutines(); Finish(); }
    void OnDisable() { Cancel(); }
}
