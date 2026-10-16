using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

// 공격별 가드(방어) 모션 생성기
//  Gen_GUARD_JAB      : 잽 방어  - 양 주먹을 광대 옆에 바짝 붙인 하이 가드, 턱 당김
//  Gen_GUARD_CROSS    : 크로스 방어 - 양 팔뚝을 얼굴 앞에 모아 막고 몸을 뒤로 뺌
//  Gen_GUARD_HIGHKICK : 하이킥 방어 - 양 주먹을 관자놀이에 붙여 머리 감싸고 앞다리 무릎 들어 올림(참고 사진)
//  Gen_GUARD_LOWKICK  : 로우킥 방어 - 앞다리 무릎을 바깥으로 들어 정강이로 체크, 손은 하이 가드
// IDLE 첫 프레임 자세에서 IK로 팔·몸·다리를 옮긴 뒤 휴머노이드 근육 값으로 클립을 만든다.
public static class BoxerGuardGen
{
    const string Dir = "Assets/Boxer_Character/Animations/";
    const string PrefabPath = "Assets/Boxer_Character/Boxer_Character.prefab";
    const string IdleFbx = "Assets/Boxer_Character/Animations/Boxing_idle.fbx";

    // 손목/팔꿈치 위치는 머리 기준: x=몸 바깥쪽(그 손의 옆방향), y=위, z=앞(상대 쪽)
    public class Pose
    {
        public string name;
        public float hipDrop = 0.02f;     // 골반 내림(m)
        public float hipBack = 0.03f;     // 골반을 뒤로 뺌(m)
        public float leanBackDeg = 5f;    // 상체를 뒤로 젖힘(도)
        public float nodDeg = 6f;         // 턱 당김(도)
        public float shoulderRaise = 8f;  // 어깨 올림(도)
        public Vector3 leadWrist, rearWrist;
        public Vector3 leadPole, rearPole;
        public float legLift = 0f;        // 앞다리 들어올리는 높이(m)
        public float legFwd = 0f;         // 앞으로(m)
        public float legOut = 0f;         // 바깥으로(m)
        public Vector3 kneePole = new Vector3(0.3f, 0f, 1f); // x=바깥, y=위, z=앞
        public float toePitch = 0f;       // 들어올린 발끝을 아래로 숙이는 각도
    }

public static Pose[] Poses()
    {
        // 참고: IDLE 주먹 위치(머리 기준) 앞손 (x0, y-0.13, z0.34) / 뒷손 (x0.10, y-0.13, z0.20)
        return new[]
        {
            new Pose {
                name = "JAB",
                hipDrop = 0.02f, hipBack = 0.04f, leanBackDeg = 5f, nodDeg = 7f, shoulderRaise = 8f,
                leadWrist = new Vector3(0.10f, -0.02f, 0.20f), rearWrist = new Vector3(0.11f, -0.04f, 0.14f),
                leadPole = new Vector3(0.25f, -1f, -0.15f), rearPole = new Vector3(0.25f, -1f, -0.15f),
            },
            new Pose {
                name = "CROSS",
                hipDrop = 0.04f, hipBack = 0.08f, leanBackDeg = 9f, nodDeg = 9f, shoulderRaise = 11f,
                leadWrist = new Vector3(0.06f, 0.02f, 0.22f), rearWrist = new Vector3(0.06f, 0.00f, 0.17f),
                leadPole = new Vector3(0.05f, -1f, 0.15f), rearPole = new Vector3(0.05f, -1f, 0.15f),
            },
            new Pose {
                name = "HIGHKICK",
                hipDrop = 0.0f, hipBack = 0.03f, leanBackDeg = 3f, nodDeg = 6f, shoulderRaise = 10f,
                leadWrist = new Vector3(0.16f, 0.01f, 0.10f), rearWrist = new Vector3(0.16f, 0.00f, 0.10f),
                leadPole = new Vector3(0.1f, -0.35f, 1f), rearPole = new Vector3(0.1f, -0.35f, 1f),
                legLift = 0.34f, legFwd = 0.12f, legOut = 0.03f,
                kneePole = new Vector3(0.1f, 0.2f, 1f), toePitch = 25f,
            },
            new Pose {
                name = "LOWKICK",
                hipDrop = 0.03f, hipBack = 0.02f, leanBackDeg = 2f, nodDeg = 6f, shoulderRaise = 8f,
                leadWrist = new Vector3(0.10f, -0.02f, 0.20f), rearWrist = new Vector3(0.11f, -0.04f, 0.14f),
                leadPole = new Vector3(0.25f, -1f, -0.15f), rearPole = new Vector3(0.25f, -1f, -0.15f),
                legLift = 0.24f, legFwd = 0.04f, legOut = 0.07f,
                kneePole = new Vector3(1f, 0.1f, 0.4f), toePitch = 15f,
            },
        };
    }

    // ---------- 유틸 ----------
    public static string Key(string m)
    {
        var p = m.Split(' ');
        if (p.Length >= 3 && (p[1] == "Thumb" || p[1] == "Index" || p[1] == "Middle" || p[1] == "Ring" || p[1] == "Little"))
            return p[0] + "Hand." + p[1] + "." + (p[2] == "Spread" ? "Spread" : p[2] + " Stretched");
        return m;
    }

    static AnimationClip LoadIdleClip()
    {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(IdleFbx))
        {
            var c = o as AnimationClip;
            if (c != null && !c.name.StartsWith("__preview__")) return c;
        }
        return null;
    }

    static Dictionary<string, float> Sample(AnimationClip c, float t)
    {
        var d = new Dictionary<string, float>();
        foreach (var b in AnimationUtility.GetCurveBindings(c))
            d[b.propertyName] = AnimationUtility.GetEditorCurve(c, b).Evaluate(t);
        return d;
    }

    static Quaternion Q(Dictionary<string, float> f, string k) { return new Quaternion(f[k + ".x"], f[k + ".y"], f[k + ".z"], f[k + ".w"]); }
    static void SetV(Dictionary<string, float> f, string k, Vector3 v) { f[k + ".x"] = v.x; f[k + ".y"] = v.y; f[k + ".z"] = v.z; }
    static void SetQ(Dictionary<string, float> f, string k, Quaternion q) { f[k + ".x"] = q.x; f[k + ".y"] = q.y; f[k + ".z"] = q.z; f[k + ".w"] = q.w; }

    static GameObject Spawn(out Animator an)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var go = (GameObject)Object.Instantiate(prefab);
        go.name = "TMP_GUARD_POSE"; go.hideFlags = HideFlags.DontSave;
        go.transform.position = Vector3.zero; go.transform.rotation = Quaternion.identity;
        foreach (var m in go.GetComponents<MonoBehaviour>()) m.enabled = false;
        an = go.GetComponent<Animator>();
        return go;
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
        if (an.GetBoneTransform(left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal) == null ||
            an.GetBoneTransform(left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal) == null ||
            an.GetBoneTransform(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal) == null) return;

        var lower = an.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
        var hand = an.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        Vector3 axis = (hand.position - lower.position).normalized;
        Vector3 p = Vector3.ProjectOnPlane(Palm(an, left), axis), d = Vector3.ProjectOnPlane(desired, axis);
        if (p.sqrMagnitude < 1e-6f || d.sqrMagnitude < 1e-6f) return;
        float ang = Vector3.SignedAngle(p, d, axis);
        lower.rotation = Quaternion.AngleAxis(ang, axis) * lower.rotation;
    }

    public static string lastInfo = "";

    // IDLE 첫 프레임을 기준으로 한 가드 자세 한 장 (근육 값 + 루트/발 IK 목표)
    static Dictionary<string, float> BuildPose(Animator an, HumanPoseHandler h, AnimationClip idle, Pose P)
    {
        var fr = Sample(idle, 0f);
        // 애니메이터 평가 대신 IDLE 클립을 직접 샘플링 (에디터에서 애니메이터 평가가 기본 자세로 나오는 문제 방지)
        idle.SampleAnimation(an.gameObject, 0f);
        System.Func<HumanBodyBones, Transform> B = an.GetBoneTransform;

        var lFoot = B(HumanBodyBones.LeftFoot); var rFoot = B(HumanBodyBones.RightFoot);
        var lToe = B(HumanBodyBones.LeftToes); var rToe = B(HumanBodyBones.RightToes);
        var hips = B(HumanBodyBones.Hips); var head = B(HumanBodyBones.Head);
        var lHandT = B(HumanBodyBones.LeftHand); var rHandT = B(HumanBodyBones.RightHand);

        Quaternion bq0 = Q(fr, "RootQ");
        Vector3 F = an.transform.forward; F.y = 0; F.Normalize();   // 몸이 보는 방향
        Vector3 R = Vector3.Cross(Vector3.up, F).normalized;           // 캐릭터 오른쪽

        // 앞손/앞발 판별: 더 앞에 있는 쪽
        bool leadHandLeft = Vector3.Dot(lHandT.position, F) >= Vector3.Dot(rHandT.position, F);
        bool leadFootLeft = Vector3.Dot(lFoot.position, F) >= Vector3.Dot(rFoot.position, F);

        Vector3 lFootPos = lFoot.position, rFootPos = rFoot.position;
        Quaternion lRot = lFoot.rotation, rRot = rFoot.rotation;
        Quaternion lToeRot = lToe != null ? lToe.rotation : Quaternion.identity;
        Quaternion rToeRot = rToe != null ? rToe.rotation : Quaternion.identity;

        // 1) 골반: 낮추고 뒤로 빼고 뒤로 젖힘
        hips.position += Vector3.down * P.hipDrop - F * P.hipBack;
        float lb = P.leanBackDeg * Mathf.Deg2Rad;
        hips.rotation = Quaternion.FromToRotation(Vector3.up, (Vector3.up * Mathf.Cos(lb) - F * Mathf.Sin(lb)).normalized) * hips.rotation;

        // 2) 턱 당김(고개 숙임) — 목/머리에 나눠서
        var neck = B(HumanBodyBones.Neck);
        float nr = P.nodDeg * 0.5f * Mathf.Deg2Rad;
        Quaternion nod = Quaternion.FromToRotation(F, (F * Mathf.Cos(nr) - Vector3.up * Mathf.Sin(nr)).normalized);
        if (neck != null) neck.rotation = nod * neck.rotation;
        head.rotation = nod * head.rotation;

        // 3) 어깨 올림
        var lSh = B(HumanBodyBones.LeftShoulder); var rSh = B(HumanBodyBones.RightShoulder);
        if (lSh) lSh.rotation = Quaternion.AngleAxis(P.shoulderRaise, F) * lSh.rotation;
        if (rSh) rSh.rotation = Quaternion.AngleAxis(-P.shoulderRaise, F) * rSh.rotation;

        // 4) 팔 (머리 기준 위치)
        Vector3 hd = head.position;
        Vector3 lOut = -R, rOut = R; // 각 손의 바깥 방향
        Vector3 lw = leadHandLeft ? P.leadWrist : P.rearWrist, rw = leadHandLeft ? P.rearWrist : P.leadWrist;
        Vector3 lp = leadHandLeft ? P.leadPole : P.rearPole, rp = leadHandLeft ? P.rearPole : P.leadPole;
        Vector3 lTarget = hd + lOut * lw.x + Vector3.up * lw.y + F * lw.z;
        Vector3 rTarget = hd + rOut * rw.x + Vector3.up * rw.y + F * rw.z;
        TwoBone(B(HumanBodyBones.LeftUpperArm), B(HumanBodyBones.LeftLowerArm), lHandT, lTarget, lOut * lp.x + Vector3.up * lp.y + F * lp.z);
        TwoBone(B(HumanBodyBones.RightUpperArm), B(HumanBodyBones.RightLowerArm), rHandT, rTarget, rOut * rp.x + Vector3.up * rp.y + F * rp.z);
        // 주먹 안쪽(손바닥)이 얼굴을 향하게
        RollPalm(an, true, (hd - lHandT.position));
        RollPalm(an, false, (hd - rHandT.position));

        // 5) 다리: 지지발은 제자리, 앞발은 들어올림
        Vector3 leadOut = leadFootLeft ? -R : R;
        Vector3 lAnk = lFootPos, rAnk = rFootPos;
        Quaternion lGoalDelta = Quaternion.identity, rGoalDelta = Quaternion.identity;
        Vector3 liftOffset = Vector3.up * P.legLift + F * P.legFwd + leadOut * P.legOut;
        Vector3 kp = leadOut * P.kneePole.x + Vector3.up * P.kneePole.y + F * P.kneePole.z;
        Quaternion pitch = Quaternion.AngleAxis(P.toePitch, R);   // +각도 = 발끝 아래로
        if (P.legLift > 0f)
        {
            if (leadFootLeft) { lAnk += liftOffset; lGoalDelta = pitch; }
            else { rAnk += liftOffset; rGoalDelta = pitch; }
        }
        Vector3 lKneePole = leadFootLeft ? kp : (F * 1.0f + (-R) * 0.35f);
        Vector3 rKneePole = leadFootLeft ? (F * 0.8f + R * 0.3f) : kp;
        if (P.legLift <= 0f) { lKneePole = F * 1.0f - R * 0.35f; rKneePole = F * 0.8f + R * 0.3f; }
        TwoBone(B(HumanBodyBones.LeftUpperLeg), B(HumanBodyBones.LeftLowerLeg), lFoot, lAnk, lKneePole);
        TwoBone(B(HumanBodyBones.RightUpperLeg), B(HumanBodyBones.RightLowerLeg), rFoot, rAnk, rKneePole);
        lFoot.rotation = lGoalDelta * lRot; rFoot.rotation = rGoalDelta * rRot;
        if (lToe != null) lToe.rotation = lGoalDelta * lToeRot;
        if (rToe != null) rToe.rotation = rGoalDelta * rToeRot;

        lastInfo = P.name + " leadHandLeft=" + leadHandLeft + " leadFootLeft=" + leadFootLeft +
            " headToLH " + Vector3.Distance(hd, lHandT.position).ToString("F3") +
            " headToRH " + Vector3.Distance(hd, rHandT.position).ToString("F3") +
            " LFootY " + lFoot.position.y.ToString("F3") + " RFootY " + rFoot.position.y.ToString("F3") +
            " ankErrL " + (lFoot.position - lAnk).magnitude.ToString("F3") + " ankErrR " + (rFoot.position - rAnk).magnitude.ToString("F3");

        // 6) 휴머노이드 포즈로 캡처
        var hp = new HumanPose();
        h.GetHumanPose(ref hp);
        var outF = new Dictionary<string, float>(fr);
        var names = HumanTrait.MuscleName;
        for (int i = 0; i < names.Length; i++)
        {
            string k = Key(names[i]);
            if (outF.ContainsKey(k)) outF[k] = hp.muscles[i];
        }
        // 손가락은 원래 IDLE(주먹) 값 유지 — 이미 fr에서 복사됨
        Vector3 bp1 = hp.bodyPosition; Quaternion bq1 = hp.bodyRotation;
        SetV(outF, "RootT", bp1); SetQ(outF, "RootQ", bq1);
        float fb = an.leftFeetBottomHeight;
        foreach (var s in new[] { "Left", "Right" })
        {
            bool left = s == "Left";
            Vector3 ankW = left ? lAnk : rAnk;
            Quaternion delta = left ? lGoalDelta : rGoalDelta;
            Vector3 gw = ankW - Vector3.up * fb;
            Quaternion gq = Q(fr, s + "FootQ");
            SetV(outF, s + "FootT", Quaternion.Inverse(bq1) * (gw - bp1) / an.humanScale);
            SetQ(outF, s + "FootQ", Quaternion.Inverse(bq1) * delta * bq0 * gq);
        }
        return outF;
    }

    static Dictionary<string, float> Lerp(Dictionary<string, float> a, Dictionary<string, float> b, float t)
    {
        var o = new Dictionary<string, float>();
        foreach (var k in a.Keys)
        {
            bool quat = k.Contains("Q.");
            float tt = quat ? Mathf.Clamp01(t) : t;      // 회전값은 오버슈트 없이
            o[k] = Mathf.LerpUnclamped(a[k], b[k], tt);
        }
        return o;
    }

    static AnimationClip MakeClip(string path, AnimationClip template, List<float> times, List<Dictionary<string, float>> frames)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null)
        {
            clip = new AnimationClip();
            AssetDatabase.CreateAsset(clip, path);
        }
        clip.ClearCurves();
        clip.frameRate = 30f;
        foreach (var b in AnimationUtility.GetCurveBindings(template))
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
        var st = AnimationUtility.GetAnimationClipSettings(template);   // IDLE과 같은 루트(높이/위치) 설정 상속
        st.loopTime = false;
        AnimationUtility.SetAnimationClipSettings(clip, st);
        EditorUtility.SetDirty(clip);
        return clip;
    }

    [MenuItem("Tools/Boxer/Generate Guard Clips")]
    public static void MenuGenerate() { Debug.Log(Generate()); }

    public static string Generate()
    {
        var idle = LoadIdleClip();
        if (idle == null) return "IDLE clip not found";
        Animator an; var go = Spawn(out an);
        var sb = new System.Text.StringBuilder();
        try
        {
            var h = new HumanPoseHandler(an.avatar, go.transform);
            var f0 = Sample(idle, 0f);
            foreach (var P in Poses())
            {
                var pose = BuildPose(an, h, idle, P);
                sb.AppendLine(lastInfo);
                // 0 → 0.07 살짝 오버슈트 → 0.12 안착 → 0.45까지 유지 → 0.62에 IDLE로 복귀
                var times = new List<float> { 0f, 0.07f, 0.12f, 0.45f, 0.62f };
                var frames = new List<Dictionary<string, float>> { f0, Lerp(f0, pose, 1.08f), pose, pose, f0 };
                MakeClip(Dir + "Gen_GUARD_" + P.name + ".anim", idle, times, frames);
            }
            AssetDatabase.SaveAssets();
        }
        finally { Object.DestroyImmediate(go); }
        return sb.ToString();
    }

    // 애니메이터에 가드 상태/파라미터/전환 연결 (이미 있으면 건너뜀)
    [MenuItem("Tools/Boxer/Wire Guard States")]
    public static void MenuWire() { Debug.Log(Wire()); }

    public static string Wire()
    {
        var c = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Boxer_Character/Boxer_Animator.controller");
        var sm = c.layers[0].stateMachine;
        AnimatorState idle = null;
        foreach (var s in sm.states) if (s.state.name == "IDLE") idle = s.state;
        var sb = new System.Text.StringBuilder();
        int row = 0;
        foreach (var P in Poses())
        {
            string trig = "Guard" + char.ToUpper(P.name[0]) + P.name.Substring(1).ToLower();
            if (P.name == "HIGHKICK") trig = "GuardHighKick";
            if (P.name == "LOWKICK") trig = "GuardLowKick";
            bool hasParam = false;
            foreach (var p in c.parameters) if (p.name == trig) hasParam = true;
            if (!hasParam) c.AddParameter(trig, AnimatorControllerParameterType.Trigger);

            string stName = "GUARD_" + P.name;
            AnimatorState st = null;
            foreach (var s in sm.states) if (s.state.name == stName) st = s.state;
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Dir + "Gen_GUARD_" + P.name + ".anim");
            if (st == null)
            {
                st = sm.AddState(stName, new Vector3(520, 40 + row * 60, 0));
                var t1 = idle.AddTransition(st);
                t1.hasExitTime = false; t1.duration = 0.04f;
                t1.AddCondition(AnimatorConditionMode.If, 0, trig);
                var t2 = st.AddTransition(idle);
                t2.hasExitTime = true; t2.exitTime = 0.95f; t2.duration = 0.08f;
                sb.AppendLine("created " + stName);
            }
            st.motion = clip;
            st.speed = 1f;
            st.iKOnFeet = true;
            row++;
        }
        EditorUtility.SetDirty(c);
        AssetDatabase.SaveAssets();
        return sb.ToString();
    }
}
