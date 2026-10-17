using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine.Playables;
using UnityEngine.Animations;

/// <summary>Rebuild shared boxing assets from the approved, versioned source clips.</summary>
public static class UnifiedBoxingInstaller
{
    public const string Folder = "Assets/Animation/Combat3D/UnifiedBoxing";
    public const string Boxer = "Assets/Boxer_Character/Boxer_Character.prefab";
    public const string Scout = "Assets/Characters/Scout/Scout_Visual.prefab";
    public static readonly string[] Keys = { "IDLE", "JAB", "CROSS", "HIGHKICK", "LOWKICK", "GUARD", "GUARD_LOWKICK", "HURT", "WALK_FORWARD", "WALK_BACK", "KO" };
    public static string Out
    {
        get { var a = Environment.GetCommandLineArgs(); int i = Array.IndexOf(a, "-unifiedReport");
            return i >= 0 ? a[i + 1] : Path.Combine(Application.temporaryCachePath, "UnifiedBoxing"); }
    }
    public static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
        AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\', '/'), Path.GetFileName(path));
    }
    static T Save<T>(T asset, string path) where T : UnityEngine.Object
    {
        var old = AssetDatabase.LoadAssetAtPath<T>(path);
        if (old != null) { EditorUtility.CopySerialized(asset, old); UnityEngine.Object.DestroyImmediate(asset); EditorUtility.SetDirty(old); return old; }
        AssetDatabase.CreateAsset(asset, path); return asset;
    }
    static void MapFingers()
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath("Assets/Characters/Scout/Scout_Animated.fbx");
        var d = importer.humanDescription; var list = d.human.ToList(); var bones = d.skeleton.Select(x => x.name).ToHashSet();
        foreach (string side in new[] { "Left", "Right" }) foreach (string finger in new[] { "Thumb", "Index", "Middle", "Ring", "Little" })
        foreach (string part in new[] { "Proximal", "Intermediate", "Distal" })
        {
            string human = side + " " + finger + " " + part, bone = side + "_" + (finger == "Little" ? "Pinky" : finger) + part;
            if (!list.Any(x => x.humanName == human) && bones.Contains(bone))
                list.Add(new HumanBone { humanName = human, boneName = bone, limit = new HumanLimit { useDefaultValues = true } });
        }
        if (list.Count != d.human.Length) { d.human = list.ToArray(); importer.humanDescription = d; importer.SaveAndReimport(); }
    }
    public static float SkinFloor(GameObject root)
    {
        float min = float.MaxValue;
        foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var mesh = new Mesh(); skin.BakeMesh(mesh);
            // BakeMesh includes scale, so do not apply lossyScale a second time.
            var matrix = Matrix4x4.TRS(skin.transform.position, skin.transform.rotation, Vector3.one);
            foreach (var v in mesh.vertices) min = Mathf.Min(min, matrix.MultiplyPoint3x4(v).y);
            UnityEngine.Object.DestroyImmediate(mesh);
        }
        return min;
    }
    static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3 - 2 * x); }
    static float AirHeight(float t)
    {
        if (t < .12f) return 0; if (t < .32f) return .16f * Smooth((t - .12f) / .20f);
        if (t < .70f) { float d = (t - .32f) / .38f; return .16f * (1 - d * d); }
        return t < .84f ? .012f * Mathf.Sin((t - .70f) / .14f * Mathf.PI) : 0;
    }
    static Dictionary<string, AnimationClip> Ground(string prefab, string subfolder)
    {
        EnsureFolder(Folder + "/" + subfolder);
        var result = new Dictionary<string, AnimationClip>();
        var root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(prefab));
        root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        foreach (var m in root.GetComponentsInChildren<MonoBehaviour>(true)) if (m != null) UnityEngine.Object.DestroyImmediate(m);
        var actor = root.GetComponentInChildren<Animator>(); actor.runtimeAnimatorController = null;
        actor.applyRootMotion = false; actor.cullingMode = AnimatorCullingMode.AlwaysAnimate; actor.Rebind();
        foreach (string key in Keys)
        {
            var source = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "/Source/Unified_" + key + ".anim");
            if (!source || !source.humanMotion) throw new Exception("Missing Humanoid clip " + key);
            var clip = UnityEngine.Object.Instantiate(source); clip.name = "Unified_" + subfolder + "_" + key;
            var graph = PlayableGraph.Create("Ground calibration"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var play = AnimationClipPlayable.Create(graph, source); play.SetApplyFootIK(false);
            AnimationPlayableOutput.Create(graph, "Pose", actor).SetSourcePlayable(play); graph.Play();
            var binding = EditorCurveBinding.FloatCurve("", typeof(Animator), "RootT.y");
            var y = AnimationUtility.GetEditorCurve(source, binding);
            int n = Mathf.RoundToInt(source.length * 60) + 1; var values = new Keyframe[n];
            for (int i = 0; i < n; i++)
            {
                float time = i * source.length / (n - 1);
                play.SetTime(Mathf.Min(time, source.length - .00001f)); graph.Evaluate(0);
                float correction = ((key == "KO" ? AirHeight(time) : 0) - SkinFloor(root)) / actor.humanScale;
                values[i] = new Keyframe(time, y.Evaluate(time) + correction);
            }
            graph.Destroy();
            if (key != "KO") values[n - 1].value = values[0].value;
            var curve = new AnimationCurve(values);
            for (int i = 0; i < n; i++) { AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto); AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto); }
            AnimationUtility.SetEditorCurve(clip, binding, curve);
            result[key] = Save(clip, Folder + "/" + subfolder + "/" + clip.name + ".anim");
        }
        UnityEngine.Object.DestroyImmediate(root);
        foreach (string key in new[] { "GUARD", "GUARD_LOWKICK" })
        {
            var clip = UnityEngine.Object.Instantiate(result[key]); clip.name += "_HOLD";
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                float value = AnimationUtility.GetEditorCurve(clip, binding).Evaluate(.27f);
                AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0, 1, value));
            }
            var settings = AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime = true; settings.startTime = 0; settings.stopTime = 1;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            result[key + "_HOLD"] = Save(clip, Folder + "/" + subfolder + "/" + clip.name + ".anim");
        }
        return result;
    }
    static void SetPrefab(string path, RuntimeAnimatorController controller)
    {
        var root = PrefabUtility.LoadPrefabContents(path);
        // Legacy model-demo controls used their own state names, IK and keyboard
        // shortcuts. They must not compete with the common combat presentation.
        var legacy = new HashSet<string> { "HanbokDemoControl", "HanbokActionFeel", "GuardWidth", "HeadLook", "PunchAim", "FighterStance", "KickGuard", "HitKnockback" };
        foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            if (behaviour != null && legacy.Contains(behaviour.GetType().Name)) behaviour.enabled = false;
        var animator = root.GetComponentInChildren<Animator>(true);
        animator.runtimeAnimatorController = controller; animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        PrefabUtility.SaveAsPrefabAsset(root, path); PrefabUtility.UnloadPrefabContents(root);
    }
    public static void Verify()
    {
        SetPrefab(Boxer, AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(Folder + "/Unified_Combat.controller"));
        SetPrefab(Scout, AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(Folder + "/Unified_Scout.overrideController"));
        AssetDatabase.SaveAssets();
        UnifiedBoxingVerification.Run();
    }
    public static void Apply()
    {
        try
        {
            Directory.CreateDirectory(Out);
            MapFingers(); EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var normal = Ground(Boxer, "Common"); var scout = Ground(Scout, "ScoutContact");
            string path = Folder + "/Unified_Combat.controller";
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path) ?? AnimatorController.CreateAnimatorControllerAtPath(path);
            var sm = controller.layers[0].stateMachine;
            foreach (var old in sm.states) sm.RemoveState(old.state);
            foreach (var pair in normal)
            {
                var state = sm.AddState(pair.Key); state.motion = pair.Value; state.iKOnFeet = false; state.writeDefaultValues = false;
                if (pair.Key == "IDLE") sm.defaultState = state;
            }
            foreach (string key in new[] { "GUARD", "GUARD_LOWKICK" }) foreach (string suffix in new[] { "_HIT", "_RELEASE" })
            { var state = sm.AddState(key + suffix); state.motion = normal[key]; state.writeDefaultValues = false; }
            EditorUtility.SetDirty(controller);
            var overrides = new AnimatorOverrideController(controller) { name = "Unified_Scout" };
            overrides.ApplyOverrides(normal.Select(pair => new KeyValuePair<AnimationClip, AnimationClip>(pair.Value, scout[pair.Key])).ToList());
            var saved = Save(overrides, Folder + "/Unified_Scout.overrideController");
            SetPrefab(Boxer, controller); SetPrefab(Scout, saved);
            AssetDatabase.SaveAssets();
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/GameScene.unity");
            var report = new List<string>();
            foreach (var view in UnityEngine.Object.FindObjectsByType<Fighter3DView>(FindObjectsInactive.Include))
            {
                view.modelAnimator.runtimeAnimatorController = controller; view.modelAnimator.applyRootMotion = false;
                view.strikeClips = new[] { normal["JAB"], normal["CROSS"], normal["HIGHKICK"], normal["LOWKICK"] };
                view.guardClips = new[] { normal["GUARD"], normal["GUARD_LOWKICK"] };
                var collider = view.GetComponent<BoxCollider2D>();
                var origin = view.visualRoot.localPosition;
                origin.y = collider.offset.y - collider.size.y * .5f + .005f;
                view.visualRoot.localPosition = origin;
                PrefabUtility.RecordPrefabInstancePropertyModifications(view.modelAnimator);
                PrefabUtility.RecordPrefabInstancePropertyModifications(view.visualRoot);
                EditorUtility.SetDirty(view);
                report.Add(view.name + ": shared controller; originY=" + origin.y + "; scale=" + view.visualRoot.lossyScale);
            }
            // The approved KO example uses a small zoom, not the previous 2x zoom.
            foreach (var impact in UnityEngine.Object.FindObjectsByType<HitImpact>())
            { var so = new SerializedObject(impact); so.FindProperty("zoomRatio").floatValue = .935f; so.ApplyModifiedPropertiesWithoutUndo(); }
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            report.Add("11 source motions; 2 guards, each with hold/hit/release handling. Scout overrides differ only in floor contact.");
            File.WriteAllLines(Path.Combine(Out, "integration.txt"), report);
            File.WriteAllText(Path.Combine(Out, "apply_done.txt"), "Applied");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { File.WriteAllText(Path.Combine(Out, "apply_error.txt"), e.ToString()); Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
