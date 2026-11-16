using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// Actual selected scout avatar and production warmup, without camera input,
// network peers, attacks, charge refill or damage. Not a frame-time benchmark.
[InitializeOnLoad]
public static class RedboolScoutGhostWarmupCheck
{
    const string Active = "RedboolScoutGhostWarmupCheck.Active", SaveKey = "VisionStriker.Run";
    static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly List<string> checks = new List<string>();
    static readonly HashSet<int> renderedFrames = new HashSet<int>();
    static IEnumerator flow;
    static int frame = -1;
    static EnemySpecial spell;
    static Camera cameraUsed;
    static RenderTexture originalTarget;
    static bool observing, sawBothPoses, wrongSample;
    static string Output => Environment.GetEnvironmentVariable("REDBOOL_SCOUT_GHOST_CHECK_OUTPUT");
    [Serializable] sealed class Result
    {
        public bool passed; public string error; public string[] checks;
        public bool physical_camera_used = false, network_peer_used = false, frame_time_benchmark = false;
    }
    static RedboolScoutGhostWarmupCheck() { if (SessionState.GetBool(Active, false)) Subscribe(); }
    public static void Run()
    {
        if (!Application.isBatchMode || string.IsNullOrEmpty(Output)
            || Array.IndexOf(Environment.GetCommandLineArgs(), "--redbool-external-camera") < 0)
            throw new Exception("Isolated batch output and --redbool-external-camera are required");
        Directory.CreateDirectory(Output);
        SessionState.SetBool(Active + ".HadSave", PlayerPrefs.HasKey(SaveKey));
        SessionState.SetString(Active + ".Save", PlayerPrefs.GetString(SaveKey, ""));
        SessionState.SetFloat(Active + ".Started", (float)EditorApplication.timeSinceStartup);
        SessionState.SetBool(Active + ".Playing", false);
        EditorSceneManager.OpenScene("Assets/Scenes/ScoutPass.unity");
        SessionState.SetBool(Active, true); Subscribe();
        EditorApplication.delayCall += () => EditorApplication.EnterPlaymode();
    }
    static void Subscribe() { EditorApplication.update -= Tick; EditorApplication.update += Tick; }
    static void Tick()
    {
        if (!Application.isPlaying || EditorApplication.isCompiling)
        {
            if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Active + ".Started", 0) > 300) Finish("Startup timeout");
            return;
        }
        if (!SessionState.GetBool(Active + ".Playing", false))
        {
            SessionState.SetBool(Active + ".Playing", true);
            SessionState.SetFloat(Active + ".Started", (float)EditorApplication.timeSinceStartup);
        }
        if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Active + ".Started", 0) > 120) { Finish("Play timeout"); return; }
        EditorApplication.isPaused = false; EditorApplication.QueuePlayerLoopUpdate();
        if (frame == Time.frameCount) return; frame = Time.frameCount;
        try { if (flow == null) flow = Tests(); if (!flow.MoveNext()) Finish(null); }
        catch (Exception error) { Finish(error.ToString()); }
    }
    static void Check(bool value, string label)
    {
        checks.Add((value ? "PASS " : "FAIL ") + label);
        File.WriteAllLines(Path.Combine(Output, "checks.txt"), checks);
        if (!value) throw new Exception(label);
    }
    static object Get(object owner, string field) => owner.GetType().GetField(field, Private).GetValue(owner);
    static void Pvp(bool active) => typeof(RedboolPvp).GetField("<Active>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, active);
    static List<EnemySpecial.GhostRenderSample> Owned => (List<EnemySpecial.GhostRenderSample>)Get(spell, "ghostRenderSamples");
    static void Render(ScriptableRenderContext context, Camera camera)
    {
        if (!observing || camera != cameraUsed || camera.targetTexture == originalTarget || Owned.Count == 0) return;
        renderedFrames.Add(Time.frameCount);
        bool low = false, high = false;
        foreach (var sample in Owned)
        {
            low |= sample.AnimationState == "LOWKICK"; high |= sample.AnimationState == "HIGHKICK";
            wrongSample |= sample.Dark || sample.Root == null || !sample.Root.activeInHierarchy;
        }
        sawBothPoses |= low && high && Owned.Count == 2;
    }
    static IEnumerator Tests()
    {
        yield return null; yield return null;
        double until = Time.realtimeSinceStartupAsDouble + 40;
        while (Object.FindAnyObjectByType<RedboolRenderWarmup>() != null)
        { if (Time.realtimeSinceStartupAsDouble > until) throw new Exception("Initial warmup timeout"); yield return null; }
        foreach (var receiver in Object.FindObjectsByType<RedboolDemoReceiver>(FindObjectsSortMode.None)) receiver.enabled = false;
        foreach (var receiver in Object.FindObjectsByType<UdpActionReceiver>(FindObjectsSortMode.None)) receiver.enabled = false;
        foreach (var gate in Object.FindObjectsByType<RedboolBattlePreparation>(FindObjectsSortMode.None)) gate.enabled = false;
        var ai = Object.FindAnyObjectByType<EnemyAI>(); ai.enabled = false;
        var foe = ai.GetComponent<Fighter>(); var hero = Object.FindAnyObjectByType<PlayerController>().GetComponent<Fighter>();
        spell = ai.GetComponent<EnemySpecial>();
        var source = foe.GetComponent<Fighter3DView>().visualRoot;
        Check(source != null && source.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length > 0, "Actual ScoutPass selected avatar has real skinned meshes");
        Pvp(true); cameraUsed = Camera.main;
        Check(cameraUsed != null, "Actual arena render camera exists");
        RenderPipelineManager.beginCameraRendering += Render;
        foreach (bool remote in new[] { false, true })
        {
            string label = remote ? "guest presentation" : "host authority";
            Check(spell.ConfigureForPlayer("scout", hero, remote), label + " selects scout technique");
            int hp0 = foe.Health.CurrentHealth, hp1 = hero.Health.CurrentHealth, charge = spell.Charge, serial = spell.FireSerial;
            int updaters = ((IList)Get(spell, "updaters")).Count;
            Vector3 position = source.position; Quaternion rotation = source.rotation;
            Time.timeScale = .37f; Time.fixedDeltaTime = .0074f;
            var holder = new GameObject("Scout warmup check holder"); holder.SetActive(false);
            var samples = new List<EnemySpecial.GhostRenderSample>();
            spell.CreateGhostRenderSamples(holder.transform, Vector3.up, samples);
            Check(samples.Count == 2 && samples[0].AnimationState == "LOWKICK" && samples[1].AnimationState == "HIGHKICK", label + " creates both actual scout animation variants");
            foreach (var sample in samples)
            {
                Check(!sample.Dark && !sample.Root.activeInHierarchy && sample.Root.transform.IsChildOf(holder.transform), label + " light ghost starts under inactive holder");
                Check(sample.Root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length > 0, label + " sample includes actual skin buffers");
                Check(sample.Root.GetComponentsInChildren<MonoBehaviour>(true).Length == 0
                    && sample.Root.GetComponentsInChildren<Collider>(true).Length == 0
                    && sample.Root.GetComponentsInChildren<Collider2D>(true).Length == 0
                    && sample.Root.GetComponentsInChildren<AudioSource>(true).Length == 0, label + " strips gameplay, collisions and audio");
                foreach (var rb in sample.Root.GetComponentsInChildren<Rigidbody>(true)) Check(rb.isKinematic && !rb.detectCollisions, label + " disables 3D physics");
                foreach (var rb in sample.Root.GetComponentsInChildren<Rigidbody2D>(true)) Check(!rb.simulated, label + " disables 2D physics");
            }
            holder.SetActive(true);
            foreach (var sample in samples)
            {
                sample.PrepareForRender();
                var animators = sample.Root.GetComponentsInChildren<Animator>(true);
                Check(animators.Length > 0, label + " retains actual ghost animator");
                foreach (var anim in animators)
                    Check(!anim.applyRootMotion && !anim.fireEvents && anim.HasState(0, Animator.StringToHash(sample.AnimationState))
                        && anim.GetCurrentAnimatorStateInfo(0).shortNameHash == Animator.StringToHash(sample.AnimationState), label + " samples requested real pose without events or root motion");
            }
            Check(foe.Health.CurrentHealth == hp0 && hero.Health.CurrentHealth == hp1 && spell.Charge == charge && spell.FireSerial == serial
                && !spell.IsPlaying && Get(spell, "routine") == null && ((IList)Get(spell, "updaters")).Count == updaters
                && source.position == position && source.rotation == rotation, label + " direct samples do not affect health, charge, serial, routine or source transform");
            foreach (var sample in samples)
            {
                var root = sample.Root; sample.Dispose(); sample.Dispose();
                Check(sample.Root == null && !root.activeSelf, label + " disposal is immediate and idempotent");
            }
            Object.Destroy(holder); yield return null;
            Check(Owned.Count == 0, label + " releases all sample ownership");
            foreach (bool cancel in new[] { false, true })
            {
                originalTarget = cameraUsed.targetTexture; renderedFrames.Clear(); sawBothPoses = wrongSample = false; observing = true;
                var warmup = foe.gameObject.AddComponent<RedboolRenderWarmup>(); warmup.characterSpecials = new[] { spell };
                until = Time.realtimeSinceStartupAsDouble + 15;
                while (warmup != null)
                {
                    if (Time.realtimeSinceStartupAsDouble > until) throw new Exception("Production warmup timeout");
                    if (cancel && renderedFrames.Count > 0) { warmup.Cancel(); break; }
                    yield return null;
                }
                observing = false; yield return null; yield return null;
                string mode = cancel ? "cancelled" : "completed";
                Check(sawBothPoses && !wrongSample && renderedFrames.Count > 0 && renderedFrames.Count <= 4, label + " " + mode + " uses both light samples within existing four arena render frames");
                Check(cameraUsed.targetTexture == originalTarget && Owned.Count == 0, label + " " + mode + " restores camera target and disposes all samples");
                Check(foe.Health.CurrentHealth == hp0 && hero.Health.CurrentHealth == hp1 && spell.Charge == charge && spell.FireSerial == serial
                    && !spell.IsPlaying && !foe.IsAttacking && !hero.IsAttacking && Mathf.Approximately(Time.timeScale, .37f)
                    && Mathf.Approximately(Time.fixedDeltaTime, .0074f), label + " " + mode + " emits no attack/damage/charge/time ownership change");
            }
            holder = new GameObject("Scout outstanding samples"); holder.SetActive(false); samples.Clear();
            spell.CreateGhostRenderSamples(holder.transform, Vector3.zero, samples); spell.ConfigureForPlayer("scout", hero, remote);
            Check(samples.TrueForAll(x => x.Root == null) && Owned.Count == 0, label + " reconfiguration cancels outstanding samples");
            samples.Clear(); spell.CreateGhostRenderSamples(holder.transform, Vector3.zero, samples); spell.enabled = false;
            Check(samples.TrueForAll(x => x.Root == null) && Owned.Count == 0, label + " disabling owner cancels outstanding samples");
            spell.enabled = true; Object.Destroy(holder); yield return null;
        }
        var otherHolder = new GameObject("Other technique warmup"); otherHolder.SetActive(false);
        foreach (string id in new[] { "river", "ridge", "old_ferry", "gate", "quarry", "east", "camp", "rescue", "final", "entrance" })
        {
            spell.ConfigureForPlayer(id, hero, false); var samples = new List<EnemySpecial.GhostRenderSample>();
            spell.CreateGhostRenderSamples(otherHolder.transform, Vector3.zero, samples);
            Check(samples.Count == (id == "entrance" ? 1 : 0), id + " retains previous warmup allocation count");
            if (id == "entrance") Check(samples[0].Dark && samples[0].AnimationState == "WALK_FORWARD", "Existing entrance dark ghost variant retained");
            foreach (var sample in samples) sample.Dispose();
        }
        Object.Destroy(otherHolder);
        Check(PlayerPrefs.GetString(SaveKey, "") == SessionState.GetString(Active + ".Save", ""), "Campaign save unchanged");
        Check(!RedboolPvp.Instance && Object.FindObjectsByType<RedboolViewLink>(FindObjectsSortMode.None).Length == 0, "No online/view peer exists in this visual-only fixture");
    }
    static void Finish(string error)
    {
        observing = false; RenderPipelineManager.beginCameraRendering -= Render;
        SessionState.SetBool(Active, false); EditorApplication.update -= Tick;
        Pvp(false); Time.timeScale = 1; Time.fixedDeltaTime = .02f;
        if (SessionState.GetBool(Active + ".HadSave", false)) PlayerPrefs.SetString(SaveKey, SessionState.GetString(Active + ".Save", ""));
        else PlayerPrefs.DeleteKey(SaveKey);
        PlayerPrefs.Save();
        Directory.CreateDirectory(Output);
        File.WriteAllText(Path.Combine(Output, "result.json"), JsonUtility.ToJson(new Result { passed = error == null, error = error, checks = checks.ToArray() }, true));
        EditorApplication.Exit(error == null ? 0 : 1);
    }
}
