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
/// 주인공(Hero02) 전용 방어 클립을 굽는다. 얼굴 막기는 복싱 하이가드, 로우킥 막기는 무에타이 체크(앞다리를 들어 정강이로 받기).
/// 동작은 Blender에서 만들었다(Hero_Guard_*.motion.json). 여기서는 그 자세를 주인공 뼈대에 그대로 얹은 뒤
/// HumanPoseHandler로 Humanoid 클립으로 바꾼다. 손가락·삼각근처럼 Humanoid 밖에 있는 뼈는 Transform 커브로 함께 넣는다.
///   Hero_Guard_High / Hero_Guard_Low : 0.9초. 0~0.27 올리기 / 0.351~ 막힌 타격 반응(_HIT) / 0.648~ 풀기(_RELEASE)
///   Hero_Guard_High_Hold / Hero_Guard_Low_Hold : 막은 자세로 버티는 반복(_HOLD)
/// 적은 계속 공용 방어 클립을 쓴다. 바뀌는 것은 Hero_ApprovedMovement.controller의 방어 상태 여덟 개뿐이다.
/// 실행: Unity -batchmode -executeMethod HeroGuardBaker.Bake [-heroGuardReport &lt;폴더&gt;]
/// </summary>
public static class HeroGuardBaker
{
    public const string Folder = "Assets/Characters/Hero02/ApprovedGuard";
    static readonly string[] Sources = { Folder + "/Hero_Guard_High.motion.json", Folder + "/Hero_Guard_Low.motion.json" };
    public const string GuardClip = "Hero_Guard_High", HoldClip = "Hero_Guard_High_Hold", LowClip = "Hero_Guard_Low", LowHoldClip = "Hero_Guard_Low_Hold";
    /// <summary>클립 → 그 클립을 재생하는 주인공 컨트롤러의 상태들.</summary>
    static readonly Dictionary<string, string[]> States = new Dictionary<string, string[]>
    {
        { GuardClip, new[] { "GUARD", "GUARD_HIT", "GUARD_RELEASE" } }, { HoldClip, new[] { "GUARD_HOLD" } },
        { LowClip, new[] { "GUARD_LOWKICK", "GUARD_LOWKICK_HIT", "GUARD_LOWKICK_RELEASE" } }, { LowHoldClip, new[] { "GUARD_LOWKICK_HOLD" } },
    };
    public const string HeroPrefab = "Assets/Characters/Hero02/Hero02_Visual.prefab";
    public const string IdleFbx = "Assets/Characters/Hero02/ApprovedIdle/Hero_Idle_Base_Final.fbx";
    public const string ControllerPath = "Assets/Characters/Hero02/ApprovedMovement/Hero_ApprovedMovement.controller";
    /// <summary>Blender에서 자세를 얹은 기준: 승인 기본자세의 두 번째 프레임.</summary>
    const float BaseTime = 1f / 30f;

    [Serializable] class Motion { public int fps; public string[] bones; public string[] parents; public float[] basePos; public MotionClip[] clips; }
    [Serializable] class MotionClip { public string name; public bool loop; public int frames; public float[] hips; public float[] rot; public float[] pos; }

    static string Report { get { var a = Environment.GetCommandLineArgs(); int i = Array.IndexOf(a, "-heroGuardReport"); return i >= 0 && i + 1 < a.Length ? a[i + 1] : Path.GetFullPath("Logs/HeroGuard"); } }
    static readonly List<string> log = new List<string>();
    static void L(string s) { log.Add(s); Debug.Log("[HeroGuard] " + s); }
    static Vector3 V(float[] a, int i) => new Vector3(a[i * 3], a[i * 3 + 1], a[i * 3 + 2]);

    /// <summary>Blender 뼈대 공간 → Unity 모델 공간. 축 바꿈(부호 포함) + 배율 + 이동.</summary>
    struct Map
    {
        public int[] axis; public float[] sign; public float det, scale; public Vector3 from, to;
        public Vector3 Dir(Vector3 v) => new Vector3(sign[0] * v[axis[0]], sign[1] * v[axis[1]], sign[2] * v[axis[2]]);
        public Vector3 Point(Vector3 p) => to + scale * Dir(p - from);
        // 회전은 축 벡터처럼 옮기되, 좌우가 뒤집히는 변환이면 도는 방향도 뒤집힌다.
        public Quaternion Rot(float w, float x, float y, float z) { var v = det * Dir(new Vector3(x, y, z)); return new Quaternion(v.x, v.y, v.z, w); }
        public override string ToString() => $"x={(sign[0] > 0 ? "+" : "-")}{"xyz"[axis[0]]} y={(sign[1] > 0 ? "+" : "-")}{"xyz"[axis[1]]} z={(sign[2] > 0 ? "+" : "-")}{"xyz"[axis[2]]} scale={scale:F4}";
    }
    /// <summary>두 점 묶음이 가장 잘 겹치는 축 바꿈을 48가지 가운데서 고른다.</summary>
    static Map Fit(Vector3[] src, Vector3[] dst, out float rms)
    {
        Vector3 cs = Vector3.zero, cd = Vector3.zero;
        foreach (var p in src) cs += p;
        foreach (var p in dst) cd += p;
        cs /= src.Length; cd /= dst.Length;
        int[][] perms = { new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 } };
        float[] parity = { 1, -1, -1, 1, 1, -1 };
        Map best = default; rms = float.MaxValue;
        for (int pi = 0; pi < 6; pi++) for (int s = 0; s < 8; s++)
        {
            var m = new Map { axis = perms[pi], sign = new[] { (s & 1) == 0 ? 1f : -1f, (s & 2) == 0 ? 1f : -1f, (s & 4) == 0 ? 1f : -1f }, from = cs, to = cd, scale = 1 };
            m.det = parity[pi] * m.sign[0] * m.sign[1] * m.sign[2];
            float num = 0, den = 0;
            for (int i = 0; i < src.Length; i++) { var a = m.Dir(src[i] - cs); num += Vector3.Dot(dst[i] - cd, a); den += a.sqrMagnitude; }
            m.scale = num / den; if (m.scale <= 0) continue;
            float err = 0; for (int i = 0; i < src.Length; i++) err += (dst[i] - m.Point(src[i])).sqrMagnitude;
            err = Mathf.Sqrt(err / src.Length);
            if (err < rms) { rms = err; best = m; }
        }
        return best;
    }

    static Animator Spawn()
    {
        var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(HeroPrefab));
        PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        foreach (var mb in model.GetComponentsInChildren<MonoBehaviour>(true)) mb.enabled = false;
        var anim = model.GetComponentsInChildren<Animator>(true).First(a => a.avatar && a.avatar.isHuman);
        model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); model.transform.localScale = Vector3.one;
        anim.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); anim.transform.localScale = Vector3.one;
        anim.runtimeAnimatorController = null; anim.applyRootMotion = false; anim.cullingMode = AnimatorCullingMode.AlwaysAnimate; anim.Rebind();
        // 한 에디터 프레임 안에서 여러 번 그리므로 매번 스키닝을 다시 계산하게 한다.
        foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>()) smr.forceMatrixRecalculationPerRender = true;
        return anim;
    }
    static void Sample(Animator anim, AnimationClip clip, float time)
    {
        var g = PlayableGraph.Create("HeroGuard"); g.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        var p = AnimationClipPlayable.Create(g, clip); p.SetApplyFootIK(false); p.SetApplyPlayableIK(false);
        AnimationPlayableOutput.Create(g, "out", anim).SetSourcePlayable(p); g.Play(); p.SetTime(time); g.Evaluate(0); g.Destroy();
    }
    static AnimationClip Idle() => AssetDatabase.LoadAllAssetsAtPath(IdleFbx).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));

    static bool IsFinger(string muscle) => muscle.Contains("Thumb") || muscle.Contains("Index") || muscle.Contains("Middle") || muscle.Contains("Ring") || muscle.Contains("Little");

    static AnimationClip MakeClip(string name, int fps, HumanPose[] poses, string[] extraPaths, (Quaternion r, Vector3 p, Vector3 s)[][] local, bool loop, AnimationClipSettings settings)
    {
        int n = poses.Length;
        // 쿼터니언 부호가 프레임 사이에서 뒤집히지 않게 맞춘다.
        for (int i = 1; i < n; i++)
        {
            if (Quaternion.Dot(poses[i - 1].bodyRotation, poses[i].bodyRotation) < 0) { var q = poses[i].bodyRotation; poses[i].bodyRotation = new Quaternion(-q.x, -q.y, -q.z, -q.w); }
            for (int e = 0; e < extraPaths.Length; e++)
                if (Quaternion.Dot(local[i - 1][e].r, local[i][e].r) < 0) { var q = local[i][e].r; local[i][e].r = new Quaternion(-q.x, -q.y, -q.z, -q.w); }
        }
        var clip = new AnimationClip { name = name, frameRate = fps };
        AnimationCurve C(Func<int, float> v)
        {
            // 반복 클립은 마지막 키를 첫 키와 똑같이 둬서 이음매가 없게 한다.
            var c = new AnimationCurve(Enumerable.Range(0, n).Select(i => new Keyframe(i / (float)fps, v(loop && i == n - 1 ? 0 : i))).ToArray());
            for (int i = 0; i < n; i++) { AnimationUtility.SetKeyLeftTangentMode(c, i, AnimationUtility.TangentMode.ClampedAuto); AnimationUtility.SetKeyRightTangentMode(c, i, AnimationUtility.TangentMode.ClampedAuto); }
            return c;
        }
        void Muscle(string attr, Func<int, float> v) => AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), attr), C(v));
        Muscle("RootT.x", i => poses[i].bodyPosition.x); Muscle("RootT.y", i => poses[i].bodyPosition.y); Muscle("RootT.z", i => poses[i].bodyPosition.z);
        Muscle("RootQ.x", i => poses[i].bodyRotation.x); Muscle("RootQ.y", i => poses[i].bodyRotation.y); Muscle("RootQ.z", i => poses[i].bodyRotation.z); Muscle("RootQ.w", i => poses[i].bodyRotation.w);
        for (int m = 0; m < HumanTrait.MuscleCount; m++)
        {
            string muscle = HumanTrait.MuscleName[m];
            if (IsFinger(muscle)) continue; // 주인공 아바타에는 손가락이 없다. 주먹은 아래 Transform 커브가 쥔다.
            int k = m; Muscle(muscle, i => poses[i].muscles[k]);
        }
        for (int e = 0; e < extraPaths.Length; e++)
        {
            int k = e; string path = extraPaths[e];
            clip.SetCurve(path, typeof(Transform), "localRotation.x", C(i => local[i][k].r.x)); clip.SetCurve(path, typeof(Transform), "localRotation.y", C(i => local[i][k].r.y));
            clip.SetCurve(path, typeof(Transform), "localRotation.z", C(i => local[i][k].r.z)); clip.SetCurve(path, typeof(Transform), "localRotation.w", C(i => local[i][k].r.w));
            clip.SetCurve(path, typeof(Transform), "localPosition.x", C(i => local[i][k].p.x)); clip.SetCurve(path, typeof(Transform), "localPosition.y", C(i => local[i][k].p.y)); clip.SetCurve(path, typeof(Transform), "localPosition.z", C(i => local[i][k].p.z));
            clip.SetCurve(path, typeof(Transform), "localScale.x", C(i => local[i][k].s.x)); clip.SetCurve(path, typeof(Transform), "localScale.y", C(i => local[i][k].s.y)); clip.SetCurve(path, typeof(Transform), "localScale.z", C(i => local[i][k].s.z));
        }
        // 뿌리 처리(높이·위치를 자세 안에 그대로 둠)는 승인 기본자세와 같게 하고, 반복 여부와 길이만 다르다.
        settings.loopTime = loop; settings.loopBlend = false; settings.startTime = 0; settings.stopTime = (n - 1) / (float)fps; settings.cycleOffset = 0; settings.mirror = false;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
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
            // 동작 파일은 모두 같은 뼈대·같은 기준 자세에서 내보낸 것이라 클립만 한데 모은다.
            var motion = JsonUtility.FromJson<Motion>(File.ReadAllText(Sources[0]));
            foreach (string path in Sources.Skip(1))
            {
                var more = JsonUtility.FromJson<Motion>(File.ReadAllText(path));
                if (!more.bones.SequenceEqual(motion.bones) || more.fps != motion.fps) throw new Exception(path + ": 뼈대나 fps가 다르다");
                motion.clips = motion.clips.Concat(more.clips).ToArray();
            }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var idle = Idle();
            var anim = Spawn();
            int nb = motion.bones.Length;
            var all = anim.GetComponentsInChildren<Transform>(true);
            var bone = motion.bones.Select(n => all.FirstOrDefault(t => t.name == n)).ToArray();
            var missing = motion.bones.Where((n, i) => bone[i] == null).ToArray();
            if (missing.Length > 0) throw new Exception("주인공에서 뼈를 찾지 못함: " + string.Join(", ", missing));
            int hips = Array.IndexOf(motion.bones, "Hips");

            // 기준 자세(승인 기본자세 2번째 프레임)에서 두 프로그램의 좌표계를 맞춘다.
            Sample(anim, idle, BaseTime);
            var baseRot = bone.Select(t => t.rotation).ToArray();
            var basePos = bone.Select(t => t.position).ToArray();
            var map = Fit(Enumerable.Range(0, nb).Select(i => V(motion.basePos, i)).ToArray(), basePos, out float rms);
            float height = basePos.Max(p => p.y) - basePos.Min(p => p.y);
            L($"idle clip={idle.name} length={idle.length:F3} humanScale={anim.humanScale:F3} joint height={height:F3}");
            L($"Blender→Unity: {map} det={map.det:+0;-0} / 기준 자세 관절 위치 차이 RMS {rms / height * 100:F3}% of height");
            if (rms > height * .01f) throw new Exception("기준 자세가 Blender와 맞지 않는다 (RMS " + rms + ")");

            var human = new HashSet<Transform>();
            foreach (HumanBodyBones b in Enum.GetValues(typeof(HumanBodyBones))) { if (b == HumanBodyBones.LastBone) continue; var t = anim.GetBoneTransform(b); if (t) human.Add(t); }
            var extra = Enumerable.Range(0, nb).Where(i => !human.Contains(bone[i])).ToArray();
            var extraPaths = extra.Select(i => AnimationUtility.CalculateTransformPath(bone[i], anim.transform)).ToArray();
            L("Humanoid 뼈 " + (nb - extra.Length) + "개, Transform 커브로 넣는 뼈 " + extra.Length + "개: " + string.Join(", ", extra.Select(i => motion.bones[i])));

            var handler = new HumanPoseHandler(anim.avatar, anim.transform);
            var baked = new Dictionary<string, AnimationClip>(); var direct = new Dictionary<string, Vector3[][]>();
            foreach (var mc in motion.clips)
            {
                var poses = new HumanPose[mc.frames]; var local = new (Quaternion r, Vector3 p, Vector3 s)[mc.frames][]; var posed = new Vector3[mc.frames][];
                float worst = 0; string worstAt = "";
                for (int f = 0; f < mc.frames; f++)
                {
                    for (int b = 0; b < nb; b++) // 부모가 먼저 오는 순서
                    {
                        int k = (f * nb + b) * 4;
                        bone[b].rotation = map.Rot(mc.rot[k], mc.rot[k + 1], mc.rot[k + 2], mc.rot[k + 3]) * baseRot[b];
                        if (b == hips) bone[b].position = map.Point(V(mc.hips, f));
                    }
                    poses[f] = new HumanPose(); handler.GetHumanPose(ref poses[f]);
                    local[f] = extra.Select(i => (bone[i].localRotation, bone[i].localPosition, bone[i].localScale)).ToArray();
                    posed[f] = bone.Select(t => t.position).ToArray();
                    for (int b = 0; b < nb; b++) { float e = (posed[f][b] - map.Point(V(mc.pos, f * nb + b))).magnitude; if (e > worst) { worst = e; worstAt = motion.bones[b] + " @" + f; } }
                }
                L($"{mc.name}: {mc.frames}프레임 / Blender 대비 관절 위치 오차 최대 {worst / height * 100:F3}% of height ({worstAt})");
                if (worst > height * .02f) throw new Exception(mc.name + ": 자세가 Blender와 다르게 얹혔다");
                var clip = MakeClip(mc.name, motion.fps, poses, extraPaths, local, mc.loop, AnimationUtility.GetAnimationClipSettings(idle));
                baked[mc.name] = Save(clip, Folder + "/" + mc.name + ".anim"); direct[mc.name] = posed;
            }
            handler.Dispose();
            AssetDatabase.SaveAssets();

            // 구운 클립을 다시 재생해 직접 얹은 자세와 비교한다(Humanoid 변환에서 잃은 정도).
            foreach (var mc in motion.clips)
            {
                var clip = baked[mc.name]; float worst = 0, sum = 0; string worstAt = ""; int count = 0;
                for (int f = 0; f < mc.frames; f++)
                {
                    Sample(anim, clip, Mathf.Min(f / (float)motion.fps, clip.length - (mc.loop ? 1e-4f : 0f)));
                    for (int b = 0; b < nb; b++)
                    {
                        float e = (bone[b].position - direct[mc.name][f][b]).magnitude; sum += e; count++;
                        if (e > worst) { worst = e; worstAt = motion.bones[b] + " @" + f; }
                    }
                }
                L($"{mc.name}: length={clip.length:F3}s loop={clip.isLooping} human={clip.isHumanMotion} / 재생 결과와 얹은 자세의 차이 평균 {sum / count / height * 100:F3}% 최대 {worst / height * 100:F3}% of height ({worstAt})");
                if (!clip.isHumanMotion) throw new Exception(mc.name + ": Humanoid 클립이 아니다");
                if (worst > height * .03f) throw new Exception(mc.name + ": 재생 결과가 얹은 자세와 다르다");
            }

            // 주인공 컨트롤러의 방어 상태에만 연결한다. 적이 쓰는 Unified_Combat은 건드리지 않는다.
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            var states = ctrl.layers[0].stateMachine.states.Select(s => s.state).ToArray();
            foreach (var pair in States)
                foreach (string stateName in pair.Value)
                {
                    var st = states.FirstOrDefault(s => s.name == stateName);
                    if (st == null) throw new Exception("컨트롤러에 상태가 없다: " + stateName);
                    st.motion = baked[pair.Key];
                }
            EditorUtility.SetDirty(ctrl); AssetDatabase.SaveAssets();
            L("방어 상태: " + string.Join(", ", states.Where(s => s.name.StartsWith("GUARD")).Select(s => s.name + "=" + (s.motion ? s.motion.name : "none"))));

            RenderPreview(anim, new[] { (baked[GuardClip], baked[HoldClip], "guard"), (baked[LowClip], baked[LowHoldClip], "low") });
            Object.DestroyImmediate(anim.transform.root.gameObject);
            File.WriteAllLines(Path.Combine(Report, "bake_log.txt"), log);
            File.WriteAllText(Path.Combine(Report, "bake_done.txt"), "PASS");
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            L("FATAL " + e); File.WriteAllLines(Path.Combine(Report, "bake_log.txt"), log);
            File.WriteAllText(Path.Combine(Report, "bake_done.txt"), "FAIL " + e.Message); Debug.LogException(e); EditorApplication.Exit(1);
        }
    }

    // ───────────── 확인용 그림 ─────────────
    static void RenderPreview(Animator anim, (AnimationClip guard, AnimationClip hold, string tag)[] sets)
    {
        ShaderUtil.allowAsyncCompilation = false;
        var light = new GameObject("Key").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.2f; light.transform.rotation = Quaternion.Euler(40, -30, 0);
        RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.55f, .56f, .6f);
        float h = anim.GetBoneTransform(HumanBodyBones.Head).position.y * 1.28f;
        var cam = new GameObject("Cam").AddComponent<Camera>(); cam.orthographic = true; cam.orthographicSize = h * .56f; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.32f, .33f, .36f);
        cam.nearClipPlane = .01f; cam.farClipPlane = 50f;
        var rt = new RenderTexture(440, 520, 24); cam.targetTexture = rt; var tex = new Texture2D(440, 520, TextureFormat.RGB24, false);
        string dir = Path.Combine(Report, "frames"); Directory.CreateDirectory(dir);
        // 게임 화면은 주인공의 오른쪽 옆을 본다(오른쪽을 보고 설 때). 주인공은 +Z를 보고 선다.
        var views = new[] { ("game", new Vector3(5, .5f, 0)), ("front", new Vector3(0, .5f, 5)), ("quarter", new Vector3(3.2f, .6f, 3.8f)), ("left", new Vector3(-5, .5f, 0)) };
        var shots = new List<(AnimationClip clip, float t, string tag)>();
        foreach (var (guard, hold, name) in sets)
        {
            foreach (float t in new[] { 0f, .10f, .20f, .27f, .39f, .56f, .76f, .90f }) shots.Add((guard, t, name + "_" + Mathf.RoundToInt(t * 100).ToString("000")));
            foreach (float t in new[] { 0f, .8f }) shots.Add((hold, t, name + "hold_" + Mathf.RoundToInt(t * 100).ToString("000")));
        }
        foreach (var (clip, t, tag) in shots)
        {
            Sample(anim, clip, t);
            foreach (var (view, pos) in views)
            {
                cam.transform.position = new Vector3(pos.x, pos.y * h, pos.z); cam.transform.LookAt(new Vector3(0, h * .5f, .04f));
                var req = new RenderPipeline.StandardRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req); else cam.Render();
                RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply(); RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(dir, view + "_" + tag + ".jpg"), tex.EncodeToJPG(90));
            }
        }
    }
}
