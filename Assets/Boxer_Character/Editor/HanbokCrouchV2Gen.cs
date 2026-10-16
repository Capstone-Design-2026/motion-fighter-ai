using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

// 앉기 계열 2차:
//  GenerateCrouchBlock() : 앉은 하체·상체 기울기·고개는 그대로, 팔만 새 Block(BLOCK2) 가드로 + 좌우 흔들림
//  AddSwayToCrouch()     : 그냥 앉기(CROUCH)에도 좌우 흔들림만 살짝 추가
public static class HanbokCrouchV2Gen
{
    const string Dir = "Assets/Boxer_Character/Animations/";

    // ---- 조절값 ----
    public static float swayX  = 0.23f;   // 좌우 흔들림 비율 (BLOCK2 원본 대비)
    public static float swayZ  = 0.12f;   // 앞뒤 흔들림 비율
    public static float bounce = 0.015f;  // 위아래 바운스(m, 진폭이 아니라 전체 폭)
    public static float lift   = 0.0048f; // 앉아서 막기 바닥 높이 보정
    public static float crouchSwayAmp = 0.010f;  // 그냥 앉기의 좌우 흔들림(±m)
    public static float crouchLift    = 0.0128f; // 그냥 앉기의 바닥 높이 보정
    public static string srcState = "TMP_BLOCK2";
    public static float armLiftDeg = 26f;   // 앉은 상체 기울기만큼 팔을 들어올려 가드 높이 보정
    public static string lastInfo = "";

    // FootQ(발 목표회전)에는 아바타 고유의 보정각이 곱해져 있다. 빠뜨리면 발끝이 위로 들린다.
    // 잘 만들어진 앉기 클립 한 프레임에서 직접 구해 둔다.
    static Quaternion qFixL = Quaternion.identity, qFixR = Quaternion.identity;
    static bool qFixDone = false;
    public static void EnsureFootFix(Animator an)
    {
        if (qFixDone) return;
        var c = AssetDatabase.LoadAssetAtPath<AnimationClip>(Dir + "Gen_CROUCH_GUARD.anim");
        if (c == null) return;
        var fr = HanbokBlockCoverGen.Sample(c, 0f);
        HanbokBlockCoverGen.EvalState(an, "CROUCH", 0f, c.length);
        Quaternion rq = Q(fr, "RootQ");
        Quaternion wL = an.GetBoneTransform(HumanBodyBones.LeftFoot).rotation;
        Quaternion wR = an.GetBoneTransform(HumanBodyBones.RightFoot).rotation;
        qFixL = Quaternion.Inverse(Quaternion.Inverse(rq) * wL) * Q(fr, "LeftFootQ");
        qFixR = Quaternion.Inverse(Quaternion.Inverse(rq) * wR) * Q(fr, "RightFootQ");
        qFixDone = true;
    }

    static bool IsLeg(string m)
    {
        return m.StartsWith("Left Upper Leg") || m.StartsWith("Left Leg") || m.StartsWith("Left Foot") || m.StartsWith("Left Toes")
            || m.StartsWith("Right Upper Leg") || m.StartsWith("Right Leg") || m.StartsWith("Right Foot") || m.StartsWith("Right Toes");
    }
    // 팔(어깨·팔·팔뚝·손·손가락)만 새 가드에서 가져온다
    static bool IsArm(string m)
    {
        if (IsLeg(m)) return false;
        if (m.StartsWith("Spine") || m.StartsWith("Chest") || m.StartsWith("UpperChest")) return false;
        if (m.StartsWith("Neck") || m.StartsWith("Head") || m.StartsWith("Jaw")) return false;
        if (m.Contains("Eye")) return false;
        return true;
    }

    static void SetV(Dictionary<string, float> f, string k, Vector3 v) { f[k + ".x"] = v.x; f[k + ".y"] = v.y; f[k + ".z"] = v.z; }
    static void SetQ(Dictionary<string, float> f, string k, Quaternion q) { f[k + ".x"] = q.x; f[k + ".y"] = q.y; f[k + ".z"] = q.z; f[k + ".w"] = q.w; }
    static Quaternion Q(Dictionary<string, float> f, string k) { return new Quaternion(f[k + ".x"], f[k + ".y"], f[k + ".z"], f[k + ".w"]); }

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
        foreach (var k in a.Keys) o[k] = Mathf.LerpUnclamped(a[k], b[k], t);
        return o;
    }

    // 자세를 굽고 발 IK 목표를 고정 발자리로 쓰는 공통 처리
    static Dictionary<string, float> Bake(Animator an, HumanPoseHandler h, HumanPose mix,
        Dictionary<string, float> template, Quaternion baseRootQ,
        Vector3 lAnkW, Vector3 rAnkW, Quaternion lFq, Quaternion rFq)
    {
        h.SetHumanPose(ref mix);
        // 앉아서 숙인 상체 때문에 내려간 팔을 들어올림 (어깨 관절 기준 회전)
        if (Mathf.Abs(armLiftDeg) > 0.01f)
        {
            var axis = Vector3.right;   // 캐릭터 좌우축
            var rot = Quaternion.AngleAxis(-armLiftDeg, axis);
            foreach (var bone in new[] { HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder })
            {
                var b2 = an.GetBoneTransform(bone);
                if (b2 == null) b2 = an.GetBoneTransform(bone == HumanBodyBones.LeftShoulder ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
                if (b2 != null) b2.rotation = rot * b2.rotation;
            }
        }
        // 다리를 발자리에 붙임
        var lf = an.GetBoneTransform(HumanBodyBones.LeftFoot);
        var rf = an.GetBoneTransform(HumanBodyBones.RightFoot);
        var lt = an.GetBoneTransform(HumanBodyBones.LeftToes);
        var rt = an.GetBoneTransform(HumanBodyBones.RightToes);
        Quaternion ltq = lt ? lt.rotation : Quaternion.identity, rtq = rt ? rt.rotation : Quaternion.identity;
        Vector3 O = Vector3.forward, side = Vector3.Cross(Vector3.up, O).normalized;
        TwoBone(an.GetBoneTransform(HumanBodyBones.LeftUpperLeg), an.GetBoneTransform(HumanBodyBones.LeftLowerLeg), lf, lAnkW, O * 1.0f - side * 0.35f);
        TwoBone(an.GetBoneTransform(HumanBodyBones.RightUpperLeg), an.GetBoneTransform(HumanBodyBones.RightLowerLeg), rf, rAnkW, O * 0.8f + side * 0.3f);
        lf.rotation = lFq; rf.rotation = rFq;
        if (lt) lt.rotation = ltq; if (rt) rt.rotation = rtq;

        var hp = new HumanPose(); h.GetHumanPose(ref hp);
        var outF = new Dictionary<string, float>(template);
        var names = HumanTrait.MuscleName;
        for (int k = 0; k < names.Length; k++)
        {
            string key = HanbokBlockCoverGen.Key(names[k]);
            if (outF.ContainsKey(key)) outF[key] = hp.muscles[k];
        }
        outF["Jaw Close"] = 0; outF["Jaw Left-Right"] = 0;
        SetV(outF, "RootT", hp.bodyPosition); SetQ(outF, "RootQ", hp.bodyRotation);
        float fb = an.leftFeetBottomHeight;
        for (int s = 0; s < 2; s++)
        {
            string sideName = s == 0 ? "Left" : "Right";
            Vector3 gw = (s == 0 ? lAnkW : rAnkW) - Vector3.up * fb;
            SetV(outF, sideName + "FootT", Quaternion.Inverse(hp.bodyRotation) * (gw - hp.bodyPosition) / an.humanScale);
            SetQ(outF, sideName + "FootQ", Quaternion.Inverse(hp.bodyRotation) * (s == 0 ? lFq : rFq) * (s == 0 ? qFixL : qFixR));
        }
        return outF;
    }

    public static string GenerateCrouchBlock()
    {
        var crouch = AssetDatabase.LoadAssetAtPath<AnimationClip>(Dir + "Gen_CROUCH_GUARD.anim");
        var cIn  = AssetDatabase.LoadAssetAtPath<AnimationClip>(Dir + "Gen_CROUCHBLOCK_IN.anim");
        var cOut = AssetDatabase.LoadAssetAtPath<AnimationClip>(Dir + "Gen_CROUCHBLOCK_OUT.anim");
        AnimationClip src = null;
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(Dir + "Anim_BLOCK2.fbx"))
            if (o is AnimationClip && o.name == "BLOCK2") src = (AnimationClip)o;
        if (src == null || crouch == null) return "CLIP NOT FOUND";

        var guard = Ensure(Dir + "Gen_CROUCHBLOCK_GUARD.anim", crouch);
        AssetDatabase.SaveAssets();

        Animator an; var go = HanbokBlockCoverGen.Spawn(out an);
        var sb = new System.Text.StringBuilder();
        try
        {
            var h = new HumanPoseHandler(an.avatar, go.transform);
            var names = HumanTrait.MuscleName;
            var cFrame = HanbokBlockCoverGen.Sample(crouch, 0f);

            // 앉기 기준 자세
            EnsureFootFix(an);
            HanbokBlockCoverGen.EvalState(an, "CROUCH", 0f, crouch.length);;
            var cPose = new HumanPose(); h.GetHumanPose(ref cPose);
            Vector3 cLF = an.GetBoneTransform(HumanBodyBones.LeftFoot).position + Vector3.up * lift;
            Vector3 cRF = an.GetBoneTransform(HumanBodyBones.RightFoot).position + Vector3.up * lift;
            Quaternion cLFq = an.GetBoneTransform(HumanBodyBones.LeftFoot).rotation;
            Quaternion cRFq = an.GetBoneTransform(HumanBodyBones.RightFoot).rotation;
            Quaternion baseQ = Q(cFrame, "RootQ");

            float sl = src.length;
            HanbokBlockCoverGen.EvalState(an, srcState, 0f, sl);
            Vector3 srcHip0 = an.GetBoneTransform(HumanBodyBones.Hips).position;

            int steps = 40;
            var times = new List<float>(); var frames = new List<Dictionary<string, float>>();
            for (int i = 0; i <= steps; i++)
            {
                float t = sl * i / steps;
                HanbokBlockCoverGen.EvalState(an, srcState, t, sl);
                var sp = new HumanPose(); h.GetHumanPose(ref sp);
                Vector3 d = an.GetBoneTransform(HumanBodyBones.Hips).position - srcHip0;

                var mix = new HumanPose();
                mix.muscles = new float[cPose.muscles.Length];
                for (int k = 0; k < mix.muscles.Length; k++)
                    mix.muscles[k] = IsArm(names[k]) ? sp.muscles[k] : cPose.muscles[k];

                // 흔들림: 좌우·앞뒤는 새 가드에서 비율로, 위아래는 앉기 바운스(5주기)
                float bob = -Mathf.Cos(2f * Mathf.PI * 5f * i / steps) * 0.5f * bounce + 0.5f * bounce;
                Vector3 off = new Vector3(d.x * swayX, 0f, d.z * swayZ) + Vector3.up * (lift + bob);
                mix.bodyPosition = cPose.bodyPosition + off;
                mix.bodyRotation = cPose.bodyRotation;

                times.Add(t);
                frames.Add(Bake(an, h, mix, cFrame, baseQ, cLF, cRF, cLFq, cRFq));
                if (i == 0) lastInfo = "t0 머리 " + an.GetBoneTransform(HumanBodyBones.Head).position.ToString("F3")
                    + " L손 " + an.GetBoneTransform(HumanBodyBones.LeftHand).position.ToString("F3")
                    + " R손 " + an.GetBoneTransform(HumanBodyBones.RightHand).position.ToString("F3");
            }
            frames[steps] = new Dictionary<string, float>(frames[0]);

            // 들어가기/나오기: 앉기 자세 <-> 새 가드 첫 프레임
            var g0 = frames[0];
            var c0 = HanbokBlockCoverGen.Sample(crouch, 0f);
            var inC  = Ensure(Dir + "Gen_CROUCHBLOCK_IN.anim", cIn);
            var outC = Ensure(Dir + "Gen_CROUCHBLOCK_OUT.anim", cOut);
            int nb = 9;
            var inT = new List<float>(); var inFr = new List<Dictionary<string, float>>();
            for (int i = 0; i < nb; i++) {
                float u = (float)i / (nb - 1);
                float w = u * u * (3f - 2f * u);
                w += 0.12f * Mathf.Sin(Mathf.PI * Mathf.Clamp01((u - 0.35f) / 0.65f));
                inT.Add(0.10f * u); inFr.Add(Lerp(c0, g0, w));
            }
            inFr[nb - 1] = new Dictionary<string, float>(g0);
            WriteClip(inC, inT, inFr);
            var ouT = new List<float>(); var ouFr = new List<Dictionary<string, float>>();
            for (int i = 0; i < nb; i++) {
                float u = (float)i / (nb - 1);
                float w = u * u * (3f - 2f * u);
                ouT.Add(0.20f * u); ouFr.Add(Lerp(g0, c0, w));
            }
            ouFr[nb - 1] = new Dictionary<string, float>(c0);
            WriteClip(outC, ouT, ouFr);

            WriteClip(guard, times, frames);
            var stg = AnimationUtility.GetAnimationClipSettings(guard);
            stg.loopTime = true; AnimationUtility.SetAnimationClipSettings(guard, stg);
            AssetDatabase.SaveAssets();
            sb.AppendLine("CROUCHBLOCK " + guard.length.ToString("F2") + "초  " + lastInfo);
        }
        finally { Object.DestroyImmediate(go); }
        sb.Append(HanbokBlockV2Gen.Level("CROUCHBLOCK_IN", Dir + "Gen_CROUCHBLOCK_IN.anim", 2));
        sb.Append(HanbokBlockV2Gen.Level("CROUCHBLOCK_OUT", Dir + "Gen_CROUCHBLOCK_OUT.anim", 2));
        return sb.ToString();
    }

    // 그냥 앉기에 좌우 흔들림만 추가 (기존 바운스·자세는 유지)
    public static string AddSwayToCrouch()
    {
        // ⚠ 앉기는 이제 HanbokCrouchV3Gen.Generate()가 만듭니다(흔들림 포함). 이걸 돌리면 옆 백업으로 덮어써서 새 앉기가 날아갑니다.
        if (true) return "AddSwayToCrouch는 더 이상 쓰지 않음 — HanbokCrouchV3Gen.Generate() 사용";
        var crouch = AssetDatabase.LoadAssetAtPath<AnimationClip>(Dir + "Gen_CROUCH_GUARD.anim");
        var backup = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Boxer_Character/Backups/0930_before_block_new/Gen_CROUCH_GUARD.anim");
        if (backup == null) return "BACKUP NOT FOUND";
        var guard = Ensure(Dir + "Gen_CROUCH_GUARD.anim", backup);
        AssetDatabase.SaveAssets();

        Animator an; var go = HanbokBlockCoverGen.Spawn(out an);
        var sb = new System.Text.StringBuilder();
        try
        {
            var h = new HumanPoseHandler(an.avatar, go.transform);
            float len = backup.length;
            int steps = 24;
            var times = new List<float>(); var frames = new List<Dictionary<string, float>>();
            // 원래 발 자리(0초 기준)
            EnsureFootFix(an);
            HanbokBlockCoverGen.EvalState(an, "CROUCH", 0f, len);;
            Vector3 cLF = an.GetBoneTransform(HumanBodyBones.LeftFoot).position + Vector3.up * crouchLift;
            Vector3 cRF = an.GetBoneTransform(HumanBodyBones.RightFoot).position + Vector3.up * crouchLift;
            Quaternion cLFq = an.GetBoneTransform(HumanBodyBones.LeftFoot).rotation;
            Quaternion cRFq = an.GetBoneTransform(HumanBodyBones.RightFoot).rotation;

            for (int i = 0; i <= steps; i++)
            {
                float t = len * i / steps;
                var fr = HanbokBlockCoverGen.Sample(backup, t);
                HanbokBlockCoverGen.EvalState(an, "CROUCH", t, len);
                var sp = new HumanPose(); h.GetHumanPose(ref sp);
                float ph = 2f * Mathf.PI * i / steps;               // 한 주기 = 클립 길이 (매끄럽게 반복)
                Vector3 off = new Vector3(Mathf.Sin(ph) * crouchSwayAmp, 0f, Mathf.Sin(ph * 2f) * crouchSwayAmp * 0.4f);
                sp.bodyPosition += off;
                times.Add(t);
                frames.Add(Bake(an, h, sp, fr, Q(fr, "RootQ"), cLF, cRF, cLFq, cRFq));
            }
            frames[steps] = new Dictionary<string, float>(frames[0]);
            WriteClip(guard, times, frames);
            var stg = AnimationUtility.GetAnimationClipSettings(guard);
            stg.loopTime = true; AnimationUtility.SetAnimationClipSettings(guard, stg);
            AssetDatabase.SaveAssets();
            sb.AppendLine("CROUCH 좌우 흔들림 추가: ±" + (crouchSwayAmp * 100).ToString("F1") + "cm");
        }
        finally { Object.DestroyImmediate(go); }
        return sb.ToString();
    }
}
