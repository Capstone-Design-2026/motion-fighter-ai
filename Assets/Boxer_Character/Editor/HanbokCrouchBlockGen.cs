using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

// 앉아서 막기(커버 가드) 생성기: Gen_CROUCHBLOCK_GUARD / IN / OUT
// 하체·골반 높이·바운스는 Gen_CROUCH_GUARD 그대로, 상체·팔·고개만 커버 가드로 바꾼다.
public static class HanbokCrouchBlockGen
{
    // ---- 조절 가능한 값 ----
    public static float leanDeg = 0f;        // 척추 추가 숙임(앉기가 이미 19도 숙여 있어 기본 0)
    public static float nodDeg = -12f;       // 음수 = 고개 듦(상대를 봄)
    public static float turnDeg = 20f;       // 고개를 상대 쪽으로
    public static float shoulderRaise = 5f;  // 어깨 올림(도)
    // 손목 위치 = 머리 기준 (x: +가 캐릭터 오른쪽=뒷손, y: 위, z: 얼굴 앞)
    public static Vector3 rearWrist = new Vector3(0.09f, 0.20f, 0.02f);
    public static Vector3 frontWrist = new Vector3(-0.10f, -0.08f, 0.22f);
    // 팔꿈치가 향할 방향 (x=얼굴앞, y=위, z=캐릭터 오른쪽)
    public static Vector3 rearPole = new Vector3(1.0f, 0.30f, 0.55f);
    public static Vector3 frontPole = new Vector3(-0.30f, -1.0f, -0.10f);
    public static float inOvershoot = 1.12f;
    public static float lift = 0f;           // 전체 높이 보정

    const string Dir = "Assets/Boxer_Character/Animations/";
    static readonly Vector3 O = Vector3.forward;
    public static string lastInfo = "";

    static Vector3 V(Dictionary<string, float> f, string k) { return new Vector3(f[k + ".x"], f[k + ".y"], f[k + ".z"]); }
    static Quaternion Q(Dictionary<string, float> f, string k) { return new Quaternion(f[k + ".x"], f[k + ".y"], f[k + ".z"], f[k + ".w"]); }
    static void SetV(Dictionary<string, float> f, string k, Vector3 v) { f[k + ".x"] = v.x; f[k + ".y"] = v.y; f[k + ".z"] = v.z; }
    static void SetQ(Dictionary<string, float> f, string k, Quaternion q) { f[k + ".x"] = q.x; f[k + ".y"] = q.y; f[k + ".z"] = q.z; f[k + ".w"] = q.w; }

    static void TwoBone(Transform a, Transform b, Transform c, Vector3 target, Vector3 pole)
    {
        float la = (b.position - a.position).magnitude, lb = (c.position - b.position).magnitude;
        Vector3 at = target - a.position;
        float d = Mathf.Clamp(at.magnitude, 0.02f, la + lb - 0.002f);
        Vector3 dir = at.normalized;
        float cosA = Mathf.Clamp((la * la + d * d - lb * lb) / (2 * la * d), -1f, 1f);
        Vector3 pd = Vector3.ProjectOnPlane(pole, dir).normalized;
        Vector3 bPos = a.position + dir * (la * cosA) + pd * (la * Mathf.Sqrt(1 - cosA * cosA));
        a.rotation = Quaternion.FromToRotation(b.position - a.position, bPos - a.position) * a.rotation;
        Vector3 tgt = a.position + dir * d;
        b.rotation = Quaternion.FromToRotation(c.position - b.position, tgt - b.position) * b.rotation;
    }

    static Vector3 Palm(Animator an, bool left)
    {
        var hand = an.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        var idx = an.GetBoneTransform(left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
        var lit = an.GetBoneTransform(left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal);
        var mid = an.GetBoneTransform(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
        Vector3 across = idx.position - lit.position, along = mid.position - hand.position;
        return (left ? Vector3.Cross(across, along) : Vector3.Cross(along, across)).normalized;
    }

    static void RollPalm(Animator an, bool left, Vector3 desired)
    {
        var lower = an.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
        var hand = an.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        Vector3 axis = (hand.position - lower.position).normalized;
        Vector3 p = Vector3.ProjectOnPlane(Palm(an, left), axis), d = Vector3.ProjectOnPlane(desired, axis);
        float ang = Vector3.SignedAngle(p, d, axis);
        lower.rotation = Quaternion.AngleAxis(ang, axis) * lower.rotation;
    }

    // 앉기 자세(t)에서 상체만 커버 가드로 바꾼 한 프레임
    public static Dictionary<string, float> Cover(Animator an, HumanPoseHandler h, AnimationClip crouchGuard, float t)
    {
        var fr = HanbokBlockCoverGen.Sample(crouchGuard, t);
        HanbokBlockCoverGen.EvalState(an, "CROUCH", t, crouchGuard.length);
        var B = (System.Func<HumanBodyBones, Transform>)an.GetBoneTransform;

        Quaternion bq0 = Q(fr, "RootQ");
        Vector3 F = bq0 * Vector3.forward; F.y = 0; F.Normalize();

        // 발·다리·골반은 건드리지 않는다 (앉기 그대로). 발 월드 위치만 기억.
        var lFoot = B(HumanBodyBones.LeftFoot); var rFoot = B(HumanBodyBones.RightFoot);
        Vector3 lAnk = lFoot.position + Vector3.up * lift;
        Vector3 rAnk = rFoot.position + Vector3.up * lift;
        if (lift != 0f) B(HumanBodyBones.Hips).position += Vector3.up * lift;

        // 1) 척추 (앉기가 이미 숙여 있으므로 기본 0)
        if (Mathf.Abs(leanDeg) > 0.01f)
        {
            Vector3 D = (F * 0.6f + O * 0.4f).normalized;
            float[] w = { 0.4f, 0.35f, 0.25f };
            var sp = new[] { B(HumanBodyBones.Spine), B(HumanBodyBones.Chest), B(HumanBodyBones.UpperChest) };
            for (int i = 0; i < 3; i++)
            {
                if (sp[i] == null) continue;
                float r = leanDeg * w[i] * Mathf.Deg2Rad;
                sp[i].rotation = Quaternion.FromToRotation(Vector3.up, (Vector3.up * Mathf.Cos(r) + D * Mathf.Sin(r)).normalized) * sp[i].rotation;
            }
        }

        // 2) 고개: 상대 쪽으로 돌리고 들어서 상대를 봄
        var neck = B(HumanBodyBones.Neck); var head = B(HumanBodyBones.Head);
        neck.rotation = Quaternion.AngleAxis(-turnDeg * 0.5f, Vector3.up) * neck.rotation;
        head.rotation = Quaternion.AngleAxis(-turnDeg * 0.5f, Vector3.up) * head.rotation;
        Vector3 face = Quaternion.AngleAxis(-turnDeg, Vector3.up) * F;
        Vector3 faceSide = Vector3.Cross(Vector3.up, face).normalized;
        float nr = nodDeg * 0.5f * Mathf.Deg2Rad;
        Quaternion nod = Quaternion.FromToRotation(face, (face * Mathf.Cos(nr) - Vector3.up * Mathf.Sin(nr)).normalized);
        neck.rotation = nod * neck.rotation;
        head.rotation = nod * head.rotation;

        // 3) 어깨 올림
        var lSh = B(HumanBodyBones.LeftShoulder); var rSh = B(HumanBodyBones.RightShoulder);
        if (lSh) lSh.rotation = Quaternion.AngleAxis(shoulderRaise, F) * lSh.rotation;
        if (rSh) rSh.rotation = Quaternion.AngleAxis(-shoulderRaise, F) * rSh.rotation;

        // 4) 팔: 뒷손은 정수리 위로 아치, 앞팔은 가슴 앞에 세로로
        Vector3 hd = head.position;
        Vector3 rw = hd + faceSide * rearWrist.x + Vector3.up * rearWrist.y + face * rearWrist.z;
        TwoBone(B(HumanBodyBones.RightUpperArm), B(HumanBodyBones.RightLowerArm), B(HumanBodyBones.RightHand), rw,
            face * rearPole.x + Vector3.up * rearPole.y + faceSide * rearPole.z);
        Vector3 lw = hd + faceSide * frontWrist.x + Vector3.up * frontWrist.y + face * frontWrist.z;
        TwoBone(B(HumanBodyBones.LeftUpperArm), B(HumanBodyBones.LeftLowerArm), B(HumanBodyBones.LeftHand), lw,
            face * frontPole.x + Vector3.up * frontPole.y + faceSide * frontPole.z);
        RollPalm(an, false, (hd + Vector3.up * 0.05f - B(HumanBodyBones.RightHand).position));
        RollPalm(an, true, (-face * 0.75f + faceSide * 0.35f));

        lastInfo = "hips " + B(HumanBodyBones.Hips).position.ToString("F3") + " head " + hd.ToString("F3") +
                   " RH " + B(HumanBodyBones.RightHand).position.ToString("F3") +
                   " RElb " + B(HumanBodyBones.RightLowerArm).position.ToString("F3") +
                   " LH " + B(HumanBodyBones.LeftHand).position.ToString("F3") +
                   " headToRearElb " + Vector3.Distance(hd, B(HumanBodyBones.RightLowerArm).position).ToString("F3") +
                   " LF " + lFoot.position.ToString("F3") + " RF " + rFoot.position.ToString("F3");

        // 5) 근육값으로 캡처
        var hp = new HumanPose();
        h.GetHumanPose(ref hp);
        var outF = new Dictionary<string, float>(fr);
        var names = HumanTrait.MuscleName;
        for (int i = 0; i < names.Length; i++)
        {
            string k = HanbokBlockCoverGen.Key(names[i]);
            if (outF.ContainsKey(k)) outF[k] = hp.muscles[i];
        }
        // 손가락은 앉기 원본 그대로(이미 주먹)
        foreach (var k in new List<string>(outF.Keys))
            if (k.StartsWith("LeftHand.") || k.StartsWith("RightHand.")) outF[k] = fr[k];
        outF["Jaw Close"] = 0; outF["Jaw Left-Right"] = 0;

        Vector3 bp1 = hp.bodyPosition; Quaternion bq1 = hp.bodyRotation;
        SetV(outF, "RootT", bp1); SetQ(outF, "RootQ", bq1);
        float fb = an.leftFeetBottomHeight;
        foreach (var s in new[] { "Left", "Right" })
        {
            Vector3 ankW = (s == "Left") ? lAnk : rAnk;
            Vector3 gw = ankW - Vector3.up * fb;
            Quaternion gq = Q(fr, s + "FootQ");
            SetV(outF, s + "FootT", Quaternion.Inverse(bq1) * (gw - bp1) / an.humanScale);
            SetQ(outF, s + "FootQ", Quaternion.Inverse(bq1) * bq0 * gq);
        }
        return outF;
    }

    static void WriteClip(AnimationClip clip, List<float> times, List<Dictionary<string, float>> frames)
    {
        foreach (var b in AnimationUtility.GetCurveBindings(clip))
        {
            var keys = new Keyframe[times.Count];
            for (int i = 0; i < times.Count; i++) keys[i] = new Keyframe(times[i], frames[i][b.propertyName]);
            var c = new AnimationCurve(keys);
            for (int i = 0; i < keys.Length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(c, i, AnimationUtility.TangentMode.ClampedAuto);
                AnimationUtility.SetKeyRightTangentMode(c, i, AnimationUtility.TangentMode.ClampedAuto);
            }
            AnimationUtility.SetEditorCurve(clip, b, c);
        }
        EditorUtility.SetDirty(clip);
    }

    static Dictionary<string, float> Lerp(Dictionary<string, float> a, Dictionary<string, float> b, float t)
    {
        var o = new Dictionary<string, float>();
        foreach (var k in a.Keys) o[k] = Mathf.LerpUnclamped(a[k], b[k], t);
        return o;
    }

    static AnimationClip Ensure(string path, AnimationClip src)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path); }
        EditorUtility.CopySerialized(src, clip);
        clip.name = System.IO.Path.GetFileNameWithoutExtension(path);
        EditorUtility.SetDirty(clip);
        return clip;
    }

    public static string Generate()
    {
        var crouchGuard = AssetDatabase.LoadAssetAtPath<AnimationClip>(Dir + "Gen_CROUCH_GUARD.anim");
        var crouchIn = AssetDatabase.LoadAssetAtPath<AnimationClip>(Dir + "Gen_CROUCH_IN.anim");
        var crouchOut = AssetDatabase.LoadAssetAtPath<AnimationClip>(Dir + "Gen_CROUCH_OUT.anim");

        var guard = Ensure(Dir + "Gen_CROUCHBLOCK_GUARD.anim", crouchGuard);
        var cin = Ensure(Dir + "Gen_CROUCHBLOCK_IN.anim", crouchIn);
        var cout = Ensure(Dir + "Gen_CROUCHBLOCK_OUT.anim", crouchOut);
        AssetDatabase.SaveAssets();

        Animator an; var go = HanbokBlockCoverGen.Spawn(out an);
        var sb = new System.Text.StringBuilder();
        try
        {
            var h = new HumanPoseHandler(an.avatar, go.transform);
            var times = new List<float>(); var frames = new List<Dictionary<string, float>>();
            int n = 24; float len = crouchGuard.length;
            for (int i = 0; i <= n; i++)
            {
                float t = len * i / n;
                times.Add(t); frames.Add(Cover(an, h, crouchGuard, t));
                if (i == 0) sb.AppendLine("t0: " + lastInfo);
            }
            frames[n] = new Dictionary<string, float>(frames[0]);
            var g0 = frames[0];

            // 앉기 유지 자세(0초)에서 들어가고, 다시 그 자세로 나온다
            var c0 = HanbokBlockCoverGen.Sample(crouchGuard, 0f);
            var inT = new List<float> { 0f, 0.06f, 0.10f };
            var inF = new List<Dictionary<string, float>> { c0, Lerp(c0, g0, inOvershoot), g0 };
            var outT = new List<float> { 0f, 0.06f, 0.13f, 0.20f };
            var outF = new List<Dictionary<string, float>> { g0, Lerp(g0, c0, 0.15f), Lerp(g0, c0, 0.7f), c0 };

            WriteClip(guard, times, frames);
            WriteClip(cin, inT, inF);
            WriteClip(cout, outT, outF);
            AssetDatabase.SaveAssets();
            sb.AppendLine("guard len " + guard.length.ToString("F2") + " in " + cin.length.ToString("F2") + " out " + cout.length.ToString("F2"));
        }
        finally { Object.DestroyImmediate(go); }
        return sb.ToString();
    }
}
