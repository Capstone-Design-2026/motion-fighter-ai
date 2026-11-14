using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using HB = UnityEngine.HumanBodyBones;
using Object = UnityEngine.Object;

/// <summary>
/// 적들의 동작을 주인공(Hero02)의 동작으로 맞춘다 (2026-11-13, 두 번째 방식).
/// 대상 동작: 기본자세, 잽, 스트레이트, 얼굴 막기(올리기·유지), 로우킥 막기(올리기·유지), 걷기(앞·뒤).
/// 하이킥·로우킥·피격·KO는 주인공도 공용 동작을 쓰므로 그대로다.
///
/// 처음에는 주인공 클립의 근육 값을 그대로 복사했는데, 체형이 다른 적에서는 몸이 뒤로 젖혀지고 뒷발이 까치발이 되고 손이 얼굴에서 벗어났다.
/// 지금은 주인공 자세를 프레임마다 뼈대 위치로 읽어서 적의 뼈대에 다시 세운다.
///   - 몸통·머리·손·발의 방향: 양쪽 모두 T자세에서 얼마나 돌았는지를 그대로 옮긴다.
///   - 발: 주인공 발목 위치를 다리 길이 비율로 옮긴 자리에 정확히 놓는다(IK). 그래서 발이 뜨거나 묻히거나 미끄러지지 않는다.
///   - 손: 주인공 손목 위치를 팔 길이 비율로 옮기되, 얼굴 가까이 있을 때는 머리 기준으로 놓는다(IK). 그래서 가드가 얼굴을 가린다.
///   - 주먹: 모든 적이 같은 주먹 모양을 쥔다(주인공 아바타에는 손가락이 없어 주인공 클립에는 주먹 정보가 없다).
/// 그 자세를 HumanPoseHandler로 읽어 Humanoid 클립으로 굽는다. 클립은 제자리에 덮어쓴다(GUID와 이름 유지).
///
/// 실행: Unity -batchmode -executeMethod EnemyHeroMotionInstaller.Run [-enemyMotionReport &lt;폴더&gt;] [-enemyMotionOnly 이름,이름] [-enemyMotionDry]
/// 결과: 폴더에 install_log.txt, 확인 그림(stills/hero, stills/before, stills/after), install_done.txt(PASS/FAIL).
/// </summary>
public static class EnemyHeroMotionInstaller
{
    public const int Fps = 60;
    public const string StepClip = "Assets/Characters/Hero02/ApprovedMovement/Hero_Forward_Step_Loop.anim";
    public const string PunchFolder = "Assets/Characters/Hero02/AuthoredPunches";

    sealed class MotionDef { public string baseClip, key, heroPath; public bool reversed; public float[] stills = new float[0]; }
    static readonly MotionDef[] Motions =
    {
        new MotionDef { baseClip = "Unified_Common_IDLE", key = "IDLE", heroPath = HeroGuardBaker.IdleFbx, stills = new[] { .5f } },
        new MotionDef { baseClip = "Unified_Common_JAB", key = "JAB", heroPath = PunchFolder + "/Hero_JAB.anim", stills = new[] { 8f / 60f } },
        new MotionDef { baseClip = "Unified_Common_CROSS", key = "CROSS", heroPath = PunchFolder + "/Hero_CROSS.anim", stills = new[] { .1f, .25f } },
        new MotionDef { baseClip = "Unified_Common_GUARD", key = "GUARD", heroPath = HeroGuardBaker.Folder + "/" + HeroGuardBaker.GuardClip + ".anim" },
        new MotionDef { baseClip = "Unified_Common_GUARD(Clone)_HOLD", key = "GUARD_HOLD", heroPath = HeroGuardBaker.Folder + "/" + HeroGuardBaker.HoldClip + ".anim", stills = new[] { 0f } },
        new MotionDef { baseClip = "Unified_Common_GUARD_LOWKICK", key = "GUARD_LOWKICK", heroPath = HeroGuardBaker.Folder + "/" + HeroGuardBaker.LowClip + ".anim" },
        new MotionDef { baseClip = "Unified_Common_GUARD_LOWKICK(Clone)_HOLD", key = "GUARD_LOWKICK_HOLD", heroPath = HeroGuardBaker.Folder + "/" + HeroGuardBaker.LowHoldClip + ".anim", stills = new[] { 0f } },
        new MotionDef { baseClip = "Unified_Common_WALK_FORWARD", key = "WALK_FORWARD", heroPath = StepClip, stills = new[] { .17f, .5f } },
        new MotionDef { baseClip = "Unified_Common_WALK_BACK", key = "WALK_BACK", heroPath = StepClip, reversed = true },
    };

    const int N = (int)HB.LastBone;
    static readonly HB[] Body =
    {
        HB.Hips, HB.Spine, HB.Chest, HB.UpperChest, HB.Neck, HB.Head, HB.LeftShoulder, HB.RightShoulder,
        HB.LeftUpperArm, HB.RightUpperArm, HB.LeftLowerArm, HB.RightLowerArm, HB.LeftHand, HB.RightHand,
        HB.LeftUpperLeg, HB.RightUpperLeg, HB.LeftLowerLeg, HB.RightLowerLeg, HB.LeftFoot, HB.RightFoot, HB.LeftToes, HB.RightToes,
    };
    static readonly HB[] TorsoChain = { HB.Spine, HB.Chest, HB.UpperChest };
    static readonly HB[][] Arm = { new[] { HB.LeftShoulder, HB.LeftUpperArm, HB.LeftLowerArm, HB.LeftHand }, new[] { HB.RightShoulder, HB.RightUpperArm, HB.RightLowerArm, HB.RightHand } };
    static readonly HB[][] Leg = { new[] { HB.LeftUpperLeg, HB.LeftLowerLeg, HB.LeftFoot, HB.LeftToes }, new[] { HB.RightUpperLeg, HB.RightLowerLeg, HB.RightFoot, HB.RightToes } };

    /// <summary>한 캐릭터의 뼈대와 기준 T자세.</summary>
    sealed class Rig
    {
        public string name; public GameObject root; public Animator anim;
        public Transform[] bone = new Transform[N];
        public Quaternion[] tRot = new Quaternion[N];
        public Vector3[] tPos = new Vector3[N];
        public float leg, arm, height; public float[] sole = new float[2];
        public Transform[] all; public Vector3[] tLocalPos; public Quaternion[] tLocalRot;   // 엉덩이 아래 모든 뼈의 T자세(프레임마다 여기서 시작)
        public string tNote;
        public Transform T(HB b) => bone[(int)b];
        public bool Has(HB b) => bone[(int)b] != null;
        public void RestoreT() { for (int i = 0; i < all.Length; i++) { all[i].localPosition = tLocalPos[i]; all[i].localRotation = tLocalRot[i]; } }
    }
    sealed class Pose { public Vector3[] p = new Vector3[N]; public Quaternion[] q = new Quaternion[N]; public float[] muscles; }
    /// <summary>
    /// 손목 근육(아래팔 비틀림, 손목 위아래·안팎). 손목은 위치로 옮기지 않고 주인공의 근육 값을 그대로 쓴다.
    /// 자동으로 뼈를 심은 모델은 손가락 관절 위치가 제각각이라 손 방향을 뼈 위치로 재면 주먹이 꺾이는 캐릭터가 나온다.
    /// </summary>
    static readonly int[] WristMuscles = Enumerable.Range(0, HumanTrait.MuscleCount).Where(i => { string n = HumanTrait.MuscleName[i]; return n.EndsWith("Forearm Twist In-Out") || n.EndsWith("Hand Down-Up") || n.EndsWith("Hand In-Out"); }).ToArray();
    sealed class Target { public string name, prefab; public Dictionary<string, AnimationClip> clips; }
    sealed class Sampler : IDisposable
    {
        PlayableGraph graph; AnimationClipPlayable play;
        public Sampler(Animator anim, AnimationClip clip)
        {
            graph = PlayableGraph.Create("EnemyHeroMotion"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            play = AnimationClipPlayable.Create(graph, clip); play.SetApplyFootIK(false); play.SetApplyPlayableIK(false);
            AnimationPlayableOutput.Create(graph, "out", anim).SetSourcePlayable(play); graph.Play();
        }
        public void Pose(float time) { play.SetTime(time); graph.Evaluate(0); }
        public void Dispose() { if (graph.IsValid()) graph.Destroy(); }
    }

    static string Arg(string name) { var a = Environment.GetCommandLineArgs(); int i = Array.IndexOf(a, name); return i >= 0 && i + 1 < a.Length ? a[i + 1] : null; }
    static string Report => Arg("-enemyMotionReport") ?? Path.GetFullPath("Logs/EnemyHeroMotion");
    static readonly List<string> log = new List<string>();
    static void L(string s) { log.Add(s); Debug.Log("[EnemyHeroMotion] " + s); }
    static bool IsFinger(string muscle) => muscle.Contains("Thumb") || muscle.Contains("Index") || muscle.Contains("Middle") || muscle.Contains("Ring") || muscle.Contains("Little");
    /// <summary>주먹. 승준이 적 잽·스트레이트에 넣은 값과 같다(엄지는 덜 굽히고 나머지 네 손가락은 말아 쥔다).</summary>
    static float Fist(string muscle) => muscle.Contains("Spread") ? 0f : muscle.Contains("Thumb") ? -.4f : -.92f;
    /// <summary>클립에 적는 근육 이름. 손가락만 형식이 다르다: "Left Index 1 Stretched" → "LeftHand.Index.1 Stretched".</summary>
    static string MuscleAttribute(string muscle)
    {
        if (!IsFinger(muscle)) return muscle;
        var parts = muscle.Split(' ');
        return parts[0] + "Hand." + parts[1] + "." + string.Join(" ", parts.Skip(2));
    }
    static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3 - 2 * x); }

    // ───────────── 뼈대 준비 ─────────────
    static Rig Build(string name, GameObject root)
    {
        var rig = new Rig { name = name, root = root };
        root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true)) if (mb != null) mb.enabled = false;
        rig.anim = root.GetComponentsInChildren<Animator>(true).First(a => a.avatar != null && a.avatar.isHuman);
        rig.anim.runtimeAnimatorController = null; rig.anim.applyRootMotion = false; rig.anim.cullingMode = AnimatorCullingMode.AlwaysAnimate; rig.anim.Rebind();
        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) { smr.forceMatrixRecalculationPerRender = true; smr.updateWhenOffscreen = true; }
        foreach (var b in Body) rig.bone[(int)b] = rig.anim.GetBoneTransform(b);
        foreach (var b in new[] { HB.Hips, HB.Spine, HB.Head, HB.LeftUpperArm, HB.LeftLowerArm, HB.LeftHand, HB.LeftUpperLeg, HB.LeftLowerLeg, HB.LeftFoot })
            if (!rig.Has(b)) throw new Exception(name + ": 필수 뼈가 없다 " + b);

        // 기준 자세는 모델을 만들 때의 자세(메시의 바인드 포즈)다. 좌우가 같고 몸이 곧게 선 자세라서 캐릭터끼리 견줄 수 있다.
        // 아바타에 저장된 T자세는 쓰지 않는다: 주인공과 일부 적은 싸움 자세에서 아바타를 만들어 골반이 10~29도 돌아가 있다.
        var hips = rig.T(HB.Hips);
        rig.all = hips.GetComponentsInChildren<Transform>(true);
        var bind = new Dictionary<Transform, Matrix4x4>();
        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var bones = smr.bones; var poses = smr.sharedMesh != null ? smr.sharedMesh.bindposes : null;
            if (poses == null || poses.Length != bones.Length) continue;
            for (int i = 0; i < bones.Length; i++) if (bones[i] != null && !bind.ContainsKey(bones[i])) bind[bones[i]] = smr.transform.localToWorldMatrix * poses[i].inverse;
        }
        int applied = 0;
        foreach (var t in rig.all)   // 부모가 먼저 오는 순서
            if (bind.TryGetValue(t, out var m)) { t.SetPositionAndRotation(m.MultiplyPoint3x4(Vector3.zero), m.rotation); applied++; }
        var unbound = Body.Where(b => rig.Has(b) && !bind.ContainsKey(rig.T(b))).ToArray();
        if (applied < 15 || unbound.Length > 4) throw new Exception(name + ": 바인드 포즈를 뼈대에 얹지 못했다 (" + applied + "개, 빠진 뼈: " + string.Join(",", unbound) + ")");

        // 기준 T자세로 다듬는다: 몸은 +Z를 보고, 팔은 옆으로 곧게, 다리는 아래로 곧게, 발끝은 앞으로.
        var note = new List<string>();
        // 몸이 보는 쪽은 어깨 관절 줄로 잰다(자동으로 뼈를 심은 모델은 골반 관절이 좌우로 어긋나 있기도 하다). 모델이 딴 쪽을 보고 서 있을 때만 돌린다.
        Vector3 across = rig.T(HB.RightUpperArm).position - rig.T(HB.LeftUpperArm).position; across.y = 0;
        Vector3 acrossHips = rig.T(HB.RightUpperLeg).position - rig.T(HB.LeftUpperLeg).position; acrossHips.y = 0;
        float yawShoulders = Vector3.SignedAngle(across, Vector3.right, Vector3.up), yawHips = Vector3.SignedAngle(acrossHips, Vector3.right, Vector3.up);
        // 어깨 줄과 골반 줄이 같은 쪽으로 같이 돌아가 있을 때만 몸이 돌아선 것으로 보고 바로 세운다(한쪽만 어긋난 것은 관절 위치가 좌우로 다른 것).
        float yaw = Mathf.Abs(yawShoulders - yawHips) < 4f ? (yawShoulders + yawHips) * .5f : 0f;
        if (Mathf.Abs(yaw) > .01f) hips.RotateAround(hips.position, Vector3.up, yaw);
        note.Add($"몸 방향 어깨 {yawShoulders:F1} 골반 {yawHips:F1} → {yaw:F1}");
        for (int s = 0; s < 2; s++)
        {
            Vector3 side = s == 0 ? Vector3.left : Vector3.right;
            Transform ua = rig.T(Arm[s][1]), la = rig.T(Arm[s][2]), hand = rig.T(Arm[s][3]);
            note.Add($"{(s == 0 ? "왼" : "오른")}팔 {Vector3.Angle(la.position - ua.position, side):F1}/{Vector3.Angle(hand.position - la.position, side):F1}");
            Aim(ua, la.position, ua.position + side); Aim(la, hand.position, la.position + side);
            if (HandTip(rig, s, out Vector3 tip)) { note.Add($"손 {Vector3.Angle(tip - hand.position, side):F1}"); Aim(hand, tip, hand.position + side); }
            Transform ul = rig.T(Leg[s][0]), ll = rig.T(Leg[s][1]), foot = rig.T(Leg[s][2]);
            var footRot = foot.rotation;
            note.Add($"{(s == 0 ? "왼" : "오른")}다리 {Vector3.Angle(ll.position - ul.position, Vector3.down):F1}/{Vector3.Angle(foot.position - ll.position, Vector3.down):F1}");
            Aim(ul, ll.position, ul.position + Vector3.down); Aim(ll, foot.position, ll.position + Vector3.down);
            foot.rotation = footRot;   // 발바닥은 아바타 T자세 그대로(바닥에 평평)
            if (ToeTip(rig, s, out Vector3 toe))
            {
                Vector3 flat = toe - foot.position; flat.y = 0;
                float turn = Vector3.SignedAngle(flat, Vector3.forward, Vector3.up);
                note.Add($"발끝 {turn:F1}");
                foot.rotation = Quaternion.AngleAxis(turn, Vector3.up) * foot.rotation;
            }
        }
        for (int s = 0; s < 2; s++)
        {
            // 손등 방향을 가늠하는 값: 손목에서 본 엄지 방향이 앞(+Z)에서 얼마나 돌아가 있는지(팔 축 둘레).
            var hand = rig.T(Arm[s][3]); var thumb = rig.anim.GetBoneTransform(s == 0 ? HB.LeftThumbProximal : HB.RightThumbProximal);
            if (thumb == null) foreach (Transform c in hand) if (c.name.IndexOf("thumb", StringComparison.OrdinalIgnoreCase) >= 0) thumb = c;
            if (thumb != null) note.Add($"엄지 {Vector3.SignedAngle(Vector3.forward, Vector3.ProjectOnPlane(thumb.position - hand.position, Vector3.right), s == 0 ? Vector3.left : Vector3.right):F0}");
        }
        if (unbound.Length > 0) note.Add("바인드 없는 뼈 " + string.Join(",", unbound));
        rig.tNote = string.Join(" ", note);
        foreach (var b in Body) if (rig.Has(b)) { rig.tRot[(int)b] = rig.T(b).rotation; rig.tPos[(int)b] = rig.T(b).position; }
        rig.tLocalPos = rig.all.Select(t => t.localPosition).ToArray(); rig.tLocalRot = rig.all.Select(t => t.localRotation).ToArray();
        for (int s = 0; s < 2; s++)
        {
            rig.leg += .5f * (Vector3.Distance(rig.T(Leg[s][0]).position, rig.T(Leg[s][1]).position) + Vector3.Distance(rig.T(Leg[s][1]).position, rig.T(Leg[s][2]).position));
            rig.arm += .5f * (Vector3.Distance(rig.T(Arm[s][1]).position, rig.T(Arm[s][2]).position) + Vector3.Distance(rig.T(Arm[s][2]).position, rig.T(Arm[s][3]).position));
        }
        var low = FootLow(rig);
        float floor = Mathf.Min(low[0], low[1]);
        for (int s = 0; s < 2; s++) rig.sole[s] = rig.T(Leg[s][2]).position.y - floor;
        rig.height = rig.T(HB.Head).position.y - floor;
        return rig;
    }
    /// <summary>뼈를 돌려 child 위치가 target 방향에 오게 한다.</summary>
    static void Aim(Transform bone, Vector3 child, Vector3 target)
    {
        Vector3 from = child - bone.position, to = target - bone.position;
        if (from.sqrMagnitude < 1e-12f || to.sqrMagnitude < 1e-12f) return;
        bone.rotation = Quaternion.FromToRotation(from, to) * bone.rotation;
    }
    /// <summary>손끝 방향을 재는 점. 사람형 손가락 뼈가 있으면 가운뎃손가락 첫 마디, 없으면 엄지가 아닌 손의 자식 뼈.</summary>
    static bool HandTip(Rig rig, int side, out Vector3 tip)
    {
        var middle = rig.anim.GetBoneTransform(side == 0 ? HB.LeftMiddleProximal : HB.RightMiddleProximal);
        if (middle != null) { tip = middle.position; return true; }
        var hand = rig.T(Arm[side][3]);
        foreach (Transform c in hand) if (c.name.IndexOf("thumb", StringComparison.OrdinalIgnoreCase) < 0 && (c.position - hand.position).sqrMagnitude > 1e-6f) { tip = c.position; return true; }
        tip = default; return false;
    }
    static bool ToeTip(Rig rig, int side, out Vector3 toe)
    {
        if (rig.Has(Leg[side][3])) { toe = rig.T(Leg[side][3]).position; return true; }
        var foot = rig.T(Leg[side][2]);
        foreach (Transform c in foot) if ((c.position - foot.position).sqrMagnitude > 1e-6f) { toe = c.position; return true; }
        toe = default; return false;
    }
    /// <summary>두 발 각각의 가장 낮은 지점(발 주변의 메시만 본다. 옷자락이나 무기는 빼기 위해서다).</summary>
    static float[] FootLow(Rig rig)
    {
        float[] min = { float.MaxValue, float.MaxValue };
        Vector3[] feet = { rig.T(HB.LeftFoot).position, rig.T(HB.RightFoot).position };
        float reach = Vector3.Distance(rig.T(HB.LeftLowerLeg).position, feet[0]) * .75f; float r2 = reach * reach;
        foreach (var skin in rig.root.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (!skin.enabled) continue;
            var mesh = new Mesh(); skin.BakeMesh(mesh);
            var matrix = Matrix4x4.TRS(skin.transform.position, skin.transform.rotation, Vector3.one);   // BakeMesh가 이미 배율을 넣는다
            foreach (var v in mesh.vertices)
            {
                var w = matrix.MultiplyPoint3x4(v);
                for (int s = 0; s < 2; s++) if (w.y < min[s] && (w - feet[s]).sqrMagnitude < r2) min[s] = w.y;
            }
            Object.DestroyImmediate(mesh);
        }
        return min;
    }
    static Pose Capture(Rig rig)
    {
        var pose = new Pose();
        foreach (var b in Body) if (rig.Has(b)) { pose.p[(int)b] = rig.T(b).position; pose.q[(int)b] = rig.T(b).rotation; }
        return pose;
    }

    // ───────────── 자세 옮기기 ─────────────
    sealed class Fit { public float legRatio, armRatio, floorHero, floorEnemy, soleAdjust, heroUnit; public float reachClamp; }

    /// <summary>두 마디(위팔·아래팔, 허벅지·정강이)의 가운데 관절 위치. 끝이 닿지 않으면 닿는 데까지만 편다.</summary>
    static Vector3 Middle(Vector3 start, ref Vector3 end, float l1, float l2, Vector3 pole, Fit fit)
    {
        Vector3 delta = end - start; float want = delta.magnitude;
        float d = Mathf.Clamp(want, Mathf.Abs(l1 - l2) + 1e-4f, (l1 + l2) * .999f);
        Vector3 axis = want > 1e-6f ? delta / want : Vector3.down;
        if (Mathf.Abs(want - d) > 1e-5f) { fit.reachClamp = Mathf.Max(fit.reachClamp, Mathf.Abs(want - d)); end = start + axis * d; }
        float along = (l1 * l1 - l2 * l2 + d * d) / (2 * d);
        Vector3 side = Vector3.ProjectOnPlane(pole, axis);
        if (side.sqrMagnitude < 1e-8f) side = Vector3.ProjectOnPlane(Vector3.forward, axis);
        return start + axis * along + side.normalized * Mathf.Sqrt(Mathf.Max(0, l1 * l1 - along * along));
    }

    static void Retarget(Rig hero, Pose h, Rig e, Fit fit)
    {
        e.RestoreT();
        Quaternion D(HB b) => h.q[(int)b] * Quaternion.Inverse(hero.tRot[(int)b]);
        void Turn(HB b, Quaternion d) { if (e.Has(b)) e.T(b).rotation = d * e.tRot[(int)b]; }

        // 골반 방향, 몸통(마디 수가 달라도 엉덩이→가슴 끝까지 같은 비율로 나눠 옮긴다), 목, 머리
        Quaternion dHips = D(HB.Hips);
        Turn(HB.Hips, dHips);
        var hc = TorsoChain.Where(hero.Has).ToArray(); var ec = TorsoChain.Where(e.Has).ToArray();
        Quaternion At(float u)
        {
            float x = u * hc.Length; int j = Mathf.Min(hc.Length - 1, Mathf.FloorToInt(x - 1e-5f));
            return Quaternion.Slerp(j <= 0 ? dHips : D(hc[j - 1]), D(hc[Mathf.Max(0, j)]), x - Mathf.Max(0, j));
        }
        for (int i = 0; i < ec.Length; i++) Turn(ec[i], At((i + 1f) / ec.Length));
        Quaternion dChest = D(hc[hc.Length - 1]), dHead = D(HB.Head);
        Turn(HB.Neck, hero.Has(HB.Neck) ? D(HB.Neck) : Quaternion.Slerp(dChest, dHead, .5f));
        Turn(HB.Head, dHead);

        // 다리: 골반 높이와 발목 위치를 다리 길이 비율로 옮긴다. 높이는 "발바닥이 바닥에 평평할 때의 발목 높이"를 기준으로 잰다.
        Vector3 Place(Vector3 p, float soleHero, float soleEnemy) => new Vector3(p.x * fit.legRatio, fit.floorEnemy + soleEnemy + fit.soleAdjust + (p.y - fit.floorHero - soleHero) * fit.legRatio, p.z * fit.legRatio);
        Vector3 hipMid = Place((h.p[(int)HB.LeftUpperLeg] + h.p[(int)HB.RightUpperLeg]) * .5f, (hero.sole[0] + hero.sole[1]) * .5f, (e.sole[0] + e.sole[1]) * .5f);
        e.T(HB.Hips).position += hipMid - (e.T(HB.LeftUpperLeg).position + e.T(HB.RightUpperLeg).position) * .5f;
        for (int s = 0; s < 2; s++)
        {
            HB ul = Leg[s][0], ll = Leg[s][1], ft = Leg[s][2], toes = Leg[s][3];
            Vector3 start = e.T(ul).position, ankle = Place(h.p[(int)ft], hero.sole[s], e.sole[s]);
            float l1 = Vector3.Distance(e.tPos[(int)ul], e.tPos[(int)ll]), l2 = Vector3.Distance(e.tPos[(int)ll], e.tPos[(int)ft]);
            Vector3 knee = Middle(start, ref ankle, l1, l2, h.p[(int)ll] - h.p[(int)ul], fit);
            Turn(ul, D(ul)); Aim(e.T(ul), e.T(ll).position, knee);
            Turn(ll, D(ll)); Aim(e.T(ll), e.T(ft).position, ankle);
            Turn(ft, D(ft));
            if (hero.Has(toes)) Turn(toes, D(toes));
        }

        // 팔: 어깨뼈는 그대로 옮기고, 손목은 가슴 가운데 기준(팔 길이 비율) → 얼굴 가까이에서는 머리 기준으로 놓는다.
        for (int s = 0; s < 2; s++) if (hero.Has(Arm[s][0])) Turn(Arm[s][0], D(Arm[s][0]));
        Vector3 chestHero = (h.p[(int)HB.LeftUpperArm] + h.p[(int)HB.RightUpperArm]) * .5f, chestEnemy = (e.T(HB.LeftUpperArm).position + e.T(HB.RightUpperArm).position) * .5f;
        Vector3 headHero = h.p[(int)HB.Head], headEnemy = e.T(HB.Head).position;
        for (int s = 0; s < 2; s++)
        {
            HB ua = Arm[s][1], la = Arm[s][2], hand = Arm[s][3];
            Vector3 wristHero = h.p[(int)hand];
            float near = Smooth(Mathf.InverseLerp(.42f, .20f, (wristHero - headHero).magnitude / fit.heroUnit));
            Vector3 wrist = Vector3.Lerp(chestEnemy + fit.armRatio * (wristHero - chestHero), headEnemy + fit.armRatio * (wristHero - headHero), near);
            Vector3 start = e.T(ua).position;
            float l1 = Vector3.Distance(e.tPos[(int)ua], e.tPos[(int)la]), l2 = Vector3.Distance(e.tPos[(int)la], e.tPos[(int)hand]);
            Vector3 elbow = Middle(start, ref wrist, l1, l2, h.p[(int)la] - h.p[(int)ua], fit);
            Turn(ua, D(ua)); Aim(e.T(ua), e.T(la).position, elbow);
            Turn(la, D(la)); Quaternion asCopied = e.T(la).rotation; Aim(e.T(la), e.T(hand).position, wrist);
            // 손은 아래팔이 손목 자리를 맞추느라 더 돈 만큼 같이 돌린다. 그래야 손목이 꺾인 정도가 주인공과 같다.
            Turn(hand, e.T(la).rotation * Quaternion.Inverse(asCopied) * D(hand));
        }
    }

    static AnimationClip MakeClip(string name, HumanPose[] poses, bool loop, AnimationClipSettings settings)
    {
        int n = poses.Length;
        for (int i = 1; i < n; i++)
            if (Quaternion.Dot(poses[i - 1].bodyRotation, poses[i].bodyRotation) < 0) { var q = poses[i].bodyRotation; poses[i].bodyRotation = new Quaternion(-q.x, -q.y, -q.z, -q.w); }
        var clip = new AnimationClip { name = name, frameRate = Fps };
        AnimationCurve C(Func<int, float> v)
        {
            var c = new AnimationCurve(Enumerable.Range(0, n).Select(i => new Keyframe(i / (float)Fps, v(loop && i == n - 1 ? 0 : i))).ToArray());
            for (int i = 0; i < n; i++) { AnimationUtility.SetKeyLeftTangentMode(c, i, AnimationUtility.TangentMode.ClampedAuto); AnimationUtility.SetKeyRightTangentMode(c, i, AnimationUtility.TangentMode.ClampedAuto); }
            return c;
        }
        void Muscle(string attr, Func<int, float> v) => AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), attr), C(v));
        Muscle("RootT.x", i => poses[i].bodyPosition.x); Muscle("RootT.y", i => poses[i].bodyPosition.y); Muscle("RootT.z", i => poses[i].bodyPosition.z);
        Muscle("RootQ.x", i => poses[i].bodyRotation.x); Muscle("RootQ.y", i => poses[i].bodyRotation.y); Muscle("RootQ.z", i => poses[i].bodyRotation.z); Muscle("RootQ.w", i => poses[i].bodyRotation.w);
        for (int m = 0; m < HumanTrait.MuscleCount; m++) { int k = m; Muscle(MuscleAttribute(HumanTrait.MuscleName[m]), i => poses[i].muscles[k]); }
        settings.loopTime = loop; settings.loopBlend = false; settings.startTime = 0; settings.stopTime = (n - 1) / (float)Fps; settings.cycleOffset = 0; settings.mirror = false;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        return clip;
    }

    // ───────────── 자세를 숫자로 보기 ─────────────
    /// <summary>앞으로 숙인 각도(도, +가 앞) / 두 발이 바닥에서 들린 각도(도, +가 발끝이 들림) / 두 손목이 머리에서 떨어진 거리(팔 길이 대비).</summary>
    static string Describe(Rig rig)
    {
        Vector3 up = rig.T(HB.Head).position - rig.T(HB.Hips).position;
        float lean = Mathf.Atan2(up.z, up.y) * Mathf.Rad2Deg;
        string s = $"숙임 {lean,5:F1}";
        for (int i = 0; i < 2; i++)
        {
            Vector3 f = rig.T(Leg[i][2]).rotation * Quaternion.Inverse(rig.tRot[(int)Leg[i][2]]) * Vector3.forward;
            s += $" {(i == 0 ? "왼" : "오른")}발 {Mathf.Asin(Mathf.Clamp(f.y, -1, 1)) * Mathf.Rad2Deg,5:F1}";
        }
        for (int i = 0; i < 2; i++) s += $" {(i == 0 ? "왼" : "오른")}손-머리 {Vector3.Distance(rig.T(Arm[i][3]).position, rig.T(HB.Head).position) / rig.arm,4:F2}";
        for (int i = 0; i < 2; i++)
            if (HandTip(rig, i, out Vector3 tip))
                s += $" {(i == 0 ? "왼" : "오른")}손목 꺾임 {Vector3.Angle(rig.T(Arm[i][3]).position - rig.T(Arm[i][2]).position, tip - rig.T(Arm[i][3]).position),4:F0}";
        return s;
    }

    static AnimationClip LoadClip(string path)
    {
        var clip = path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)
            ? AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"))
            : AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null || !clip.isHumanMotion) throw new Exception("주인공 클립을 찾지 못했거나 Humanoid가 아니다: " + path);
        return clip;
    }

    public static void Run()
    {
        if (!Application.isBatchMode) throw new Exception("Run with Unity -batchmode.");
        log.Clear(); Directory.CreateDirectory(Report);
        bool dry = Environment.GetCommandLineArgs().Contains("-enemyMotionDry");
        try
        {
            AssetDatabase.Refresh();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = Camera();

            // ── 주인공: 클립을 프레임마다 뼈대 위치로 읽어 둔다 ──
            var heroModel = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(HeroGuardBaker.HeroPrefab));
            PrefabUtility.UnpackPrefabInstance(heroModel, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            // 잽·스트레이트 전용 보정 메시(AuthoredPunches)는 게임에서 스크립트가 켜고 끈다. 여기서는 뼈대만 읽으므로 꺼 둔다.
            foreach (var t in heroModel.GetComponentsInChildren<Transform>(true)) if (t.name == "AuthoredPunches") t.gameObject.SetActive(false);
            var hero = Build("Hero", heroModel);
            L($"주인공 프리팹 배율: 뿌리 {heroModel.transform.lossyScale.y:F3} / 애니메이터 {hero.anim.transform.lossyScale.y:F3} → 배율 1일 때 다리 길이 {hero.leg / hero.anim.transform.lossyScale.y:F4}");
            L($"주인공: 다리 {hero.leg:F4} 팔 {hero.arm:F4} 머리 높이 {hero.height:F4} 발목 높이 {hero.sole[0]:F4}/{hero.sole[1]:F4} 배율 {hero.anim.transform.lossyScale.y:F3} | T자세 보정(도) {hero.tNote}");
            L("주인공 사람형 뼈: " + string.Join(", ", Body.Where(hero.Has)));
            hero.RestoreT(); Shot(cam, hero, "tpose", "HERO");
            var heroClips = new Dictionary<string, AnimationClip>(); var heroFrames = new Dictionary<string, Pose[]>();
            var heroHandler = new HumanPoseHandler(hero.anim.avatar, hero.anim.transform);
            var idleSettings = AnimationUtility.GetAnimationClipSettings(LoadClip(HeroGuardBaker.IdleFbx));
            float floorHero;
            using (var s = new Sampler(hero.anim, LoadClip(HeroGuardBaker.IdleFbx))) { s.Pose(1f / 30f); var low = FootLow(hero); floorHero = Mathf.Min(low[0], low[1]); L($"주인공 기본자세의 발 높이: 왼 {low[0]:F4} 오른 {low[1]:F4} | {Describe(hero)}"); }
            foreach (var m in Motions)
            {
                var clip = LoadClip(m.heroPath); heroClips[m.key] = clip;
                int n = Mathf.Max(2, Mathf.RoundToInt(clip.length * Fps) + 1);
                var frames = new Pose[n];
                using (var s = new Sampler(hero.anim, clip))
                    for (int f = 0; f < n; f++)
                    {
                        int src = clip.isLooping && f == n - 1 ? 0 : f;
                        s.Pose(Mathf.Min(src / (float)Fps, clip.length - 1e-5f)); frames[f] = Capture(hero);
                        var hp = new HumanPose(); heroHandler.GetHumanPose(ref hp); frames[f].muscles = (float[])hp.muscles.Clone();
                    }
                if (m.reversed) Array.Reverse(frames);
                heroFrames[m.key] = frames;
                L($"주인공 {m.key}: {clip.name} length={clip.length:F3}s loop={clip.isLooping} frames={n}{(m.reversed ? " (거꾸로)" : "")}");
                if (!m.reversed)
                    using (var s = new Sampler(hero.anim, clip))
                        foreach (float t in m.stills) { s.Pose(t); Shot(cam, hero, "hero", $"HERO_{m.key}_{Mathf.RoundToInt(t * 100):000}"); L($"  주인공 {m.key}@{t:F2}: {Describe(hero)}"); }
            }
            // 걷기 클립이 한 바퀴에 딛는 모양(주인공 단위): 재생 빠르기 계산에 쓰는 값을 확인한다.
            {
                var f = heroFrames["WALK_FORWARD"]; int lf = (int)HB.LeftFoot, rf = (int)HB.RightFoot, hp = (int)HB.Hips;
                L("주인공 걷기 한 바퀴 (골반 z | 왼발 z,y | 오른발 z,y): " + string.Join("  ", Enumerable.Range(0, f.Length).Where(i => i % 4 == 0).Select(i => $"{f[i].p[hp].z:F3}|{f[i].p[lf].z:F3},{f[i].p[lf].y:F3}|{f[i].p[rf].z:F3},{f[i].p[rf].y:F3}")));
            }
            // 주먹이 가장 멀리 나가는 시각(판정 구간과 견줘 보려고 적어 둔다).
            foreach (var (key, hand) in new[] { ("JAB", HB.LeftHand), ("CROSS", HB.RightHand) })
            {
                var f = heroFrames[key];
                L($"주인공 {key} 주먹이 나간 거리(z, 프레임 2개마다): " + string.Join(" ", Enumerable.Range(0, f.Length).Where(i => i % 2 == 0).Select(i => $"{i / (float)Fps:F2}:{f[i].p[(int)hand].z:F2}")));
            }
            heroHandler.Dispose();
            heroModel.SetActive(false);

            // ── 적 ──
            var only = Arg("-enemyMotionOnly")?.Split(',');
            var targets = Targets().Where(t => only == null || only.Contains(t.name)).ToList();
            foreach (var target in targets)
            {
                var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(target.prefab));
                var e = Build(target.name, root);
                var fit = new Fit { legRatio = e.leg / hero.leg, armRatio = e.arm / hero.arm, floorHero = floorHero, floorEnemy = 0f, heroUnit = hero.leg / .85f };
                L($"{target.name}: 다리 {e.leg:F4} (주인공의 {fit.legRatio:F3}배) 팔 {e.arm:F4} ({fit.armRatio:F3}배) 머리 높이 {e.height:F4} 발목 높이 {e.sole[0]:F4}/{e.sole[1]:F4} humanScale={e.anim.humanScale:F3} 배율={e.anim.transform.lossyScale.y:F3}");
                L($"  T자세 보정(도) {e.tNote} | 몸통 뼈 {string.Join("/", TorsoChain.Where(e.Has))} 어깨뼈 {e.Has(HB.LeftShoulder)} 발가락 {e.Has(HB.LeftToes)}");

                e.RestoreT(); Shot(cam, e, "tpose", target.name);
                // 바꾸기 전 모습
                foreach (var m in Motions)
                    if (!m.reversed && m.stills.Length > 0)
                        using (var s = new Sampler(e.anim, target.clips[m.baseClip]))
                            foreach (float t in m.stills) { s.Pose(t); Shot(cam, e, "before", $"{target.name}_{m.key}_{Mathf.RoundToInt(t * 100):000}"); L($"  이전 {m.key}@{t:F2}: {Describe(e)}"); }

                // 기본자세의 기준 프레임에서 발이 바닥에 정확히 닿게 발목 높이를 한 번 맞춘다(신발 밑창 두께 차이).
                Retarget(hero, heroFrames["IDLE"][2], e, fit);
                { var low = FootLow(e); float error = Mathf.Min(low[0], low[1]) - fit.floorEnemy; fit.soleAdjust -= error; L($"  기본자세 발 높이 보정 {-error * 100f:F2}cm"); }

                var handler = new HumanPoseHandler(e.anim.avatar, e.anim.transform);
                var baked = new Dictionary<string, (AnimationClip clip, Vector3[][] posed)>();
                foreach (var m in Motions)
                {
                    var frames = heroFrames[m.key]; var source = heroClips[m.key]; var existing = target.clips[m.baseClip];
                    string path = AssetDatabase.GetAssetPath(existing);
                    if (string.IsNullOrEmpty(path) || !path.EndsWith(".anim")) throw new Exception(target.name + " " + m.key + ": 덮어쓸 .anim 클립이 아니다 (" + path + ")");
                    var poses = new HumanPose[frames.Length]; var posed = new Vector3[frames.Length][];
                    fit.reachClamp = 0f; float sink = 0f, lift = 0f;
                    for (int f = 0; f < frames.Length; f++)
                    {
                        Retarget(hero, frames[f], e, fit);
                        poses[f] = new HumanPose(); handler.GetHumanPose(ref poses[f]);
                        for (int k = 0; k < HumanTrait.MuscleCount; k++) { string muscle = HumanTrait.MuscleName[k]; if (IsFinger(muscle)) poses[f].muscles[k] = Fist(muscle); }
                        foreach (int k in WristMuscles) poses[f].muscles[k] = frames[f].muscles[k];
                        posed[f] = Body.Where(e.Has).Select(b => e.T(b).position).ToArray();
                        if (f % 6 == 0) { var low = FootLow(e); float lowest = Mathf.Min(low[0], low[1]) - fit.floorEnemy; sink = Mathf.Min(sink, lowest); lift = Mathf.Max(lift, lowest); }
                    }
                    var clip = MakeClip(existing.name, poses, source.isLooping, idleSettings);
                    if (!dry) { EditorUtility.CopySerialized(clip, existing); EditorUtility.SetDirty(existing); Object.DestroyImmediate(clip); baked[m.key] = (existing, posed); }
                    else baked[m.key] = (clip, posed);
                    L($"  {m.key,-18} → {existing.name}: {frames.Length}프레임 / 팔다리가 닿지 않아 줄인 거리 최대 {fit.reachClamp * 100f:F2}cm / 발이 바닥 아래로 {-sink * 100f:F2}cm, 가장 낮은 발이 뜬 높이 최대 {lift * 100f:F2}cm");
                }
                handler.Dispose();
                if (!dry) AssetDatabase.SaveAssets();

                // 구운 클립을 다시 재생해 세운 자세와 비교한다(Humanoid 변환에서 잃은 정도).
                var present = Body.Where(e.Has).ToArray();
                foreach (var m in Motions)
                {
                    var (clip, posed) = baked[m.key]; float worst = 0, sum = 0; int count = 0; string worstAt = "";
                    using (var s = new Sampler(e.anim, clip))
                        for (int f = 0; f < posed.Length; f++)
                        {
                            s.Pose(Mathf.Min(f / (float)Fps, clip.length - (clip.isLooping ? 1e-4f : 0f)));
                            for (int b = 0; b < present.Length; b++)
                            {
                                float err = (e.T(present[b]).position - posed[f][b]).magnitude; sum += err; count++;
                                if (err > worst) { worst = err; worstAt = present[b] + " @" + f; }
                            }
                        }
                    L($"  {m.key,-18} 재생 확인: length={clip.length:F3}s loop={clip.isLooping} human={clip.isHumanMotion} / 세운 자세와의 차이 평균 {sum / count / e.height * 100f:F2}% 최대 {worst / e.height * 100f:F2}% of height ({worstAt})");
                    if (!clip.isHumanMotion) throw new Exception(clip.name + ": Humanoid 클립이 아니다");
                    if (worst > e.height * .06f) throw new Exception(clip.name + ": 재생 결과가 세운 자세와 다르다 (" + worst + ")");
                    if (!m.reversed)
                        using (var s = new Sampler(e.anim, clip))
                            foreach (float t in m.stills) { s.Pose(t); Shot(cam, e, "after", $"{target.name}_{m.key}_{Mathf.RoundToInt(t * 100):000}"); L($"  이후 {m.key}@{t:F2}: {Describe(e)}"); }
                }
                Object.DestroyImmediate(root);
            }
            if (!dry) AssetDatabase.SaveAssets();
            File.WriteAllLines(Path.Combine(Report, "install_log.txt"), log);
            File.WriteAllText(Path.Combine(Report, "install_done.txt"), "PASS " + targets.Count + " characters x " + Motions.Length + " clips" + (dry ? " (dry run, nothing saved)" : ""));
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            L("FATAL " + e); File.WriteAllLines(Path.Combine(Report, "install_log.txt"), log);
            File.WriteAllText(Path.Combine(Report, "install_done.txt"), "FAIL " + e.Message); Debug.LogException(e); EditorApplication.Exit(1);
        }
    }

    /// <summary>공용 세트(기본 적), 척후병, 스토리 상대 10명. 각자 지금 쓰는 클립을 기준 클립 이름으로 찾는다.</summary>
    static List<Target> Targets()
    {
        var list = new List<Target>();
        var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(UnifiedBoxingInstaller.Folder + "/Unified_Combat.controller");
        var common = controller.animationClips.Where(c => c != null).GroupBy(c => c.name).ToDictionary(g => g.Key, g => g.First());
        list.Add(new Target { name = "Common", prefab = UnifiedBoxingInstaller.Boxer, clips = common });
        void Add(string name, string prefab, string overridePath)
        {
            var overrides = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(overridePath);
            if (overrides == null) throw new Exception("오버라이드 컨트롤러가 없다: " + overridePath);
            var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>(); overrides.GetOverrides(pairs);
            var clips = new Dictionary<string, AnimationClip>();
            foreach (var p in pairs) if (p.Key != null) clips[p.Key.name] = p.Value != null ? p.Value : p.Key;
            list.Add(new Target { name = name, prefab = prefab, clips = clips });
        }
        Add("Scout", UnifiedBoxingInstaller.Scout, UnifiedBoxingInstaller.Folder + "/Unified_Scout.overrideController");
        for (int i = 0; i < StoryCharacterInstaller.Keys.Length; i++)
        {
            string key = StoryCharacterInstaller.Keys[i];
            Add(key, StoryCharacterInstaller.PrefabPath(i), StoryCharacterInstaller.Root + "/" + key + "/" + key + "_UnifiedBoxing.overrideController");
        }
        foreach (var t in list)
            foreach (var m in Motions)
            {
                if (!t.clips.TryGetValue(m.baseClip, out var clip) || clip == null) throw new Exception(t.name + ": " + m.baseClip + " 에 해당하는 클립이 없다");
                // 스토리 상대와 척후병은 자기 전용 클립이어야 한다. 공용 클립을 그대로 쓰고 있으면 공용 세트와 두 번 덮어쓰게 된다.
                if (t.name != "Common" && clip == common[m.baseClip]) throw new Exception(t.name + ": " + m.key + " 가 공용 클립을 그대로 쓰고 있다");
            }
        return list;
    }

    // ───────────── 확인용 그림 ─────────────
    static Camera Camera()
    {
        ShaderUtil.allowAsyncCompilation = false;
        var light = new GameObject("Key").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.2f; light.transform.rotation = Quaternion.Euler(40, -30, 0);
        RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.55f, .56f, .6f);
        var cam = new GameObject("Cam").AddComponent<Camera>(); cam.orthographic = true; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.32f, .33f, .36f);
        cam.nearClipPlane = .01f; cam.farClipPlane = 50f; cam.targetTexture = new RenderTexture(570, 690, 24);
        return cam;
    }
    static void Shot(Camera cam, Rig rig, string folder, string name)
    {
        string dir = Path.Combine(Report, "stills", folder); Directory.CreateDirectory(dir);
        float h = rig.height * 1.2f; cam.orthographicSize = h * .56f;
        var rt = cam.targetTexture; var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        // 모델은 +Z를 보고 선다. side = 게임에서 보이는 옆모습, quarter = 앞에서 비스듬히, front = 정면.
        foreach (var (view, pos) in new[] { ("side", new Vector3(5, .5f, 0)), ("quarter", new Vector3(3.2f, .6f, 3.8f)), ("front", new Vector3(0, .5f, 5)) })
        {
            cam.transform.position = new Vector3(pos.x, pos.y * h, pos.z + h * .06f); cam.transform.LookAt(new Vector3(0, h * .5f, h * .06f));
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req); else cam.Render();
            RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply(); RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(dir, name + "_" + view + ".jpg"), tex.EncodeToJPG(88));
        }
        Object.DestroyImmediate(tex);
    }
}
