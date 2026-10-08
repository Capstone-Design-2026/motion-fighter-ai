using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

// 막기(커버 가드) 자세 생성기: Gen_BLOCK_GUARD / IN / OUT 을 새 자세로 다시 만든다.
// 참고 이미지: 뒷팔을 머리 위로 올려 감싸되 머리와 가로 간격이 있고,
// 앞팔은 가슴 앞에서 팔꿈치를 내린 채 세우며, 몸은 앞으로 길게 뻗은 낮은 스탠스.
public static class HanbokBlockCoverGen
{
    // ---- 조절 가능한 값 ----
    public static float hipDrop = 0.09f;       // 골반 내림(m)
    public static float hipFwd = 0.10f;        // 골반을 상대(+Z) 쪽으로(m)
    public static float backFootBack = 0.15f;  // 뒷발을 뒤로(m) — 보폭 넓히기
    public static float backFootOut = 0.03f;   // 뒷발을 바깥으로(m)
    public static float hipPitchDeg = 16f;     // 골반째 앞으로 기울임(등을 편 채)
    public static float leanDeg = 8f;          // 척추 추가 숙임(도)
    public static float nodDeg = 6f;           // 고개 숙임(도) — 작게 해야 안 쭈그러 보임
    public static float turnDeg = 26f;         // 고개를 상대 쪽으로(도)
    public static float shoulderRaise = 9f;    // 어깨 올림(도)
    // 손목 위치 = 머리 기준 (side: +가 캐릭터 오른쪽=뒷손 쪽, up, fwd: 얼굴 앞)
    public static Vector3 rearWrist = new Vector3(0.17f, 0.09f, 0.03f);
    public static Vector3 frontWrist = new Vector3(-0.09f, -0.17f, 0.19f);
    public static float inOvershoot = 1.12f;
    public static float lift = 0f;   // 전체 높이 보정(발이 바닥에 박히지 않게)
    // 팔꿈치 방향 (x=얼굴앞, y=위, z=캐릭터 오른쪽)
    public static Vector3 rearPole = new Vector3(1.0f, 0.25f, 0.5f);
    public static Vector3 frontPole = new Vector3(-0.35f, -1.0f, -0.15f);

    const string Dir = "Assets/Boxer_Character/Animations/";
    static readonly Vector3 O = Vector3.forward; // 상대 방향

    public static string Key(string m)
    {
        var p = m.Split(' ');
        if (p.Length >= 3 && (p[1] == "Thumb" || p[1] == "Index" || p[1] == "Middle" || p[1] == "Ring" || p[1] == "Little"))
            return p[0] + "Hand." + p[1] + "." + (p[2] == "Spread" ? "Spread" : p[2] + " Stretched");
        return m;
    }

    public static Dictionary<string, float> Sample(AnimationClip c, float t)
    {
        var d = new Dictionary<string, float>();
        foreach (var b in AnimationUtility.GetCurveBindings(c))
            d[b.propertyName] = AnimationUtility.GetEditorCurve(c, b).Evaluate(t);
        return d;
    }

    static Vector3 V(Dictionary<string, float> f, string k) { return new Vector3(f[k + ".x"], f[k + ".y"], f[k + ".z"]); }
    static Quaternion Q(Dictionary<string, float> f, string k) { return new Quaternion(f[k + ".x"], f[k + ".y"], f[k + ".z"], f[k + ".w"]); }
    static void SetV(Dictionary<string, float> f, string k, Vector3 v) { f[k + ".x"] = v.x; f[k + ".y"] = v.y; f[k + ".z"] = v.z; }
    static void SetQ(Dictionary<string, float> f, string k, Quaternion q) { f[k + ".x"] = q.x; f[k + ".y"] = q.y; f[k + ".z"] = q.z; f[k + ".w"] = q.w; }

    public static GameObject Spawn(out Animator an)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Boxer_Character/Boxer_Character.prefab");
        var go = (GameObject)Object.Instantiate(prefab);
        go.name = "TMP_POSE"; go.hideFlags = HideFlags.DontSave;
        go.transform.position = Vector3.zero; go.transform.rotation = Quaternion.identity;
        foreach (var m in go.GetComponents<MonoBehaviour>()) m.enabled = false;
        an = go.GetComponent<Animator>();
        return go;
    }

    public static void EvalState(Animator an, string state, float t, float len)
    {
        an.Rebind();
        an.Play(state, 0, Mathf.Clamp01(t / len));
        an.Update(0f);
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

    public static string lastInfo = "";

    public static Dictionary<string, float> Cover(Animator an, HumanPoseHandler h, AnimationClip oldGuard, float t)
    {
        var fr = Sample(oldGuard, t);
        EvalState(an, "BLOCK", t, oldGuard.length);
        var B = (System.Func<HumanBodyBones, Transform>)an.GetBoneTransform;
        var lFoot = B(HumanBodyBones.LeftFoot); var rFoot = B(HumanBodyBones.RightFoot);
        var lToe = B(HumanBodyBones.LeftToes); var rToe = B(HumanBodyBones.RightToes);

        Quaternion bq0 = Q(fr, "RootQ");
        Vector3 F = bq0 * Vector3.forward; F.y = 0; F.Normalize();

        // 발 목표: 왼발(앞발)은 그대로, 오른발(뒷발)은 뒤·바깥으로 이동 → 보폭 넓힘
        Vector3 sideOut = Vector3.Cross(Vector3.up, O).normalized; // 상대 기준 오른쪽
        Vector3 lAnk = lFoot.position + Vector3.up * lift;
        Vector3 rAnk = rFoot.position - O * backFootBack + sideOut * backFootOut + Vector3.up * lift;
        Quaternion lRot = lFoot.rotation, rRot = rFoot.rotation;
        Quaternion lToeRot = lToe.rotation, rToeRot = rToe.rotation;

        // 1) 골반: 내리고 앞으로 + 등을 편 채 골반째 앞으로 기울임
        var hips = B(HumanBodyBones.Hips);
        hips.position += Vector3.down * hipDrop + O * hipFwd + Vector3.up * lift;
        Vector3 D = (F * 0.6f + O * 0.4f).normalized;
        float hr = hipPitchDeg * Mathf.Deg2Rad;
        hips.rotation = Quaternion.FromToRotation(Vector3.up, (Vector3.up * Mathf.Cos(hr) + D * Mathf.Sin(hr)).normalized) * hips.rotation;

        // 2) 척추 약간 더 (등은 거의 편 상태)
        float[] w = { 0.4f, 0.35f, 0.25f };
        var sp = new[] { B(HumanBodyBones.Spine), B(HumanBodyBones.Chest), B(HumanBodyBones.UpperChest) };
        for (int i = 0; i < 3; i++)
        {
            if (sp[i] == null) continue;
            float r = leanDeg * w[i] * Mathf.Deg2Rad;
            sp[i].rotation = Quaternion.FromToRotation(Vector3.up, (Vector3.up * Mathf.Cos(r) + D * Mathf.Sin(r)).normalized) * sp[i].rotation;
        }

        // 3) 고개: 상대 쪽으로 돌리고 살짝만 숙임(턱 당김)
        var neck = B(HumanBodyBones.Neck); var head = B(HumanBodyBones.Head);
        neck.rotation = Quaternion.AngleAxis(-turnDeg * 0.5f, Vector3.up) * neck.rotation;
        head.rotation = Quaternion.AngleAxis(-turnDeg * 0.5f, Vector3.up) * head.rotation;
        Vector3 face = Quaternion.AngleAxis(-turnDeg, Vector3.up) * F;
        Vector3 faceSide = Vector3.Cross(Vector3.up, face).normalized; // 캐릭터 오른쪽(뒷손 쪽)
        float nr = nodDeg * 0.5f * Mathf.Deg2Rad;
        Quaternion nod = Quaternion.FromToRotation(face, (face * Mathf.Cos(nr) - Vector3.up * Mathf.Sin(nr)).normalized);
        neck.rotation = nod * neck.rotation;
        head.rotation = nod * head.rotation;

        // 4) 어깨 올림(움츠림 아님 — 가드 올린 느낌)
        var lSh = B(HumanBodyBones.LeftShoulder); var rSh = B(HumanBodyBones.RightShoulder);
        if (lSh) lSh.rotation = Quaternion.AngleAxis(shoulderRaise, F) * lSh.rotation;
        if (rSh) rSh.rotation = Quaternion.AngleAxis(-shoulderRaise, F) * rSh.rotation;

        // 5) 팔 — 머리와 가로 간격을 두고
        Vector3 hd = head.position;
        Vector3 rw = hd + faceSide * rearWrist.x + Vector3.up * rearWrist.y + face * rearWrist.z;
        // 뒷팔꿈치: 머리 옆·앞 (팔뚝이 머리 옆에 세로로, 사이에 공간)
        TwoBone(B(HumanBodyBones.RightUpperArm), B(HumanBodyBones.RightLowerArm), B(HumanBodyBones.RightHand), rw,
            face * rearPole.x + Vector3.up * rearPole.y + faceSide * rearPole.z);
        Vector3 lw = hd + faceSide * frontWrist.x + Vector3.up * frontWrist.y + face * frontWrist.z;
        // 앞팔꿈치: 아래·약간 뒤 (팔뚝이 가슴 앞에 세로로)
        TwoBone(B(HumanBodyBones.LeftUpperArm), B(HumanBodyBones.LeftLowerArm), B(HumanBodyBones.LeftHand), lw,
            face * frontPole.x + Vector3.up * frontPole.y + faceSide * frontPole.z);
        // 손등이 바깥(상대 쪽)을 보도록
        RollPalm(an, false, (hd + Vector3.up * 0.05f - B(HumanBodyBones.RightHand).position));
        RollPalm(an, true, (-face * 0.75f + faceSide * 0.35f));

        // 6) 다리: 무릎은 앞·바깥으로
        TwoBone(B(HumanBodyBones.LeftUpperLeg), B(HumanBodyBones.LeftLowerLeg), lFoot, lAnk,
            O * 1.0f - faceSide * 0.35f);
        TwoBone(B(HumanBodyBones.RightUpperLeg), B(HumanBodyBones.RightLowerLeg), rFoot, rAnk,
            O * 0.8f + faceSide * 0.3f);
        lFoot.rotation = lRot; rFoot.rotation = rRot; lToe.rotation = lToeRot; rToe.rotation = rToeRot;

        float lReach = (lAnk - B(HumanBodyBones.LeftUpperLeg).position).magnitude;
        float rReach = (rAnk - B(HumanBodyBones.RightUpperLeg).position).magnitude;
        lastInfo = "hips " + hips.position.ToString("F3") + " head " + hd.ToString("F3") +
                   " RH " + B(HumanBodyBones.RightHand).position.ToString("F3") +
                   " RElb " + B(HumanBodyBones.RightLowerArm).position.ToString("F3") +
                   " LH " + B(HumanBodyBones.LeftHand).position.ToString("F3") +
                   " LElb " + B(HumanBodyBones.LeftLowerArm).position.ToString("F3") +
                   " legReach L " + lReach.ToString("F3") + " R " + rReach.ToString("F3") + " (max 0.849)" +
                   " footErr L " + (lFoot.position - lAnk).magnitude.ToString("F3") + " R " + (rFoot.position - rAnk).magnitude.ToString("F3") +
                   " headToRearElb " + Vector3.Distance(hd, B(HumanBodyBones.RightLowerArm).position).ToString("F3");

        // 7) 캡처
        var hp = new HumanPose();
        h.GetHumanPose(ref hp);
        var outF = new Dictionary<string, float>(fr);
        var names = HumanTrait.MuscleName;
        for (int i = 0; i < names.Length; i++)
        {
            string k = Key(names[i]);
            if (outF.ContainsKey(k)) outF[k] = hp.muscles[i];
        }
        foreach (var k in new List<string>(outF.Keys))
            if (k.StartsWith("RightHand.")) { outF[k] = fr[k]; outF["LeftHand." + k.Substring(10)] = fr[k]; }
        outF["Jaw Close"] = 0; outF["Jaw Left-Right"] = 0;

        Vector3 bp1 = hp.bodyPosition; Quaternion bq1 = hp.bodyRotation;
        SetV(outF, "RootT", bp1); SetQ(outF, "RootQ", bq1);
        // 발 IK 목표 = 새 발 월드 위치(발목에서 발바닥 높이만큼 내림)
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
        var bindings = AnimationUtility.GetCurveBindings(clip);
        foreach (var b in bindings)
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

    public static string Generate()
    {
        string bk = "Assets/Boxer_Character/Backups/0928_before_block_cover/";
        var oldGuard = AssetDatabase.LoadAssetAtPath<AnimationClip>(bk + "Gen_BLOCK_GUARD.anim");
        var oldIn = AssetDatabase.LoadAssetAtPath<AnimationClip>(bk + "Gen_BLOCK_IN.anim");
        var oldOut = AssetDatabase.LoadAssetAtPath<AnimationClip>(bk + "Gen_BLOCK_OUT.anim");
        var guard = AssetDatabase.LoadAssetAtPath<AnimationClip>(Dir + "Gen_BLOCK_GUARD.anim");
        var cin = AssetDatabase.LoadAssetAtPath<AnimationClip>(Dir + "Gen_BLOCK_IN.anim");
        var cout = AssetDatabase.LoadAssetAtPath<AnimationClip>(Dir + "Gen_BLOCK_OUT.anim");

        EditorUtility.CopySerialized(oldGuard, guard);
        guard.name = "Gen_BLOCK_GUARD";
        AssetDatabase.SaveAssets();

        Animator an; var go = Spawn(out an);
        var sb = new System.Text.StringBuilder();
        try
        {
            var h = new HumanPoseHandler(an.avatar, go.transform);
            var times = new List<float>(); var frames = new List<Dictionary<string, float>>();
            int n = 24; float len = oldGuard.length;
            for (int i = 0; i <= n; i++)
            {
                float t = len * i / n;
                times.Add(t); frames.Add(Cover(an, h, oldGuard, t));
                if (i == 0) sb.AppendLine("t0: " + lastInfo);
            }
            frames[n] = new Dictionary<string, float>(frames[0]);
            var g0 = frames[0];

            var s0 = Sample(oldIn, 0f);
            var inT = new List<float> { 0f, 0.08f, 0.14f };
            var inF = new List<Dictionary<string, float>> { s0, Lerp(s0, g0, inOvershoot), g0 };
            var e1 = Sample(oldOut, oldOut.length);
            var outT = new List<float> { 0f, 0.07f, 0.14f, 0.21f, 0.28f };
            var outF = new List<Dictionary<string, float>> { g0, Lerp(g0, e1, 0.12f), Lerp(g0, e1, 0.5f), Lerp(g0, e1, 0.88f), e1 };

            WriteClip(guard, times, frames);
            WriteClip(cin, inT, inF);
            WriteClip(cout, outT, outF);
            AssetDatabase.SaveAssets();
        }
        finally { Object.DestroyImmediate(go); }
        return sb.ToString();
    }

    public static string Measure(string state, float len, int steps)
    {
        Animator an; var go = Spawn(out an);
        var sb = new System.Text.StringBuilder();
        try
        {
            for (int i = 0; i <= steps; i++)
            {
                float t = len * i / steps;
                EvalState(an, state, t, len);
                float minY = 999;
                foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    var m = new Mesh(); smr.BakeMesh(m, true);
                    foreach (var v in m.vertices) { var wv = smr.transform.TransformPoint(v); if (wv.y < minY) minY = wv.y; }
                    Object.DestroyImmediate(m);
                }
                sb.AppendLine(state + " t" + t.ToString("F2") + " minY " + minY.ToString("F3") +
                    " hips " + an.GetBoneTransform(HumanBodyBones.Hips).position.ToString("F3") +
                    " head " + an.GetBoneTransform(HumanBodyBones.Head).position.ToString("F3") +
                    " LH " + an.GetBoneTransform(HumanBodyBones.LeftHand).position.ToString("F3") +
                    " RH " + an.GetBoneTransform(HumanBodyBones.RightHand).position.ToString("F3") +
                    " LF " + an.GetBoneTransform(HumanBodyBones.LeftFoot).position.ToString("F3") +
                    " RF " + an.GetBoneTransform(HumanBodyBones.RightFoot).position.ToString("F3"));
            }
        }
        finally { Object.DestroyImmediate(go); }
        return sb.ToString();
    }

    // 미리보기 캐릭터를 x=6 위치에 세워 두고 BLOCK 첫 프레임 자세로
    public static string Preview()
    {
        var old = GameObject.Find("TMP_PREVIEW"); if (old) Object.DestroyImmediate(old);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Boxer_Character/Boxer_Character.prefab");
        var go = (GameObject)Object.Instantiate(prefab);
        go.name = "TMP_PREVIEW"; go.hideFlags = HideFlags.DontSave;
        go.transform.position = new Vector3(6, 0, 0);
        foreach (var m in go.GetComponents<MonoBehaviour>()) m.enabled = false;
        var an = go.GetComponent<Animator>();
        an.Rebind(); an.Play("BLOCK", 0, 0f); an.Update(0f);
        return "preview at " + go.transform.position.ToString("F2");
    }
}
