using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

// 앉기 3차 (2026-09-30): 새 Mixamo Crouching(클립 CROUCH2)의 "낮은 하체 자세"를 가져오고
// 상체는 지금의 복싱 가드를 그대로 유지한다.
// 발은 IDLE 자리에 그대로 두어 앉았다 일어설 때 발이 미끄러지지 않게 한다.
public static class HanbokCrouchV3Gen
{
    const string Dir = "Assets/Boxer_Character/Animations/";
    const string Base = "Assets/Boxer_Character/Backups/0930_before_crouch_new/";

    // ---- 조절값 ----
    public static string srcState = "TMP_CROUCH2";
    public static float srcLen = 4.07f;
    public static float holdT = 3.00f;     // 새 동작에서 가져올 유지 자세 시점(초)
    public static string idleState = "IDLE";
    public static float idleLen = 2.37f;
    public static float hipY = -1f;        // 목표 골반 높이(m). 음수면 새 동작 그대로
    public static float lift = 0f;         // 바닥 높이 보정
    public static float bounce = 0.015f;   // 위아래 바운스(전체 폭)
    public static float swayAmp = 0.010f;  // 좌우 흔들림(±m)
    public static int steps = 24;
    public static float clipLen = 1.20f;
    public static float inLen = 0.30f, outLen = 0.30f;
    public static float kneeFwd = 1.0f, kneeOut = 0.35f;  // 무릎이 향할 방향
    public static Vector3 footFix = Vector3.zero;
    public static string lastInfo = "";

    static bool IsLeg(string m)
    {
        return m.StartsWith("Left Upper Leg") || m.StartsWith("Left Leg") || m.StartsWith("Left Foot") || m.StartsWith("Left Toes")
            || m.StartsWith("Right Upper Leg") || m.StartsWith("Right Leg") || m.StartsWith("Right Foot") || m.StartsWith("Right Toes");
    }

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

    static Vector3 V(Dictionary<string, float> f, string k) { return new Vector3(f[k + ".x"], f[k + ".y"], f[k + ".z"]); }
    static Quaternion Q(Dictionary<string, float> f, string k) { return new Quaternion(f[k + ".x"], f[k + ".y"], f[k + ".z"], f[k + ".w"]); }
    static void SetV(Dictionary<string, float> f, string k, Vector3 v) { f[k + ".x"] = v.x; f[k + ".y"] = v.y; f[k + ".z"] = v.z; }
    static void SetQ(Dictionary<string, float> f, string k, Quaternion q) { f[k + ".x"] = q.x; f[k + ".y"] = q.y; f[k + ".z"] = q.z; f[k + ".w"] = q.w; }

    static AnimationClip Ensure(string path, AnimationClip src)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path); }
        EditorUtility.CopySerialized(src, clip);
        clip.name = System.IO.Path.GetFileNameWithoutExtension(path);
        EditorUtility.SetDirty(clip);
        return clip;
    }

    static void WriteClip(AnimationClip clip, List<float> times, List<Dictionary<string, float>> frames)
    {
        foreach (var b in AnimationUtility.GetCurveBindings(clip))
        {
            var keys = new Keyframe[times.Count];
            for (int i = 0; i < times.Count; i++)
                keys[i] = new Keyframe(times[i], frames[i].ContainsKey(b.propertyName) ? frames[i][b.propertyName] : 0f);
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
        foreach (var k in a.Keys) o[k] = b.ContainsKey(k) ? Mathf.LerpUnclamped(a[k], b[k], t) : a[k];
        foreach (var k in b.Keys) if (!o.ContainsKey(k)) o[k] = b[k];
        return o;
    }

    // 발 키는 월드로 풀어서 섞은 뒤 그 프레임의 몸 기준으로 되돌린다
    static void SetFootKeys(Dictionary<string, float> f, Dictionary<string, float> a, Dictionary<string, float> b, float w, float hs)
    {
        Quaternion rqF = Q(f, "RootQ"); Vector3 rtF = V(f, "RootT");
        Quaternion rqA = Q(a, "RootQ"); Vector3 rtA = V(a, "RootT");
        Quaternion rqB = Q(b, "RootQ"); Vector3 rtB = V(b, "RootT");
        float cw = Mathf.Clamp01(w);
        for (int s = 0; s < 2; s++)
        {
            string side = s == 0 ? "Left" : "Right";
            if (!a.ContainsKey(side + "FootT.x") || !b.ContainsKey(side + "FootT.x")) continue;
            Vector3 wa = rtA + rqA * (V(a, side + "FootT") * hs);
            Vector3 wb = rtB + rqB * (V(b, side + "FootT") * hs);
            Vector3 ww = Vector3.Lerp(wa, wb, cw);
            SetV(f, side + "FootT", (Quaternion.Inverse(rqF) * (ww - rtF)) / hs);
            if (a.ContainsKey(side + "FootQ.w") && b.ContainsKey(side + "FootQ.w"))
            {
                Quaternion qa = rqA * Q(a, side + "FootQ");
                Quaternion qb = rqB * Q(b, side + "FootQ");
                SetQ(f, side + "FootQ", Quaternion.Inverse(rqF) * Quaternion.Slerp(qa, qb, cw));
            }
        }
    }

    public static string Generate()
    {
        var bGuard = AssetDatabase.LoadAssetAtPath<AnimationClip>(Base + "Gen_CROUCH_GUARD.anim");
        var bIn = AssetDatabase.LoadAssetAtPath<AnimationClip>(Base + "Gen_CROUCH_IN.anim");
        var bOut = AssetDatabase.LoadAssetAtPath<AnimationClip>(Base + "Gen_CROUCH_OUT.anim");
        var idleClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Dir + "Gen_IDLE_BOXER.anim");
        if (bGuard == null || idleClip == null) return "BASE CLIP NOT FOUND";

        var guard = Ensure(Dir + "Gen_CROUCH_GUARD.anim", bGuard);
        var cin = Ensure(Dir + "Gen_CROUCH_IN.anim", bIn);
        var cout = Ensure(Dir + "Gen_CROUCH_OUT.anim", bOut);
        AssetDatabase.SaveAssets();

        Animator an; var go = HanbokBlockCoverGen.Spawn(out an);
        var sb = new System.Text.StringBuilder();
        try
        {
            var h = new HumanPoseHandler(an.avatar, go.transform);
            var names = HumanTrait.MuscleName;
            float hs = an.humanScale, fbH = an.leftFeetBottomHeight;
            var tmpl = HanbokBlockCoverGen.Sample(bGuard, 0f);
            var idleFrame = HanbokBlockCoverGen.Sample(idleClip, 0f);

            // ---- IDLE 기준: 가드 상체 + 발자리 + FootQ 보정각 ----
            HanbokBlockCoverGen.EvalState(an, idleState, 0f, idleLen);
            var gPose = new HumanPose(); h.GetHumanPose(ref gPose);
            Vector3 iLF = an.GetBoneTransform(HumanBodyBones.LeftFoot).position;
            Vector3 iRF = an.GetBoneTransform(HumanBodyBones.RightFoot).position;
            Quaternion iLFq = an.GetBoneTransform(HumanBodyBones.LeftFoot).rotation;
            Quaternion iRFq = an.GetBoneTransform(HumanBodyBones.RightFoot).rotation;
            float gHipY = an.GetBoneTransform(HumanBodyBones.Hips).position.y;
            Quaternion rqRef = Q(idleFrame, "RootQ");
            Quaternion qFixL = Quaternion.Inverse(Quaternion.Inverse(rqRef) * iLFq) * Q(idleFrame, "LeftFootQ");
            Quaternion qFixR = Quaternion.Inverse(Quaternion.Inverse(rqRef) * iRFq) * Q(idleFrame, "RightFootQ");

            // ---- 새 동작의 낮은 하체 자세 ----
            HanbokBlockCoverGen.EvalState(an, srcState, holdT, srcLen);
            var cPose = new HumanPose(); h.GetHumanPose(ref cPose);
            float cHipY = an.GetBoneTransform(HumanBodyBones.Hips).position.y;
            float targetHip = hipY < 0f ? cHipY : hipY;

            Vector3 O = Vector3.forward, sideV = Vector3.Cross(Vector3.up, O).normalized;
            Vector3 poleL = (O * kneeFwd - sideV * kneeOut).normalized;
            Vector3 poleR = (O * kneeFwd + sideV * kneeOut).normalized;

            var times = new List<float>(); var frames = new List<Dictionary<string, float>>();
            for (int i = 0; i <= steps; i++)
            {
                float t = clipLen * i / steps;
                float ph = 2f * Mathf.PI * i / steps;
                float bob = -Mathf.Cos(ph * 2f) * 0.5f * bounce + 0.5f * bounce;
                Vector3 sway = sideV * (Mathf.Sin(ph) * swayAmp) + O * (Mathf.Sin(ph * 2f) * swayAmp * 0.4f);

                var mix = new HumanPose();
                mix.muscles = new float[gPose.muscles.Length];
                for (int k = 0; k < mix.muscles.Length; k++)
                    mix.muscles[k] = IsLeg(names[k]) ? cPose.muscles[k] : gPose.muscles[k];
                mix.bodyRotation = gPose.bodyRotation;
                mix.bodyPosition = gPose.bodyPosition + Vector3.up * (targetHip - gHipY + lift + bob) + sway;
                h.SetHumanPose(ref mix);

                var lfT = an.GetBoneTransform(HumanBodyBones.LeftFoot);
                var rfT = an.GetBoneTransform(HumanBodyBones.RightFoot);
                var lTo = an.GetBoneTransform(HumanBodyBones.LeftToes);
                var rTo = an.GetBoneTransform(HumanBodyBones.RightToes);
                Quaternion klt = lTo ? lTo.rotation : Quaternion.identity, krt = rTo ? rTo.rotation : Quaternion.identity;
                Vector3 tL = iLF + footFix + Vector3.up * lift;
                Vector3 tR = iRF + footFix + Vector3.up * lift;
                TwoBone(an.GetBoneTransform(HumanBodyBones.LeftUpperLeg), an.GetBoneTransform(HumanBodyBones.LeftLowerLeg), lfT, tL, poleL);
                TwoBone(an.GetBoneTransform(HumanBodyBones.RightUpperLeg), an.GetBoneTransform(HumanBodyBones.RightLowerLeg), rfT, tR, poleR);
                lfT.rotation = iLFq; rfT.rotation = iRFq;
                if (lTo) lTo.rotation = klt; if (rTo) rTo.rotation = krt;
                var fixedPose = new HumanPose(); h.GetHumanPose(ref fixedPose);
                mix.muscles = fixedPose.muscles; mix.bodyPosition = fixedPose.bodyPosition; mix.bodyRotation = fixedPose.bodyRotation;
                h.SetHumanPose(ref mix);

                var outF = new Dictionary<string, float>(tmpl);
                for (int k = 0; k < names.Length; k++)
                {
                    string key = HanbokBlockCoverGen.Key(names[k]);
                    if (outF.ContainsKey(key)) outF[key] = mix.muscles[k];
                }
                outF["Jaw Close"] = 0; outF["Jaw Left-Right"] = 0;
                SetV(outF, "RootT", mix.bodyPosition); SetQ(outF, "RootQ", mix.bodyRotation);
                for (int s = 0; s < 2; s++)
                {
                    string side = s == 0 ? "Left" : "Right";
                    Vector3 ankW = (s == 0 ? tL : tR);
                    Quaternion fq = (s == 0 ? iLFq : iRFq);
                    Vector3 gw = ankW - Vector3.up * fbH;
                    SetV(outF, side + "FootT", (Quaternion.Inverse(mix.bodyRotation) * (gw - mix.bodyPosition)) / hs);
                    SetQ(outF, side + "FootQ", Quaternion.Inverse(mix.bodyRotation) * fq * (s == 0 ? qFixL : qFixR));
                }
                times.Add(t); frames.Add(outF);
                if (i == 0) lastInfo = "유지 골반 " + an.GetBoneTransform(HumanBodyBones.Hips).position.y.ToString("F3")
                    + " 머리 " + an.GetBoneTransform(HumanBodyBones.Head).position.y.ToString("F3")
                    + " 앞주먹 " + an.GetBoneTransform(HumanBodyBones.LeftHand).position.ToString("F3");
            }
            frames[steps] = new Dictionary<string, float>(frames[0]);

            // ---- 들어가기 / 나오기 ----
            var g0 = frames[0];
            int n = 11;
            var idleIn = HanbokBlockCoverGen.Sample(bIn, 0f);
            var idleOut = HanbokBlockCoverGen.Sample(bOut, bOut.length);
            var inT = new List<float>(); var inF = new List<Dictionary<string, float>>();
            for (int i = 0; i < n; i++)
            {
                float u = (float)i / (n - 1);
                float s = u * u * (3f - 2f * u);
                float wB = s + 0.18f * Mathf.Sin(Mathf.PI * Mathf.Clamp01((u - 0.35f) / 0.65f));
                var fr = Lerp(idleIn, g0, wB);
                SetFootKeys(fr, idleIn, g0, s, hs);
                inT.Add(inLen * u); inF.Add(fr);
            }
            inF[n - 1] = new Dictionary<string, float>(g0);

            var outT = new List<float>(); var outFrames = new List<Dictionary<string, float>>();
            for (int i = 0; i < n; i++)
            {
                float u = (float)i / (n - 1);
                float s = u * u * (3f - 2f * u);
                var fr = Lerp(g0, idleOut, s);
                SetFootKeys(fr, g0, idleOut, s, hs);
                outT.Add(outLen * u); outFrames.Add(fr);
            }
            outFrames[n - 1] = new Dictionary<string, float>(idleOut);

            WriteClip(guard, times, frames);
            WriteClip(cin, inT, inF);
            WriteClip(cout, outT, outFrames);
            var stg = AnimationUtility.GetAnimationClipSettings(guard);
            stg.loopTime = true; AnimationUtility.SetAnimationClipSettings(guard, stg);
            AssetDatabase.SaveAssets();

            sb.AppendLine("CROUCH " + guard.length.ToString("F2") + "초 / IN " + cin.length.ToString("F2") + " / OUT " + cout.length.ToString("F2"));
            sb.AppendLine("새 동작 골반 " + cHigh(cHipY) + " → 적용 " + targetHip.ToString("F3") + " (예전 0.502) / lift " + lift.ToString("F4"));
            sb.AppendLine(lastInfo);
        }
        finally { Object.DestroyImmediate(go); }

        sb.Append(HanbokBlockV2Gen.Level("CROUCH_IN", Dir + "Gen_CROUCH_IN.anim", 2));
        sb.Append(HanbokBlockV2Gen.Level("CROUCH_OUT", Dir + "Gen_CROUCH_OUT.anim", 2));
        return sb.ToString();
    }

    static string cHigh(float v) { return v.ToString("F3"); }
}
