using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// Real arena/mesh rendering fixture; never opens a physical camera or supplies attacks.
[InitializeOnLoad]
public static class RedboolPunchWarmupCheck
{
    const string Active = "RedboolPunchWarmupCheck.Active", SaveKey = "VisionStriker.Run";
    static readonly List<string> checks = new List<string>();
    static IEnumerator flow;
    static int frame = -1;
    static string Output => Environment.GetEnvironmentVariable("REDBOOL_PUNCH_WARMUP_CHECK_OUTPUT");
    internal static HeroPunchSurfaceVisibility Skin;
    internal static RedboolRenderWarmup Warmup;
    internal static readonly HashSet<int> RenderedFrames = new HashSet<int>();
    internal static bool BothVisible, ControllerChanged;

    static RedboolPunchWarmupCheck() { if (SessionState.GetBool(Active, false)) Subscribe(); }
    public static void Run()
    {
        if (!Application.isBatchMode || string.IsNullOrEmpty(Output)) throw new Exception("Isolated batch output required");
        Directory.CreateDirectory(Output);
        SessionState.SetBool(Active + ".HadSave", PlayerPrefs.HasKey(SaveKey));
        SessionState.SetString(Active + ".Save", PlayerPrefs.GetString(SaveKey, ""));
        SessionState.SetFloat(Active + ".StartupStart", (float)EditorApplication.timeSinceStartup);
        SessionState.SetBool(Active + ".PlayStarted", false);
        EditorSceneManager.OpenScene("Assets/Scenes/ScoutPass.unity");
        SessionState.SetBool(Active, true); Subscribe();
        EditorApplication.delayCall += () => EditorApplication.EnterPlaymode();
    }
    static void Subscribe() { EditorApplication.update -= Tick; EditorApplication.update += Tick; }
    static void Tick()
    {
        if (!Application.isPlaying || EditorApplication.isCompiling)
        {
            if (!SessionState.GetBool(Active + ".PlayStarted", false) &&
                EditorApplication.timeSinceStartup - SessionState.GetFloat(Active + ".StartupStart", 0) > 300)
                Finish("Startup timeout before Play; no assertions ran");
            return;
        }
        if (!SessionState.GetBool(Active + ".PlayStarted", false))
        {
            SessionState.SetBool(Active + ".PlayStarted", true);
            SessionState.SetFloat(Active + ".PlayStart", (float)EditorApplication.timeSinceStartup);
        }
        if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Active + ".PlayStart", 0) > 150) { Finish("Runtime timeout in Play"); return; }
        EditorApplication.isPaused = false; EditorApplication.QueuePlayerLoopUpdate();
        if (frame == Time.frameCount) return; frame = Time.frameCount;
        try { if (flow == null) flow = Tests(); if (!flow.MoveNext()) Finish(null); }
        catch (Exception error) { Finish(error.ToString()); }
    }
    static void Check(bool ok, string label)
    {
        checks.Add((ok ? "PASS " : "FAIL ") + label); File.WriteAllLines(Path.Combine(Output, "checks.txt"), checks);
        if (!ok) throw new Exception(label);
    }
    static IEnumerator Tests()
    {
        yield return null; yield return null;
        var gate = Object.FindAnyObjectByType<RedboolBattlePreparation>();
        Check(gate != null, "Existing combat preparation gate installed");
        double deadline = Time.realtimeSinceStartupAsDouble + 30;
        while (gate.EffectsPreparing) { if (Time.realtimeSinceStartupAsDouble > deadline) throw new Exception("Initial warmup did not finish"); yield return null; }
        var fighter = Object.FindAnyObjectByType<PlayerController>().GetComponent<Fighter>();
        Skin = fighter.GetComponent<Fighter3DView>().visualRoot.GetComponentInChildren<HeroPunchSurfaceVisibility>(true);
        Check(Skin != null && Skin.jabSurface != null && Skin.straightSurface != null, "Actual Hero02 jab and straight correction renderers exist");
        Check(Skin.jabSurface.sharedMesh != Skin.straightSurface.sharedMesh, "Distinct authored correction meshes retained");
        Check(Skin.enabled && Skin.CurrentSurface == 0, "Normal controller begins neutral and enabled");
        var camera = Camera.main; Check(camera != null && camera.targetTexture == null, "Normal scene camera restored after automatic warmup");
        int hp = fighter.Health.CurrentHealth, charge = fighter.GetComponent<SpecialMove>().Charge;
        int state = Skin.animator.GetCurrentAnimatorStateInfo(0).shortNameHash;
        bool[] originalOffscreen = { Skin.jabSurface.updateWhenOffscreen, Skin.straightSurface.updateWhenOffscreen };
        bool[] originalForced = { Skin.jabSurface.forceRenderingOff, Skin.straightSurface.forceRenderingOff };
        var probe = new GameObject("Warmup native render probe").AddComponent<RedboolPunchWarmupRenderProbe>();
        var previousTarget = new RenderTexture(320, 180, 24); previousTarget.Create(); camera.targetTexture = previousTarget;
        foreach (bool interrupt in new[] { false, true })
        {
            // Non-default flags make exact cleanup, including cancellation, observable.
            foreach (var r in new[] { Skin.jabSurface, Skin.straightSurface }) { r.forceRenderingOff = true; r.updateWhenOffscreen = false; }
            RenderedFrames.Clear(); BothVisible = ControllerChanged = false;
            typeof(RedboolBattlePreparation).GetProperty(nameof(RedboolBattlePreparation.EffectsPreparing)).SetValue(gate, true);
            Warmup = new GameObject("Correction warmup fixture").AddComponent<RedboolRenderWarmup>(); Warmup.preparation = gate;
            deadline = Time.realtimeSinceStartupAsDouble + 15;
            while (Warmup != null && gate.EffectsPreparing)
            {
                Check(!gate.AllowsPlayer, "Input remains locked during correction warmup");
                if (Time.realtimeSinceStartupAsDouble > deadline) throw new Exception("Correction warmup did not finish");
                if (interrupt && RenderedFrames.Count > 0) { Warmup.enabled = false; Object.Destroy(Warmup.gameObject); break; }
                yield return null;
            }
            yield return null; yield return null;
            string mode = interrupt ? "Interrupted" : "Completed";
            Check(BothVisible && RenderedFrames.Count > 0, mode + " warmup submitted both real visible correction skins through arena URP camera");
            Check(RenderedFrames.Count <= 4, mode + " correction work stays within existing four warmup frames");
            Check(!ControllerChanged && Skin.enabled && Skin.CurrentSurface == 0, mode + " leaves controller and network surface state unchanged");
            Check(!Skin.jabSurface.enabled && !Skin.straightSurface.enabled, mode + " restores normal hidden correction renderers");
            Check(Skin.jabSurface.forceRenderingOff && Skin.straightSurface.forceRenderingOff && !Skin.jabSurface.updateWhenOffscreen && !Skin.straightSurface.updateWhenOffscreen, mode + " restores exact renderer flags");
            Check(camera.targetTexture == previousTarget && !gate.EffectsPreparing, mode + " restores prior camera target and preparation state");
            Check(fighter.Health.CurrentHealth == hp && fighter.GetComponent<SpecialMove>().Charge == charge && !fighter.IsAttacking && !fighter.IsSpecialActive, mode + " produces no damage, attack or special charge");
            Check(Skin.animator.GetCurrentAnimatorStateInfo(0).shortNameHash == state, mode + " does not play or change an attack animation");
        }
        Object.Destroy(probe.gameObject); camera.targetTexture = null; previousTarget.Release(); Object.Destroy(previousTarget);
        Skin.jabSurface.forceRenderingOff = originalForced[0]; Skin.straightSurface.forceRenderingOff = originalForced[1];
        Skin.jabSurface.updateWhenOffscreen = originalOffscreen[0]; Skin.straightSurface.updateWhenOffscreen = originalOffscreen[1];
        Check(PlayerPrefs.GetString(SaveKey, "") == SessionState.GetString(Active + ".Save", ""), "Campaign save unchanged");
    }
    static void Finish(string error)
    {
        SessionState.SetBool(Active, false); EditorApplication.update -= Tick;
        if (SessionState.GetBool(Active + ".HadSave", false)) PlayerPrefs.SetString(SaveKey, SessionState.GetString(Active + ".Save", "")); else PlayerPrefs.DeleteKey(SaveKey);
        PlayerPrefs.Save();
        File.WriteAllText(Path.Combine(Output, "result.txt"), error == null ? "PASS\n" + checks.Count + " checks" : "FAIL\n" + error);
        EditorApplication.Exit(error == null ? 0 : 1);
    }
}

// Batch Editor may have no visible Game view. Explicitly submit the same camera
// after production LateUpdate; this is test-only and uses the existing temporary target.
[DefaultExecutionOrder(300)]
public sealed class RedboolPunchWarmupRenderProbe : MonoBehaviour
{
    void LateUpdate()
    {
        var skin = RedboolPunchWarmupCheck.Skin; var warmup = RedboolPunchWarmupCheck.Warmup; var cam = Camera.main;
        if (warmup == null || !warmup.isActiveAndEnabled || skin == null || cam == null || cam.targetTexture == null) return;
        if (!skin.jabSurface.enabled || !skin.straightSurface.enabled || skin.jabSurface.forceRenderingOff || skin.straightSurface.forceRenderingOff) return;
        var planes = GeometryUtility.CalculateFrustumPlanes(cam);
        bool visible = GeometryUtility.TestPlanesAABB(planes, skin.jabSurface.bounds) && GeometryUtility.TestPlanesAABB(planes, skin.straightSurface.bounds)
            && (cam.cullingMask & (1 << skin.jabSurface.gameObject.layer)) != 0 && (cam.cullingMask & (1 << skin.straightSurface.gameObject.layer)) != 0;
        RedboolPunchWarmupCheck.ControllerChanged |= !skin.enabled || skin.CurrentSurface != 0;
        RenderPipeline.SubmitRenderRequest(cam, new RenderPipeline.StandardRequest { destination = cam.targetTexture });
        RedboolPunchWarmupCheck.BothVisible |= visible;
        RedboolPunchWarmupCheck.RenderedFrames.Add(Time.frameCount);
    }
}
