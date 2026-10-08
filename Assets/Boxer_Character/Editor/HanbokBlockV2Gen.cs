using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

// 막기 2차: 하체·스탠스(런지)는 지금 것 그대로 두고, 상체·팔·고개만 새 Mixamo Block(BLOCK2)으로 교체.
// 흔들림도 BLOCK2의 실제 움직임에서 비율만 줄여 가져온다(좌우·앞뒤 살짝 + 위아래).
public static class HanbokBlockV2Gen
{
    const string Dir = "Assets/Boxer_Character/Animations/";
    const string Base = "Assets/Boxer_Character/Backups/0930_before_block_new/";

    // ---- 조절값 ----
    public static float swayScale = 0.35f;   // 좌우·앞뒤 흔들림 비율 (BLOCK2 원본 대비)
    public static float bobScale  = 0.30f;   // 위아래 바운스 비율
        public static float lift      = 0.0101f; // 바닥 높이 보정 (자세 바꾸면 다시 맞출 것)
    public static int   steps     = 40;      // 유지 클립 키프레임 수
    public static string srcState = "TMP_BLOCK2";
        public static float backFootBack = 0.12f;  // 뒷발을 뒤로(m) — 뒷걸음질이 닿는 자리 = 가드 보폭
    public static float backFootOut  = 0.02f;  // 뒷발을 바깥으로(m)
        public static float hipFwd       = 0f;     // 골반을 상대 쪽으로(m)
        public static float hipDrop      = 0.050f;  // 발은 그대로 두고 골반만 내림 (더 줄이면 다리가 닿지 않아 발이 미끄러짐)
    public static bool  useLegIK     = true;    public static bool  usePoleFromFoot = true;  // 무릎 방향을 '그 발이 향한 쪽'으로 잡기
    public static float poleBlend    = 0.25f;   // 발 방향 <-> 상대 방향 섞기(0=발 방향 그대로)
    public static Vector3 frontPole  = new Vector3(0f, 0f, 1f);   // usePoleFromFoot=false 일 때만
    public static Vector3 rearPole   = new Vector3(0.3f, 0f, 1f);
    public static float inLen        = 0.20f;   // 들어가기 길이(뒷걸음질 포함)
    public static float outLen       = 0.28f;   // 빠져나오기 길이
    public static float stepRaise    = 0.035f;  // 내딘는 동안 뒷발이 뜨는 높이(m)
    public static float footSag      = 0f;      // (구방식 보정 — 지금은 Level이 대신함) 양(m)
    public static float inOver       = 0.25f;   // 들어갈 때 살짝 지나치는 정도
    public static float footLead     = 0.80f;   // 발이 제자리에 닿는 시점(클립 비율)
    public static bool  anchorToIdleFeet = true;
    public static string idleState   = "IDLE";   // 발자리를 가져올 상태
    public static float  idleLen     = 2.37f;
    public static Vector3 footFix = new Vector3(-0.010f, 0f, -0.001f); // 가드 스탠스를 IDLE 앞발 자리에 맞추는 보정(측정값)

 // 가드 발자리를 IDLE 발자리에 맞춤(앞발 고정 + 뒷발만 뒤로)
  // 내딛는 동안 뒷발이 뜨는 높이(m)
   // 다리를 2본 IK로 발자리에 강제로 붙일지  // 골반을 상대 쪽으로(m)
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
        foreach (var k in a.Keys) o[k] = Mathf.LerpUnclamped(a[k], b[k], t);
        return o;
    }

    // 발 키만 따로 섞는다. w=0 이면 a의 발자리, w=1 이면 b의 발자리.
    // raiseR 만큼 뒷발(오른발)을 world 위로 띄워 '내딛는' 느낌을 만든다.
    // 발 키만 따로 섞는다. 월드 좌표로 섞은 뒤 그 프레임의 몸 기준으로 되돌린다.
    // (로컬값을 그냥 섞으면 몸이 움직일 때 발이 따라 끌려간다)
    static void SetFootKeys(Dictionary<string, float> f, Dictionary<string, float> a, Dictionary<string, float> b, float w, float raiseR, float hs)
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
            // 발꿈치를 든 IDLE 발 → 평평한 가드 발로 바뀔 때 중간에서 발바닥이 뜨는 것을 누른다
            if (s == 0) ww.y -= footSag * 4f * cw * (1f - cw);
            if (s == 1 && raiseR != 0f) ww += Vector3.up * raiseR;
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
        var baseGuard = AssetDatabase.LoadAssetAtPath<AnimationClip>(Base + "Gen_BLOCK_GUARD.anim");
        var baseIn    = AssetDatabase.LoadAssetAtPath<AnimationClip>(Base + "Gen_BLOCK_IN.anim");
        var baseOut   = AssetDatabase.LoadAssetAtPath<AnimationClip>(Base + "Gen_BLOCK_OUT.anim");
        if (baseGuard == null) return "BASE CLIP NOT FOUND";

        AnimationClip src = null;
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(Dir + "Anim_BLOCK2.fbx"))
            if (o is AnimationClip && o.name == "BLOCK2") src = (AnimationClip)o;
        if (src == null) return "BLOCK2 NOT FOUND";

        var guard = Ensure(Dir + "Gen_BLOCK_GUARD.anim", baseGuard);
        var cin   = Ensure(Dir + "Gen_BLOCK_IN.anim",   baseIn);
        var cout  = Ensure(Dir + "Gen_BLOCK_OUT.anim",  baseOut);
        AssetDatabase.SaveAssets();

        Animator an; var go = HanbokBlockCoverGen.Spawn(out an);
        var sb = new System.Text.StringBuilder();
        try
        {
            var h = new HumanPoseHandler(an.avatar, go.transform);
            var names = HumanTrait.MuscleName;
            var bFrame = HanbokBlockCoverGen.Sample(baseGuard, 0f);
            float sl = src.length;
            float hs = an.humanScale;
            float fbH = an.leftFeetBottomHeight;

            var idleStart = HanbokBlockCoverGen.Sample(baseIn, 0f);
            var idleEnd   = HanbokBlockCoverGen.Sample(baseOut, baseOut.length);

            // ---- IDLE 발자리(서 있을 때 발이 실제로 있는 곳) ----
            HanbokBlockCoverGen.EvalState(an, idleState, 0f, idleLen);
            Vector3 iLF = an.GetBoneTransform(HumanBodyBones.LeftFoot).position;
            Vector3 iRF = an.GetBoneTransform(HumanBodyBones.RightFoot).position;
            Quaternion iLFq = an.GetBoneTransform(HumanBodyBones.LeftFoot).rotation;
            Quaternion iRFq = an.GetBoneTransform(HumanBodyBones.RightFoot).rotation;
            var iLt = an.GetBoneTransform(HumanBodyBones.LeftToes);
            var iRt = an.GetBoneTransform(HumanBodyBones.RightToes);
            Vector3 iToeL = iLt != null ? Vector3.ProjectOnPlane(iLt.position - iLF, Vector3.up) : Vector3.forward;
            Vector3 iToeR = iRt != null ? Vector3.ProjectOnPlane(iRt.position - iRF, Vector3.up) : Vector3.forward;
            float idleHip = an.GetBoneTransform(HumanBodyBones.Hips).position.y;
            float idleStanceW = Vector3.ProjectOnPlane(iLF - iRF, Vector3.up).magnitude;

            // FootQ(발 목표회전)는 월드회전을 몸회전으로 나눈 값이 아니라 아바타 고유의 보정각이 곱해져 있다.
            // 그 보정각을 IDLE 한 프레임에서 직접 구해 둔다. (빠뜨리면 발끝이 위로 들림)
            Quaternion qFixL = Quaternion.identity, qFixR = Quaternion.identity;
            {
                HanbokBlockCoverGen.EvalState(an, "BLOCK_IN", 0f, cin.length);
                Quaternion rqRef = Q(idleStart, "RootQ");
                Quaternion wL = an.GetBoneTransform(HumanBodyBones.LeftFoot).rotation;
                Quaternion wR = an.GetBoneTransform(HumanBodyBones.RightFoot).rotation;
                qFixL = Quaternion.Inverse(Quaternion.Inverse(rqRef) * wL) * Q(idleStart, "LeftFootQ");
                qFixR = Quaternion.Inverse(Quaternion.Inverse(rqRef) * wR) * Q(idleStart, "RightFootQ");
                HanbokBlockCoverGen.EvalState(an, idleState, 0f, idleLen);
            }

            HanbokBlockCoverGen.EvalState(an, srcState, 0f, sl);
            var basePose = new HumanPose(); h.GetHumanPose(ref basePose);
            Vector3 b2LF = an.GetBoneTransform(HumanBodyBones.LeftFoot).position;
            Vector3 b2RF = an.GetBoneTransform(HumanBodyBones.RightFoot).position;
            Quaternion b2LFq = an.GetBoneTransform(HumanBodyBones.LeftFoot).rotation;
            Quaternion b2RFq = an.GetBoneTransform(HumanBodyBones.RightFoot).rotation;
            Vector3 hipBase = an.GetBoneTransform(HumanBodyBones.Hips).position;
            Vector3 O = Vector3.forward;
            Vector3 sideOut = Vector3.Cross(Vector3.up, O).normalized;

            // 발이 향한 방향을 '발 기준 로컬'로 기억해 둔다 (발을 옮겨도 따라오게)
            var lToe0 = an.GetBoneTransform(HumanBodyBones.LeftToes);
            var rToe0 = an.GetBoneTransform(HumanBodyBones.RightToes);
            Vector3 toeLocL = lToe0 != null ? Quaternion.Inverse(b2LFq) * (lToe0.position - b2LF) : Quaternion.Inverse(b2LFq) * O * 0.1f;
            Vector3 toeLocR = rToe0 != null ? Quaternion.Inverse(b2RFq) * (rToe0.position - b2RF) : Quaternion.Inverse(b2RFq) * O * 0.1f;

            // ---- 가드 발자리 정하기 ----
            Vector3 bLF, bRF; Quaternion bLFq, bRFq; Vector3 bodyShift = Vector3.zero;
            if (anchorToIdleFeet)
            {
                Quaternion rqI = Q(idleStart, "RootQ"); Vector3 rtI = V(idleStart, "RootT");
                Vector3 gwL = rtI + rqI * (V(idleStart, "LeftFootT") * hs);
                Vector3 gwR = rtI + rqI * (V(idleStart, "RightFootT") * hs);
                bLFq = rqI * Q(idleStart, "LeftFootQ");
                bRFq = rqI * Q(idleStart, "RightFootQ");
                bLF = gwL + Vector3.up * fbH;                                            // 앞발 = IDLE 그대로
                bRF = gwR + Vector3.up * fbH - O * backFootBack + sideOut * backFootOut; // 뒷발만 뒤로 내딛음
                Vector3 c2 = (b2LF + b2RF) * 0.5f, ct = (bLF + bRF) * 0.5f;
                bodyShift = new Vector3(ct.x - c2.x, 0f, ct.z - c2.z);
            }
            else
            {
                bLF = b2LF; bLFq = b2LFq; bRFq = b2RFq;
                bRF = b2RF - O * backFootBack + sideOut * backFootOut;
            }

            // 무릎이 향할 방향 = 그 발이 실제로 향한 쪽
            Vector3 poleL = frontPole.normalized, poleR = rearPole.normalized;
            if (usePoleFromFoot)
            {
                Vector3 dl = Vector3.ProjectOnPlane(bLFq * toeLocL, Vector3.up);
                Vector3 dr = Vector3.ProjectOnPlane(bRFq * toeLocR, Vector3.up);
                if (dl.sqrMagnitude > 1e-6f) poleL = Vector3.Slerp(dl.normalized, O, poleBlend).normalized;
                if (dr.sqrMagnitude > 1e-6f) poleR = Vector3.Slerp(dr.normalized, O, poleBlend).normalized;
            }

            // 가드 발자리를 IDLE 발자리에 맞춤 — 앞발은 그대로, 뒷발만 뒤로 내딛는다
            if (anchorToIdleFeet)
            {
                bLF = iLF; bRF = iRF - O * backFootBack + sideOut * backFootOut;
                bLF += footFix; bRF += footFix;
                bLFq = iLFq; bRFq = iRFq;
                Vector3 c2b = (b2LF + b2RF) * 0.5f, ctb = (bLF + bRF) * 0.5f;
                bodyShift = new Vector3(ctb.x - c2b.x, 0f, ctb.z - c2b.z);
                if (usePoleFromFoot)
                {
                    if (iToeL.sqrMagnitude > 1e-6f) poleL = Vector3.Slerp(iToeL.normalized, O, poleBlend).normalized;
                    if (iToeR.sqrMagnitude > 1e-6f) poleR = Vector3.Slerp(iToeR.normalized, O, poleBlend).normalized;
                }
            }
            var times = new List<float>(); var frames = new List<Dictionary<string, float>>();
            float minSway = 99, maxSway = -99, minBob = 99, maxBob = -99;
            for (int i = 0; i <= steps; i++)
            {
                float t = sl * i / steps;
                HanbokBlockCoverGen.EvalState(an, srcState, t, sl);
                var sp = new HumanPose(); h.GetHumanPose(ref sp);
                Vector3 srcHip = an.GetBoneTransform(HumanBodyBones.Hips).position;
                Vector3 d = srcHip - hipBase;

                var mix = new HumanPose();
                mix.muscles = new float[basePose.muscles.Length];
                for (int k = 0; k < mix.muscles.Length; k++)
                    mix.muscles[k] = IsLeg(names[k])
                        ? basePose.muscles[k] + (sp.muscles[k] - basePose.muscles[k]) * swayScale
                        : sp.muscles[k];

                Vector3 off = new Vector3(d.x * swayScale, d.y * bobScale, d.z * swayScale)
                            + Vector3.up * (lift - hipDrop) + O * hipFwd + bodyShift;
                mix.bodyPosition = basePose.bodyPosition + off;
                mix.bodyRotation = sp.bodyRotation;
                h.SetHumanPose(ref mix);

                if (useLegIK)
                {
                    var lfT = an.GetBoneTransform(HumanBodyBones.LeftFoot);
                    var rfT = an.GetBoneTransform(HumanBodyBones.RightFoot);
                    var lTo = an.GetBoneTransform(HumanBodyBones.LeftToes);
                    var rTo = an.GetBoneTransform(HumanBodyBones.RightToes);
                    Quaternion kl = bLFq, kr = bRFq;
                    Quaternion klt = lTo ? lTo.rotation : Quaternion.identity, krt = rTo ? rTo.rotation : Quaternion.identity;
                    TwoBone(an.GetBoneTransform(HumanBodyBones.LeftUpperLeg), an.GetBoneTransform(HumanBodyBones.LeftLowerLeg), lfT,
                            bLF + Vector3.up * lift, poleL);
                    TwoBone(an.GetBoneTransform(HumanBodyBones.RightUpperLeg), an.GetBoneTransform(HumanBodyBones.RightLowerLeg), rfT,
                            bRF + Vector3.up * lift, poleR);
                    lfT.rotation = kl; rfT.rotation = kr;
                    if (lTo) lTo.rotation = klt; if (rTo) rTo.rotation = krt;
                    var fixedPose = new HumanPose(); h.GetHumanPose(ref fixedPose);
                    mix.muscles = fixedPose.muscles; mix.bodyPosition = fixedPose.bodyPosition; mix.bodyRotation = fixedPose.bodyRotation;
                    h.SetHumanPose(ref mix);
                }

                minSway = Mathf.Min(minSway, off.x); maxSway = Mathf.Max(maxSway, off.x);
                minBob = Mathf.Min(minBob, off.y); maxBob = Mathf.Max(maxBob, off.y);

                var outF = new Dictionary<string, float>(bFrame);
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
                    Vector3 ankW = (s == 0 ? bLF : bRF) + Vector3.up * lift;
                    Quaternion fq = (s == 0 ? bLFq : bRFq);
                    Vector3 gw = ankW - Vector3.up * fbH;
                    SetV(outF, side + "FootT", (Quaternion.Inverse(mix.bodyRotation) * (gw - mix.bodyPosition)) / hs);
                    SetQ(outF, side + "FootQ", Quaternion.Inverse(mix.bodyRotation) * fq * (s == 0 ? qFixL : qFixR));
                }
                times.Add(t); frames.Add(outF);
                if (i == 0) lastInfo = "t0 머리 " + an.GetBoneTransform(HumanBodyBones.Head).position.ToString("F3")
                    + " L손 " + an.GetBoneTransform(HumanBodyBones.LeftHand).position.ToString("F3")
                    + " R손 " + an.GetBoneTransform(HumanBodyBones.RightHand).position.ToString("F3");
            }
            frames[steps] = new Dictionary<string, float>(frames[0]);

            // ---- 들어가기 / 나오기: 앞발은 붙박이, 뒷발만 뒤로 '내딛는' 동작 ----
            var g0 = frames[0];

            // 들어가기: 촘촘한 키(11개)로 만들어 스플라인이 튀지 않게 한다
            int nIn = 11;
            var inT = new List<float>(); var inF = new List<Dictionary<string, float>>();
            for (int i = 0; i < nIn; i++)
            {
                float u = (float)i / (nIn - 1);
                float s = u * u * (3f - 2f * u);
                float wB = s + inOver * Mathf.Sin(Mathf.PI * Mathf.Clamp01((u - 0.35f) / 0.65f));
                float fu = Mathf.Clamp01(u / footLead);
                float wF = fu * fu * (3f - 2f * fu);
                float rs = stepRaise * Mathf.Sin(Mathf.PI * fu);
                var fr = Lerp(idleStart, g0, wB);
                SetFootKeys(fr, idleStart, g0, wF, rs, hs);
                inT.Add(inLen * u); inF.Add(fr);
            }
            inF[nIn - 1] = new Dictionary<string, float>(g0);

            // 빠져나오기: 과하게 지나치지 않고 부드럽게 IDLE로
            int nOut = 11;
            var outT = new List<float>(); var outFrames = new List<Dictionary<string, float>>();
            for (int i = 0; i < nOut; i++)
            {
                float u = (float)i / (nOut - 1);
                float s = u * u * (3f - 2f * u);
                float fu = Mathf.Clamp01(u / footLead);
                float wF = fu * fu * (3f - 2f * fu);
                float rs = stepRaise * 0.8f * Mathf.Sin(Mathf.PI * fu);
                var fr = Lerp(g0, idleEnd, s);
                SetFootKeys(fr, g0, idleEnd, wF, rs, hs);
                outT.Add(outLen * u); outFrames.Add(fr);
            }
            outFrames[nOut - 1] = new Dictionary<string, float>(idleEnd);

            WriteClip(guard, times, frames);
            WriteClip(cin, inT, inF);
            WriteClip(cout, outT, outFrames);

            var stg = AnimationUtility.GetAnimationClipSettings(guard);
            stg.loopTime = true; AnimationUtility.SetAnimationClipSettings(guard, stg);
            AssetDatabase.SaveAssets();

            float stance = Vector3.ProjectOnPlane(bLF - bRF, Vector3.up).magnitude;
            sb.AppendLine("GUARD " + guard.length.ToString("F2") + "초 / IN " + cin.length.ToString("F2") + " / OUT " + cout.length.ToString("F2"));
            sb.AppendLine("흔들림: 좌우 " + ((maxSway - minSway) * 100).ToString("F1") + "cm, 위아래 " + ((maxBob - minBob) * 100).ToString("F1") + "cm");
            sb.AppendLine("보폭 " + (stance * 100).ToString("F0") + "cm / 뒷발 뒤로 " + (backFootBack * 100).ToString("F0") + "cm / 들림 " + (stepRaise * 100).ToString("F1") + "cm / hipDrop " + hipDrop.ToString("F3") + " / lift " + lift.ToString("F3"));
            sb.AppendLine("몸 수평보정 " + bodyShift.ToString("F3") + " / 무릎 pole L " + poleL.ToString("F2") + " R " + poleR.ToString("F2"));
            sb.AppendLine(lastInfo);
        }
        finally { Object.DestroyImmediate(go); }
        sb.Append(Level("BLOCK_IN", Dir + "Gen_BLOCK_IN.anim", 2));
        sb.Append(Level("BLOCK_OUT", Dir + "Gen_BLOCK_OUT.anim", 2));
        return sb.ToString();
    }

    // 클립의 키프레임마다 실제 바닥 높이를 재서 그만큼 몸 전체를 올리고 내린다.
    public static string Level(string stateName, string clipPath, int passes)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
        if (clip == null) return "NO CLIP " + clipPath + "\n";
        var sb = new System.Text.StringBuilder();
        for (int p = 0; p < passes; p++)
        {
            EditorCurveBinding rb = default(EditorCurveBinding); bool found = false;
            foreach (var b in AnimationUtility.GetCurveBindings(clip))
                if (b.propertyName == "RootT.y") { rb = b; found = true; break; }
            if (!found) return "NO RootT.y " + clipPath + "\n";
            var keys = AnimationUtility.GetEditorCurve(clip, rb).keys;
            Animator an; var go = HanbokBlockCoverGen.Spawn(out an);
            float worst = 0f;
            try
            {
                for (int i = 0; i < keys.Length; i++)
                {
                    HanbokBlockCoverGen.EvalState(an, stateName, keys[i].time, clip.length);
                    float minY = 999f;
                    foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
                    {
                        var m = new Mesh(); smr.BakeMesh(m, true);
                        foreach (var v in m.vertices) { var wv = smr.transform.TransformPoint(v); if (wv.y < minY) minY = wv.y; }
                        Object.DestroyImmediate(m);
                    }
                    keys[i].value -= minY;
                    if (Mathf.Abs(minY) > Mathf.Abs(worst)) worst = minY;
                }
            }
            finally { Object.DestroyImmediate(go); }
            var nc = new AnimationCurve(keys);
            for (int i = 0; i < keys.Length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(nc, i, AnimationUtility.TangentMode.ClampedAuto);
                AnimationUtility.SetKeyRightTangentMode(nc, i, AnimationUtility.TangentMode.ClampedAuto);
            }
            AnimationUtility.SetEditorCurve(clip, rb, nc);
            EditorUtility.SetDirty(clip); AssetDatabase.SaveAssets();
            sb.AppendLine(System.IO.Path.GetFileNameWithoutExtension(clipPath) + " 높이보정 pass" + p + " 최대오차 " + (worst * 1000).ToString("F1") + "mm");
        }
        return sb.ToString();
    }
}
