using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>
/// Authors the 파운장(破雲掌) Humanoid clips on the player rig and adds them to Unified_Combat.
///   SPECIAL_CHARGE : looping taiji "hold the ball" (抱球) at the right hip, knees bent, torso coiled.
///   SPECIAL_RELEASE: anticipation → two-palm thrust (雙掌) at chest height → follow-through → back to guard.
/// Key poses are built from the current IDLE stance with two-bone IK (feet stay planted), then converted to
/// muscle space with HumanPoseHandler so every Humanoid can play them.
/// Run: Unity -batchmode -executeMethod QiPalmAnimationBaker.Bake [-qiReport &lt;folder&gt;]
/// </summary>
public static class QiPalmAnimationBaker
{
    public const string Folder = UnifiedBoxingInstaller.Folder + "/Special";
    public const string ChargePath = Folder + "/Unified_Common_SPECIAL_CHARGE.anim";
    public const string ReleasePath = Folder + "/Unified_Common_SPECIAL_RELEASE.anim";
    public const string ChargeState = QiPalmTiming.ChargeState, ReleaseState = QiPalmTiming.ReleaseState;
    /// <summary>Seconds into SPECIAL_RELEASE when the palms reach full extension (the wave leaves the hands).</summary>
    public const float ReleaseImpact = QiPalmTiming.ReleaseImpact;
    public const float ReleaseLength = QiPalmTiming.ReleaseLength;
    const float ChargeLoop = 1.2f;

    static string Report { get { var a = Environment.GetCommandLineArgs(); int i = Array.IndexOf(a, "-qiReport"); return i >= 0 && i + 1 < a.Length ? a[i + 1] : Path.Combine(Application.temporaryCachePath, "QiPalm"); } }
    static readonly List<string> log = new List<string>();
    static void L(string s) { log.Add(s); Debug.Log("[QiPalm] " + s); }

    // ───────────── rig helpers ─────────────
    sealed class Rig
    {
        public Animator anim; public Transform T(HumanBodyBones b) => anim.GetBoneTransform(b);
        public float h; public HumanPoseHandler handler; public HumanPose basePose;
        public Vector3 footL, footR; public Quaternion footLRot, footRRot;
        public float armLen;
    }
    static readonly Vector3 Up = Vector3.up, Fwd = Vector3.forward, Right = Vector3.right;

    static void TwoBoneIK(Transform a, Transform b, Transform c, Vector3 target, Vector3 hint)
    {
        Vector3 ap = a.position, bp = b.position, cp = c.position;
        float lab = (bp - ap).magnitude, lcb = (cp - bp).magnitude;
        float lat = Mathf.Clamp((target - ap).magnitude, 1e-4f, lab + lcb - 1e-3f);
        float acab0 = Angle(cp - ap, bp - ap), babc0 = Angle(ap - bp, cp - bp), acat0 = Angle(cp - ap, target - ap);
        float acab1 = Mathf.Acos(Mathf.Clamp((lcb * lcb - lab * lab - lat * lat) / (-2f * lab * lat), -1f, 1f));
        float babc1 = Mathf.Acos(Mathf.Clamp((lat * lat - lab * lab - lcb * lcb) / (-2f * lab * lcb), -1f, 1f));
        Vector3 axis0 = Vector3.Cross(cp - ap, bp - ap); if (axis0.sqrMagnitude < 1e-8f) axis0 = Vector3.Cross(cp - ap, hint); axis0.Normalize();
        Vector3 axis1 = Vector3.Cross(cp - ap, target - ap); if (axis1.sqrMagnitude < 1e-8f) axis1 = axis0; axis1.Normalize();
        b.rotation = Quaternion.AngleAxis((babc1 - babc0) * Mathf.Rad2Deg, axis0) * b.rotation;
        a.rotation = Quaternion.AngleAxis((acab1 - acab0) * Mathf.Rad2Deg, axis0) * a.rotation;
        a.rotation = Quaternion.AngleAxis(acat0 * Mathf.Rad2Deg, axis1) * a.rotation;
        // swing the chain around the shoulder→target axis so the middle joint points toward the hint
        Vector3 axis = (c.position - a.position).normalized;
        Vector3 mid = Vector3.ProjectOnPlane(b.position - a.position, axis), want = Vector3.ProjectOnPlane(hint, axis);
        if (mid.sqrMagnitude > 1e-8f && want.sqrMagnitude > 1e-8f)
            a.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(mid, want, axis), axis) * a.rotation;
    }
    static float Angle(Vector3 x, Vector3 y) => Mathf.Acos(Mathf.Clamp(Vector3.Dot(x.normalized, y.normalized), -1f, 1f));

    /// <summary>Turn the shoulder line to an absolute yaw (degrees, + turns the chest toward the character's right).</summary>
    static void TorsoYaw(Rig r, float yaw, float lean)
    {
        var bones = new[] { HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest }.Select(r.T).Where(t => t).ToArray();
        float[] share = bones.Length == 3 ? new[] { .4f, .35f, .25f } : new[] { .55f, .45f };
        Vector3 line = r.T(HumanBodyBones.RightUpperArm).position - r.T(HumanBodyBones.LeftUpperArm).position; line.y = 0;
        float current = Vector3.SignedAngle(Right, line, Up);
        // Rotating the chest by +θ about up turns its forward toward +X and the shoulder line by the same +θ.
        float delta = yaw - current;
        for (int i = 0; i < bones.Length; i++) bones[i].rotation = Quaternion.AngleAxis(delta * share[i], Up) * bones[i].rotation;
        for (int i = 0; i < bones.Length; i++) bones[i].rotation = Quaternion.AngleAxis(lean * share[i], Right) * bones[i].rotation;
    }
    static void FaceForward(Rig r)
    {
        foreach (var b in new[] { HumanBodyBones.Neck, HumanBodyBones.Head })
        {
            var t = r.T(b); if (!t) continue;
            Vector3 f = r.T(HumanBodyBones.Head).forward; // Mixamo heads look along +Z in bind; measure via eyes if present
            var le = r.T(HumanBodyBones.LeftEye); var re = r.T(HumanBodyBones.RightEye);
            Vector3 across = (le && re) ? re.position - le.position : r.T(HumanBodyBones.RightUpperArm).position - r.T(HumanBodyBones.LeftUpperArm).position;
            across.y = 0; float yaw = Vector3.SignedAngle(Right, across, Up);
            t.rotation = Quaternion.AngleAxis(-yaw * (b == HumanBodyBones.Neck ? .5f : 1f), Up) * t.rotation;
        }
    }
    static void PlantFeet(Rig r)
    {
        TwoBoneIK(r.T(HumanBodyBones.LeftUpperLeg), r.T(HumanBodyBones.LeftLowerLeg), r.T(HumanBodyBones.LeftFoot), r.footL, Fwd + Right * -.15f);
        TwoBoneIK(r.T(HumanBodyBones.RightUpperLeg), r.T(HumanBodyBones.RightLowerLeg), r.T(HumanBodyBones.RightFoot), r.footR, Fwd + Right * .15f);
        r.T(HumanBodyBones.LeftFoot).rotation = r.footLRot; r.T(HumanBodyBones.RightFoot).rotation = r.footRRot;
    }
    static void Hips(Rig r, float lower, Vector3 shift, float yaw)
    {
        var hips = r.T(HumanBodyBones.Hips);
        hips.position += Up * -lower + shift;
        hips.rotation = Quaternion.AngleAxis(yaw, Up) * hips.rotation;
        PlantFeet(r);
    }
    static void Arm(Rig r, bool left, Vector3 target, Vector3 elbowHint)
    {
        TwoBoneIK(r.T(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm), r.T(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm),
            r.T(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand), target, elbowHint);
    }

    // ───────────── key poses ─────────────
    // Hand orientation is set in muscle space (forearm twist, wrist down-up / in-out) so it works for gloved rigs
    // without finger bones as well as for opponents with full hands.
    struct Pose { public float lower; public Vector3 shift; public float hipYaw, torsoYaw, lean; public Vector3 handR, handL, elbowR, elbowL; public float twistR, twistL, wristUpR, wristUpL, wristInR, wristInL; public float stretch, spread; }

    static Pose Charge(Rig r, float roll, float bob)
    {
        float h = r.h;
        var hips = r.T(HumanBodyBones.Hips).position;
        // the ball sits in front of the right hip; the coil turns the chest to the right
        Vector3 ball = hips + Right * (.13f * h) + Fwd * (.10f * h) + Up * (.015f * h + bob);
        Quaternion rot = Quaternion.AngleAxis(roll, Right);
        return new Pose
        {
            lower = .10f * h, shift = Fwd * (-.015f * h), hipYaw = 10f, torsoYaw = 54f, lean = 2f,
            handR = ball + rot * (Up * (.10f * h) + Right * (.01f * h)), handL = ball + rot * (Up * (-.075f * h) + Fwd * (.015f * h)),
            elbowR = Right * 1f + Fwd * -.7f + Up * -.2f, elbowL = Up * -1f + Right * -.25f + Fwd * .35f,
            // top (right) palm turned down over the ball, bottom (left) palm turned up under it
            twistR = TwistPalmDown, twistL = -TwistPalmDown, wristUpR = -.15f, wristUpL = .1f, wristInR = .2f, wristInL = .2f,
            stretch = .35f, spread = .25f,
        };
    }
    static Pose Thrust(Rig r, float reach, float torsoYaw, float lean, float lunge)
    {
        float h = r.h;
        var sl = r.T(HumanBodyBones.LeftUpperArm).position; var sr = r.T(HumanBodyBones.RightUpperArm).position;
        Vector3 center = (sl + sr) * .5f + Fwd * (r.armLen * reach) + Up * (-.045f * h) + Fwd * lunge;
        return new Pose
        {
            lower = .085f * h, shift = Fwd * lunge, hipYaw = -24f, torsoYaw = torsoYaw, lean = lean,
            handR = center + Right * (.032f * h) + Up * (.012f * h), handL = center + Right * (-.032f * h) + Up * (-.006f * h),
            elbowR = Right * .55f + Up * -1f, elbowL = Right * -.55f + Up * -1f,
            // palms pronated then wrists bent back: heels of both palms forward, fingers up and slightly outward
            twistR = TwistPalmDown, twistL = TwistPalmDown, wristUpR = WristBack, wristUpL = WristBack, wristInR = -.35f, wristInL = -.35f,
            stretch = .85f, spread = .45f,
        };
    }

    static HumanPose Build(Rig r, Func<Rig, Pose> make)
    {
        r.handler.SetHumanPose(ref r.basePose);
        var p0 = make(r); // body settings (stance, coil) from the base stance
        Hips(r, p0.lower, p0.shift, p0.hipYaw);
        TorsoYaw(r, p0.torsoYaw, p0.lean);
        FaceForward(r);
        var p = make(r); // hand targets measured on the lowered, coiled body
        Arm(r, false, p.handR, p.elbowR);
        Arm(r, true, p.handL, p.elbowL);
        var pose = new HumanPose();
        r.handler.GetHumanPose(ref pose);
        Set(ref pose, "Right Forearm Twist In-Out", p.twistR); Set(ref pose, "Left Forearm Twist In-Out", p.twistL);
        Set(ref pose, "Right Hand Down-Up", p.wristUpR); Set(ref pose, "Left Hand Down-Up", p.wristUpL);
        Set(ref pose, "Right Hand In-Out", p.wristInR); Set(ref pose, "Left Hand In-Out", p.wristInL);
        for (int i = 0; i < HumanTrait.MuscleCount; i++)
        {
            string n = HumanTrait.MuscleName[i];
            bool finger = n.Contains("Index") || n.Contains("Middle") || n.Contains("Ring") || n.Contains("Little") || n.Contains("Thumb");
            if (!finger) continue;
            if (n.Contains("Stretched")) pose.muscles[i] = n.Contains("Thumb") ? Mathf.Lerp(.1f, .5f, p0.stretch) : p0.stretch;
            else if (n.Contains("Spread")) pose.muscles[i] = n.Contains("Thumb") ? .35f : p0.spread * (n.Contains("Index") || n.Contains("Little") ? 1f : .4f);
        }
        return pose;
    }

    // Muscle-space hand settings (tuned against preview renders of the gloved player rig).
    const float TwistPalmDown = .75f, WristBack = .85f;
    static readonly Dictionary<string, int> muscleIndex = Enumerable.Range(0, HumanTrait.MuscleCount).ToDictionary(i => HumanTrait.MuscleName[i]);
    static void Set(ref HumanPose pose, string muscle, float value) { if (muscleIndex.TryGetValue(muscle, out int i)) pose.muscles[i] = Mathf.Clamp(value, -1f, 1f); }

    static AnimationClipSettings template;
    static AnimationClip MakeClip(string name, List<(float t, HumanPose pose)> keys, bool loop)
    {
        var clip = new AnimationClip { name = name, frameRate = 60 };
        void Curve(string attr, Func<HumanPose, float> v)
        {
            var c = new AnimationCurve(keys.Select(k => new Keyframe(k.t, v(k.pose))).ToArray());
            for (int i = 0; i < c.length; i++) { AnimationUtility.SetKeyLeftTangentMode(c, i, AnimationUtility.TangentMode.ClampedAuto); AnimationUtility.SetKeyRightTangentMode(c, i, AnimationUtility.TangentMode.ClampedAuto); }
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), attr), c);
        }
        Curve("RootT.x", p => p.bodyPosition.x); Curve("RootT.y", p => p.bodyPosition.y); Curve("RootT.z", p => p.bodyPosition.z);
        Curve("RootQ.x", p => p.bodyRotation.x); Curve("RootQ.y", p => p.bodyRotation.y); Curve("RootQ.z", p => p.bodyRotation.z); Curve("RootQ.w", p => p.bodyRotation.w);
        for (int i = 0; i < HumanTrait.MuscleCount; i++) { int m = i; Curve(HumanTrait.MuscleName[m], p => p.muscles[m]); }
        // Same root handling as the approved IDLE (body height/offsets stay in the pose); only looping differs.
        var s = template ?? AnimationUtility.GetAnimationClipSettings(clip);
        s.loopTime = loop; s.stopTime = keys[keys.Count - 1].t; s.startTime = 0;
        AnimationUtility.SetAnimationClipSettings(clip, s);
        return clip;
    }
    static T Save<T>(T asset, string path) where T : Object
    {
        var old = AssetDatabase.LoadAssetAtPath<T>(path);
        if (old) { EditorUtility.CopySerialized(asset, old); Object.DestroyImmediate(asset); EditorUtility.SetDirty(old); return old; }
        AssetDatabase.CreateAsset(asset, path); return asset;
    }

    public static void Bake()
    {
        Directory.CreateDirectory(Report);
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            UnifiedBoxingInstaller.EnsureFolder(Folder);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(UnifiedBoxingInstaller.Boxer));
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            foreach (var mb in model.GetComponentsInChildren<MonoBehaviour>(true)) mb.enabled = false;
            model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var anim = model.GetComponentsInChildren<Animator>(true).First(a => a.avatar && a.avatar.isHuman);
            anim.runtimeAnimatorController = null; anim.applyRootMotion = false; anim.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); anim.Rebind();
            var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(UnifiedBoxingInstaller.Folder + "/Common/Unified_Common_IDLE.anim");
            template = AnimationUtility.GetAnimationClipSettings(idle);
            var graph = PlayableGraph.Create("QiPalm base"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var play = AnimationClipPlayable.Create(graph, idle); play.SetApplyFootIK(false);
            AnimationPlayableOutput.Create(graph, "out", anim).SetSourcePlayable(play); graph.Play(); play.SetTime(0); graph.Evaluate(0); graph.Destroy();

            var r = new Rig { anim = anim, handler = new HumanPoseHandler(anim.avatar, anim.transform) };
            r.handler.GetHumanPose(ref r.basePose);
            r.h = r.T(HumanBodyBones.Head).position.y + .12f;
            r.footL = r.T(HumanBodyBones.LeftFoot).position; r.footR = r.T(HumanBodyBones.RightFoot).position;
            r.footLRot = r.T(HumanBodyBones.LeftFoot).rotation; r.footRRot = r.T(HumanBodyBones.RightFoot).rotation;
            r.armLen = (r.T(HumanBodyBones.RightLowerArm).position - r.T(HumanBodyBones.RightUpperArm).position).magnitude + (r.T(HumanBodyBones.RightHand).position - r.T(HumanBodyBones.RightLowerArm).position).magnitude;
            var rootY = AnimationUtility.GetEditorCurve(idle, EditorCurveBinding.FloatCurve("", typeof(Animator), "RootT.y"));
            L($"height={r.h:F3} arm={r.armLen:F3} humanScale={anim.humanScale:F3} base bodyPos={r.basePose.bodyPosition} clip RootT.y(0)={rootY.Evaluate(0):F4}");
            Vector3 sline = r.T(HumanBodyBones.RightUpperArm).position - r.T(HumanBodyBones.LeftUpperArm).position; sline.y = 0;
            L($"base shoulder yaw={Vector3.SignedAngle(Right, sline, Up):F1} hips={r.T(HumanBodyBones.Hips).position} footL={r.footL} footR={r.footR} handL={r.T(HumanBodyBones.LeftHand).position} handR={r.T(HumanBodyBones.RightHand).position}");
            L("finger bones mapped: " + (r.T(HumanBodyBones.RightIndexProximal) != null));

            // charge loop: ball rolls ±12° front/back and bobs, closing exactly on the first frame
            var charge = new List<(float, HumanPose)>();
            float[] rolls = { 0, 10, 0, -10, 0 }, bobs = { 0, .006f, 0, -.006f, 0 };
            for (int k = 0; k < rolls.Length; k++) { float roll = rolls[k], bob = bobs[k] * r.h; charge.Add((ChargeLoop * k / (rolls.Length - 1), Build(r, x => Charge(x, roll, bob)))); }
            var chargeClip = Save(MakeClip("Unified_Common_SPECIAL_CHARGE", charge, true), ChargePath);

            var release = new List<(float, HumanPose)>
            {
                (0f, Build(r, x => Charge(x, 0, 0))),
                (.07f, Build(r, x => { var p = Charge(x, -8, -.004f * x.h); p.torsoYaw = 64; p.lower = .11f * x.h; p.handR += Fwd * (-.035f * x.h); p.handL += Fwd * (-.035f * x.h); return p; })),
                (ReleaseImpact, Build(r, x => Thrust(x, .92f, -4f, 9f, .07f * x.h))),
                (.30f, Build(r, x => Thrust(x, .97f, -9f, 12f, .08f * x.h))),
                (.46f, Build(r, x => Thrust(x, .92f, -6f, 9f, .075f * x.h))),
                (ReleaseLength, r.basePose),
            };
            var releaseClip = Save(MakeClip("Unified_Common_SPECIAL_RELEASE", release, false), ReleasePath);
            r.handler.Dispose();
            Object.DestroyImmediate(model);

            // states in the shared combat state machine (played by name through Fighter3DView)
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(UnifiedBoxingInstaller.Folder + "/Unified_Combat.controller");
            var sm = ctrl.layers[0].stateMachine; var idleState = sm.states.First(s => s.state.name == "IDLE").state;
            foreach (var (name, clip, offset) in new[] { (ChargeState, chargeClip, new Vector3(0, 520)), (ReleaseState, releaseClip, new Vector3(260, 520)) })
            {
                var st = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == name) ?? sm.AddState(name, offset);
                st.motion = clip; st.writeDefaultValues = idleState.writeDefaultValues; st.iKOnFeet = false; st.speed = 1;
            }
            EditorUtility.SetDirty(ctrl); AssetDatabase.SaveAssets();
            L("states: " + string.Join(",", sm.states.Select(s => s.state.name)));
            RenderPreview(chargeClip, releaseClip);
            File.WriteAllLines(Path.Combine(Report, "bake_log.txt"), log);
            File.WriteAllText(Path.Combine(Report, "bake_done.txt"), "PASS");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { L("FATAL " + e); File.WriteAllLines(Path.Combine(Report, "bake_log.txt"), log); Debug.LogException(e); EditorApplication.Exit(1); }
    }

    // ───────────── preview frames for review ─────────────
    static void RenderPreview(AnimationClip charge, AnimationClip release)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        ShaderUtil.allowAsyncCompilation = false;
        var light = new GameObject("Key").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.2f; light.transform.rotation = Quaternion.Euler(40, -30, 0);
        RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.5f, .52f, .56f);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane); floor.transform.localScale = Vector3.one * .4f;
        var cam = new GameObject("Cam").AddComponent<Camera>(); cam.orthographic = true; cam.orthographicSize = 1.05f; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.17f, .18f, .21f);
        var rt = new RenderTexture(480, 480, 24); cam.targetTexture = rt; var tex = new Texture2D(480, 480, TextureFormat.RGB24, false);
        foreach (var viewer in new[] { ("side", new Vector3(5, 1.0f, 0)), ("front34", new Vector3(3.2f, 1.1f, 3.8f)) })
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(UnifiedBoxingInstaller.Boxer));
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            foreach (var mb in model.GetComponentsInChildren<MonoBehaviour>(true)) mb.enabled = false;
            var anim = model.GetComponentsInChildren<Animator>(true).First(a => a.avatar && a.avatar.isHuman); anim.runtimeAnimatorController = null; anim.applyRootMotion = false; anim.Rebind();
            // Several renders happen inside one editor frame; recompute skinning for each so frames are not stale.
            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>()) smr.forceMatrixRecalculationPerRender = true;
            cam.transform.position = viewer.Item2; cam.transform.LookAt(new Vector3(0, .78f, .25f));
            foreach (var clip in new[] { charge, release })
            {
                var g = PlayableGraph.Create("QiPalm preview"); g.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var p = AnimationClipPlayable.Create(g, clip); p.SetApplyFootIK(false); AnimationPlayableOutput.Create(g, "o", anim).SetSourcePlayable(p); g.Play();
                p.SetApplyPlayableIK(false);
                int n = Mathf.CeilToInt(clip.length * 15) + 1; string dir = Path.Combine(Report, "frames", clip.name + "_" + viewer.Item1); Directory.CreateDirectory(dir);
                for (int f = 0; f < n; f++)
                {
                    p.SetTime(Mathf.Min(f / 15f, clip.length)); g.Evaluate(0);
                    var req = new RenderPipeline.StandardRequest { destination = rt };
                    if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req); else cam.Render();
                    RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 480, 480), 0, 0); tex.Apply(); RenderTexture.active = null;
                    File.WriteAllBytes(Path.Combine(dir, f.ToString("000") + ".jpg"), tex.EncodeToJPG(88));
                }
                g.Destroy();
            }
            Object.DestroyImmediate(model);
        }
    }
}
