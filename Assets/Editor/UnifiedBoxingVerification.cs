using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Play-mode integration check using GameScene actors and the actual runtime code.</summary>
[InitializeOnLoad]
public static class UnifiedBoxingVerification
{
    const string Flag = "UnifiedBoxingVerification.Running";
    static IEnumerator flow;
    static int lastFrame = -1, captured;
    static List<string> results = new List<string>();
    static List<string> errors = new List<string>();
    static Fighter3DView[] views;
    static Fighter[] fighters;
    static Health[] health;
    static Rigidbody2D[] bodies;
    static Camera camera;
    static RenderTexture rt;
    static Texture2D image;
    static bool recording, hitTests;
    static string label;
    static StreamWriter frames;
    static string Out => UnifiedBoxingInstaller.Out;
    static UnifiedBoxingVerification()
    {
        if (SessionState.GetBool(Flag, false)) { EditorApplication.update += Tick; Application.logMessageReceived += Log; }
    }
    public static void Run()
    {
        Directory.CreateDirectory(Out);
        EditorSceneManager.OpenScene("Assets/Scenes/GameScene.unity");
        SessionState.SetBool(Flag, true);
        // This project disables domain reload on entering Play mode.
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
        Application.logMessageReceived -= Log; Application.logMessageReceived += Log;
        EditorApplication.isPaused = false;
        EditorApplication.delayCall += () => EditorApplication.EnterPlaymode();
    }
    static void Log(string text, string stack, LogType type)
    {
        if ((type == LogType.Exception || type == LogType.Error) && !stack.Contains("IvanMurzak") && !text.Contains("MCP")) errors.Add(text);
    }
    static void Check(bool ok, string name)
    {
        results.Add((ok ? "PASS " : "FAIL ") + name);
        File.WriteAllLines(Path.Combine(Out, "checks.txt"), results);
        if (!ok) throw new Exception(name);
    }
    static void Tick()
    {
        if (!EditorApplication.isPlaying || !Application.isPlaying || EditorApplication.isCompiling) return;
        EditorApplication.isPaused = false; Application.runInBackground = true;
        EditorApplication.QueuePlayerLoopUpdate();
        if (Time.frameCount == lastFrame) return;
        lastFrame = Time.frameCount;
        try
        {
            if (flow == null) flow = Tests();
            bool next = flow.MoveNext();
            if (recording && Time.frameCount % 2 == 0) Capture();
            if (!next) Finish();
        }
        catch (Exception e) { errors.Add(e.ToString()); Finish(); }
    }
    static void Finish()
    {
        recording = false; frames?.Dispose(); frames = null;
        File.WriteAllLines(Path.Combine(Out, "runtime_errors.txt"), errors);
        File.WriteAllText(Path.Combine(Out, "verification_done.txt"), errors.Count == 0 ? "PASS" : "FAIL");
        SessionState.SetBool(Flag, false); EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
        EditorApplication.Exit(errors.Count == 0 ? 0 : 1);
    }
    static IEnumerator Wait(float seconds)
    {
        float end = Time.time + seconds;
        while (Time.time < end) yield return null;
    }
    static bool State(string name) => views.All(v => v.CurrentState == name);
    static void ResetActors()
    {
        foreach (var f in fighters) { f.InputLocked = false; f.IsBlocking = f.IsCrouching = false; f.CancelAttack(); }
        foreach (var h in health) { h.DamageLocked = false; h.ResetHealth(); }
        foreach (var rb in bodies) rb.linearVelocity = Vector2.zero;
        if (hitTests) { bodies[0].position = new Vector2(-.55f, 0); bodies[1].position = new Vector2(.55f, 0); Physics2D.SyncTransforms(); }
        foreach (var v in views) v.ResetPresentation();
    }
    static IEnumerator Tests()
    {
        yield return null;
        var player = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
        var enemy = UnityEngine.Object.FindAnyObjectByType<EnemyAI>();
        views = new[] { player.GetComponent<Fighter3DView>(), enemy.GetComponent<Fighter3DView>() };
        fighters = views.Select(v => v.GetComponent<Fighter>()).ToArray();
        health = views.Select(v => v.GetComponent<Health>()).ToArray();
        bodies = views.Select(v => v.GetComponent<Rigidbody2D>()).ToArray();
        Check(views.All(v => v.modelAnimator.avatar.isHuman && v.modelAnimator.avatar.isValid), "GameScene ordinary avatars valid");
        Check(views[0].modelAnimator.runtimeAnimatorController == views[1].modelAnimator.runtimeAnimatorController, "Ordinary player/enemy share controller");
        Check(views.All(v => !v.visualRoot.GetComponentsInChildren<MonoBehaviour>().Any(m => m != null && m.enabled)), "Legacy model demo/IK/key handlers are inactive");
        player.enabled = enemy.enabled = false;
        // These presenters subscribe to death even while disabled; remove only in
        // this unsaved test session so a first KO doesn't lock the other subject.
        foreach (var c in UnityEngine.Object.FindObjectsByType<BattleSceneBridge>()) UnityEngine.Object.Destroy(c);
        foreach (var c in UnityEngine.Object.FindObjectsByType<CombatHUD>()) UnityEngine.Object.Destroy(c);
        foreach (var c in UnityEngine.Object.FindObjectsByType<UdpActionReceiver>()) c.enabled = false;
        foreach (var c in UnityEngine.Object.FindObjectsByType<Pushbox>()) c.enabled = false;
        foreach (var c in UnityEngine.Object.FindObjectsByType<Camera>()) c.enabled = false;
        foreach (var h in health) h.Configure("Test", 10000);
        foreach (var f in fighters) { var so = new SerializedObject(f); so.FindProperty("useHitImpact").boolValue = false; so.ApplyModifiedPropertiesWithoutUndo(); }
        Check(views[1].ApplyVisual(AssetDatabase.LoadAssetAtPath<GameObject>(UnifiedBoxingInstaller.Scout)), "Scout stage visual replacement succeeds");
        var overrides = views[1].modelAnimator.runtimeAnimatorController as AnimatorOverrideController;
        Check(overrides != null && overrides.runtimeAnimatorController == views[0].modelAnimator.runtimeAnimatorController, "Scout uses the same state machine");
        Check(views[1].modelAnimator.GetBoneTransform(HumanBodyBones.RightIndexProximal) != null, "Scout finger mapping available");
        yield return null;
        for (int p = 0; p < 2; p++)
        {
            bodies[p].simulated = false; fighters[p].Facing = 1;
            views[p].transform.position = new Vector3(p * 10, 0, 0);
            foreach (var t in views[p].visualRoot.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 10 + p;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.layer = 10 + p;
            floor.transform.position = new Vector3(p * 10, -.035f, 0); floor.transform.localScale = new Vector3(8, .06f, 5);
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")); material.color = new Color(.10f, .13f, .17f); floor.GetComponent<Renderer>().sharedMaterial = material;
        }
        camera = new GameObject("Integration review camera").AddComponent<Camera>(); camera.enabled = false;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.045f, .06f, .085f);
        camera.orthographic = true; camera.orthographicSize = 1.60f;
        rt = new RenderTexture(640, 640, 24); camera.targetTexture = rt; image = new Texture2D(1280, 640, TextureFormat.RGB24, false);
        var light = new GameObject("Review light").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.3f; light.transform.rotation = Quaternion.Euler(35, -25, 0);
        Time.timeScale = 1; Time.captureDeltaTime = 1f / 60;
        Directory.CreateDirectory(Path.Combine(Out, "frames")); frames = new StreamWriter(Path.Combine(Out, "frames.csv")); frames.WriteLine("frame,label,boxerState,scoutState");
        ResetActors(); label = "IDLE"; recording = true;
        for (var w = Wait(1); w.MoveNext();) yield return null;
        Check(State("IDLE"), "Idle on both avatars");
        foreach (CombatMove move in Enum.GetValues(typeof(CombatMove)))
        {
            label = move.ToString().ToUpperInvariant();
            foreach (var f in fighters) Check(f.TryAttack(move), f.name + " starts " + move);
            for (var w = Wait(Fighter3DView.Duration(move) + .22f); w.MoveNext();) yield return null;
            Check(State("IDLE") && fighters.All(f => !f.IsAttacking), move + " completes and returns to common idle");
            Check(views.All(v => !v.modelAnimator.IsInTransition(0)), move + " idle blend completes");
            for (var w = Wait(.5f); w.MoveNext();) yield return null;
        }
        foreach (bool low in new[] { false, true })
        {
            string pose = low ? "GUARD_LOWKICK" : "GUARD"; label = pose;
            foreach (var f in fighters) { f.IsBlocking = true; f.IsCrouching = low; }
            for (var w = Wait(1.1f); w.MoveNext();) yield return null;
            Check(State(pose + "_HOLD"), pose + " remains held beyond source clip end");
            Check(views.All(v => v.modelAnimator.speed == 1 && !v.modelAnimator.IsInTransition(0)), pose + " holds without freezing transitions");
            foreach (var h in health) Check(h.TakeDamage(1, Vector2.zero, true), "Guard chip damage/reaction");
            yield return null; Check(State(pose + "_HIT"), pose + " reacts to hit");
            for (var w = Wait(.5f); w.MoveNext();) yield return null;
            Check(State(pose + "_HOLD"), pose + " returns to held guard");
            foreach (var f in fighters) f.IsBlocking = f.IsCrouching = false;
            for (var w = Wait(.6f); w.MoveNext();) yield return null;
            Check(State("IDLE"), pose + " releases to idle");
        }
        label = "HURT";
        foreach (var h in health) h.TakeDamage(1, Vector2.zero);
        for (var w = Wait(.4f); w.MoveNext();) yield return null;
        Check(State("HURT") && health.All(h => h.IsHitStunned), "Slower hurt is not cut at the old 0.3s stun");
        for (var w = Wait(.5f); w.MoveNext();) yield return null;
        Check(State("IDLE"), "Hurt recovers to idle");
        foreach (int dir in new[] { 1, -1 })
        {
            label = dir > 0 ? "WALK_FORWARD" : "WALK_BACK";
            foreach (var rb in bodies) rb.linearVelocity = Vector2.right * dir;
            // Locomotion now reads actual displacement, including wall blockage.
            // This presentation-only fixture has physics disabled, so move its roots.
            float walkUntil = Time.time + 2.6f;
            while (Time.time < walkUntil)
            {
                foreach (var v in views) v.transform.position += Vector3.right * dir * Time.deltaTime;
                yield return null;
            }
            Check(State(label), label + " loops on both avatars; no run state");
            foreach (var rb in bodies) rb.linearVelocity = Vector2.zero;
            for (var w = Wait(.5f); w.MoveNext();) yield return null;
            Check(State("IDLE"), label + " stops smoothly");
        }
        label = "KO";
        foreach (var h in health) h.TakeDamage(h.CurrentHealth, Vector2.zero);
        for (var w = Wait(2.3f); w.MoveNext();) yield return null;
        Check(State("KO") && health.All(h => h.IsDead), "KO holds fallen pose");
        Check(views.All(v => v.modelAnimator.GetBoneTransform(HumanBodyBones.Head).position.y < .55f), "KO bodies settle on ground");
        recording = false; frames.Flush();
        ResetActors(); for (var w = Wait(.2f); w.MoveNext();) yield return null;
        Check(State("IDLE"), "Round reset restores idle instead of replaying KO");

        // Actual hit queries and interruptions, using the real GameScene colliders.
        for (int p = 0; p < 2; p++) { bodies[p].simulated = true; bodies[p].bodyType = RigidbodyType2D.Kinematic; }
        hitTests = true;
        fighters[0].Facing = 1; fighters[1].Facing = -1;
        bodies[0].position = new Vector2(-.55f, 0); bodies[1].position = new Vector2(.55f, 0); Physics2D.SyncTransforms();
        for (var w = Wait(.1f); w.MoveNext();) yield return null;
        foreach (CombatMove move in Enum.GetValues(typeof(CombatMove)))
        {
            ResetActors(); int before = health[1].CurrentHealth; float begin = Time.time;
            Check(fighters[0].TryAttack(move), "Live hit starts " + move);
            while (Time.time < begin + Fighter3DView.Duration(move) + .2f && health[1].CurrentHealth == before) yield return null;
            float impact = Time.time - begin;
            Check(health[1].CurrentHealth < before, move + " hits through real overlap box");
            Check(Mathf.Abs(impact - Fighter3DView.Impact(move)) < .075f, move + " damage matches contact time: " + impact.ToString("F3"));
            for (var w = Wait(Fighter3DView.Duration(move) + .3f); w.MoveNext();) yield return null;
        }
        foreach (CombatMove move in Enum.GetValues(typeof(CombatMove)))
        {
            ResetActors(); int before = health[0].CurrentHealth;
            Check(fighters[1].TryAttack(move), "Scout starts live " + move);
            for (var w = Wait(Fighter3DView.Impact(move) + .15f); w.MoveNext();) yield return null;
            Check(health[0].CurrentHealth < before, "Scout deals damage with " + move);
        }
        ResetActors(); fighters[1].IsBlocking = true;
        int guardHp = health[1].CurrentHealth; fighters[0].TryAttack(CombatMove.Straight);
        for (var w = Wait(.55f); w.MoveNext();) yield return null;
        Check(guardHp - health[1].CurrentHealth == fighters[1].GuardedDamage(fighters[0].AttackDamage), "Basic guard reduces punch damage");
        ResetActors(); fighters[1].IsBlocking = true; fighters[1].IsCrouching = true;
        int hp = health[1].CurrentHealth; fighters[0].TryAttack(CombatMove.LowKick);
        for (var w = Wait(.6f); w.MoveNext();) yield return null;
        Check(hp - health[1].CurrentHealth == fighters[1].GuardedDamage(fighters[0].AttackDamage), "Low guard reduces actual low-kick damage");
        ResetActors(); hp = health[1].CurrentHealth; fighters[0].TryAttack(CombatMove.HighKick);
        for (var w = Wait(.1f); w.MoveNext();) yield return null;
        health[0].TakeDamage(1, Vector2.zero);
        for (var w = Wait(1.3f); w.MoveNext();) yield return null;
        Check(health[1].CurrentHealth == hp && !fighters[0].IsAttacking, "Interrupted attack cannot deal a delayed hit");
        ResetActors(); player.ExternalAction("JUMP"); player.ExternalAction("CROUCH");
        Check(!fighters[0].IsCrouching && bodies[0].linearVelocity.y == 0, "Removed jump/crouch inputs are ignored in 3D");
        player.ExternalAction("CROUCH_BLOCK"); Check(fighters[0].IsBlocking && fighters[0].IsCrouching, "Existing vision low-guard label remains compatible");
        player.ExternalAction("IDLE"); player.ExternalAction("JAB");
        Check(fighters[0].IsAttacking && fighters[0].CurrentMove == CombatMove.Jab, "Vision jab routes into shared combat");
        Check(errors.Count == 0, "No runtime exceptions or missing animation states");
    }
    static void Capture()
    {
        for (int p = 0; p < 2; p++)
        {
            var center = views[p].transform.position + new Vector3(label == "KO" ? -.42f : .25f, .94f, 0);
            camera.transform.position = center + Vector3.back * 6; camera.transform.LookAt(center);
            camera.cullingMask = 1 << (10 + p); camera.Render(); RenderTexture.active = rt;
            image.ReadPixels(new Rect(0, 0, 640, 640), p * 640, 0);
        }
        image.Apply(); File.WriteAllBytes(Path.Combine(Out, "frames", captured.ToString("D5") + ".jpg"), image.EncodeToJPG(88));
        frames.WriteLine(captured + "," + label + "," + views[0].CurrentState + "," + views[1].CurrentState); captured++; RenderTexture.active = null;
    }
}
