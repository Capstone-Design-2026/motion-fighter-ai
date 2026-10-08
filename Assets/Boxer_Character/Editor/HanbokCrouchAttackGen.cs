using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

// 앉아서 공격 생성기: 앉기 자세(하체·골반) 위에 공격 동작을 '덧붙여' 새 클립을 만든다.
// 덧붙이기(additive): out = 앉기자세 + (공격클립(t) - 공격클립(0))
//  -> 첫 프레임과 끝 프레임이 앉기 자세와 정확히 같아져서 들어가고 나올 때 튀지 않는다.
public static class HanbokCrouchAttackGen
{
    const string Dir = "Assets/Boxer_Character/Animations/";
    public static float lift = 0.013f;
    public static string lastInfo = "";

    // 근육 이름 분류
    static bool IsLeftLeg(string m)  { return m.StartsWith("Left Upper Leg") || m.StartsWith("Left Leg") || m.StartsWith("Left Foot") || m.StartsWith("Left Toes"); }
    static bool IsRightLeg(string m) { return m.StartsWith("Right Upper Leg") || m.StartsWith("Right Leg") || m.StartsWith("Right Foot") || m.StartsWith("Right Toes"); }
    static bool IsLeg(string m) { return IsLeftLeg(m) || IsRightLeg(m); }

    // 차는 발이 바닥을 뚫지 않게 올릴 최소 높이(발목 기준)
    public static float minKickAnkleY = 0.075f;
    // true면 오른다리(뒷다리)로 참 — 앉은 자세에서는 뒷다리가 자연스럽다
    public static bool kickRight = false;

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
            {
                float v = frames[i].ContainsKey(b.propertyName) ? frames[i][b.propertyName] : 0f;
                keys[i] = new Keyframe(times[i], v);
            }
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

    // srcState: 소스 클립을 넣어 둔 임시 Animator 상태 이름
    // kickLeftLeg: true면 왼다리도 소스에서 가져옴(하단 킥), false면 두 다리 모두 앉기 그대로(상체 공격)
    // 섞임 가중치: 0(앉기) -> 1(공격) -> 0(앉기)
    public static float tIn = 0.25f, tOut = 0.80f;
    static float W(float u)
    {
        float w;
        if (u <= 0f || u >= 1f) w = 0f;
        else if (u < tIn) w = u / tIn;
        else if (u > tOut) w = (1f - u) / (1f - tOut);
        else w = 1f;
        return w * w * (3f - 2f * w); // 부드럽게
    }

    public static string Generate(string srcState, string srcFbx, string srcClipName, bool kickLeftLeg, string outName, int steps)
    {
        var crouch = AssetDatabase.LoadAssetAtPath<AnimationClip>(Dir + "Gen_CROUCH_GUARD.anim");
        AnimationClip src = null;
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(Dir + srcFbx))
            if (o is AnimationClip && o.name == srcClipName) src = (AnimationClip)o;
        if (src == null) return "SOURCE CLIP NOT FOUND";

        var outClip = Ensure(Dir + outName + ".anim", crouch);
        AssetDatabase.SaveAssets();

        Animator an; var go = HanbokBlockCoverGen.Spawn(out an);
        var sb = new System.Text.StringBuilder();
        try
        {
            var h = new HumanPoseHandler(an.avatar, go.transform);
            var names = HumanTrait.MuscleName;

            // 1) 앉기 기준 자세(0초)의 근육값 + 발 월드 위치
            HanbokBlockCoverGen.EvalState(an, "CROUCH", 0f, crouch.length);
            var crouchPose = new HumanPose(); h.GetHumanPose(ref crouchPose);
            var cFrame = HanbokBlockCoverGen.Sample(crouch, 0f);
            Vector3 cLF = an.GetBoneTransform(HumanBodyBones.LeftFoot).position;
            Vector3 cRF = an.GetBoneTransform(HumanBodyBones.RightFoot).position;
            Quaternion cbq = Q(cFrame, "RootQ");

            // 2) 소스 0초 근육값 (덧붙이기 기준)
            HanbokBlockCoverGen.EvalState(an, srcState, 0f, src.length);
            var srcBase = new HumanPose(); h.GetHumanPose(ref srcBase);

            var times = new List<float>(); var frames = new List<Dictionary<string, float>>();
            for (int i = 0; i <= steps; i++)
            {
                float t = src.length * i / steps;
                HanbokBlockCoverGen.EvalState(an, srcState, t, src.length);
                var sp = new HumanPose(); h.GetHumanPose(ref sp);

                // 3) 합치기
                float w = W(src.length <= 0 ? 0 : t / src.length);
                var mix = new HumanPose();
                mix.muscles = new float[crouchPose.muscles.Length];
                for (int k = 0; k < mix.muscles.Length; k++)
                {
                    string m = names[k];
                    float c = crouchPose.muscles[k];
                    if (IsLeg(m))
                    {
                        bool thisLegKicks = kickLeftLeg && (kickRight ? IsRightLeg(m) : IsLeftLeg(m));
                        mix.muscles[k] = thisLegKicks ? Mathf.Lerp(c, sp.muscles[k], w) : c;
                        continue;
                    }
                    if (m.StartsWith("Spine") || m.StartsWith("Chest") || m.StartsWith("UpperChest") || m.StartsWith("Neck") || m.StartsWith("Head") || m.StartsWith("Jaw") || m.StartsWith("Left Eye") || m.StartsWith("Right Eye"))
                    {   // 몸통·고개는 앉기 자세를 유지하고 '움직인 만큼만' 덧붙임
                        mix.muscles[k] = Mathf.Clamp(c + w * (sp.muscles[k] - srcBase.muscles[k]), -1f, 1f);
                        continue;
                    }
                    // 팔·손은 공격 동작이 그대로 가져감
                    mix.muscles[k] = Mathf.Lerp(c, sp.muscles[k], w);
                }
                mix.bodyPosition = crouchPose.bodyPosition + Vector3.up * (lift / an.humanScale);
                mix.bodyRotation = crouchPose.bodyRotation;
                h.SetHumanPose(ref mix);

                // 3-b) 차는 발이 바닥을 뚫으면 발목을 바닥 위로 끌어올린다(땅을 쓸듯이)
                if (kickLeftLeg)
                {
                    var lf = an.GetBoneTransform(kickRight ? HumanBodyBones.RightFoot : HumanBodyBones.LeftFoot);
                    if (lf.position.y < minKickAnkleY)
                    {
                        Vector3 tgt = lf.position; tgt.y = minKickAnkleY;
                        Quaternion keep = lf.rotation;
                        TwoBone(an.GetBoneTransform(kickRight ? HumanBodyBones.RightUpperLeg : HumanBodyBones.LeftUpperLeg),
                                an.GetBoneTransform(kickRight ? HumanBodyBones.RightLowerLeg : HumanBodyBones.LeftLowerLeg), lf, tgt,
                                Vector3.forward * 1.0f + Vector3.up * 0.2f);
                        lf.rotation = keep;
                        var fixedPose = new HumanPose(); h.GetHumanPose(ref fixedPose);
                        mix.muscles = fixedPose.muscles;
                        mix.bodyPosition = fixedPose.bodyPosition;
                        mix.bodyRotation = fixedPose.bodyRotation;
                        h.SetHumanPose(ref mix);
                    }
                }

                // 4) 발 IK 목표: 디딤발은 앉기 자리에 고정, 차는 발은 지금 위치 그대로
                var outF = new Dictionary<string, float>(cFrame);
                for (int k = 0; k < names.Length; k++)
                {
                    string key = HanbokBlockCoverGen.Key(names[k]);
                    if (outF.ContainsKey(key)) outF[key] = mix.muscles[k];
                }
                outF["Jaw Close"] = 0; outF["Jaw Left-Right"] = 0;
                SetV(outF, "RootT", mix.bodyPosition); SetQ(outF, "RootQ", mix.bodyRotation);

                float fb = an.leftFeetBottomHeight;
                foreach (var s in new[] { "Left", "Right" })
                {
                    bool isKicker = kickLeftLeg && (kickRight ? s == "Right" : s == "Left");
                    var fbone = an.GetBoneTransform(s == "Left" ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
                    Vector3 ankW = isKicker ? fbone.position : ((s == "Left" ? cLF : cRF) + Vector3.up * lift);
                    Vector3 gw = ankW - Vector3.up * fb;
                    Quaternion gq = isKicker
                        ? Quaternion.Inverse(mix.bodyRotation) * fbone.rotation
                        : Quaternion.Inverse(mix.bodyRotation) * cbq * Q(cFrame, s + "FootQ");
                    SetV(outF, s + "FootT", Quaternion.Inverse(mix.bodyRotation) * (gw - mix.bodyPosition) / an.humanScale);
                    SetQ(outF, s + "FootQ", gq);
                }
                times.Add(t); frames.Add(outF);

                if (i == steps / 3)
                    lastInfo = "t" + t.ToString("F2") + " LH " + an.GetBoneTransform(HumanBodyBones.LeftHand).position.ToString("F3")
                             + " LF " + an.GetBoneTransform(HumanBodyBones.LeftFoot).position.ToString("F3")
                             + " hips " + an.GetBoneTransform(HumanBodyBones.Hips).position.ToString("F3");
            }
            // 끝 프레임을 첫 프레임과 똑같이
            frames[steps] = new Dictionary<string, float>(frames[0]);
            WriteClip(outClip, times, frames);
            // 반복 끔
            var st = AnimationUtility.GetAnimationClipSettings(outClip);
            st.loopTime = false; AnimationUtility.SetAnimationClipSettings(outClip, st);
            AssetDatabase.SaveAssets();
            sb.AppendLine(outName + " len " + outClip.length.ToString("F2") + "  " + lastInfo);
        }
        finally { Object.DestroyImmediate(go); }
        return sb.ToString();
    }
}
