using System.Text;
using UnityEngine;

// 확인용 임시 기록기: 로우킥 막기 때 막는 쪽 다리 자세와, 차는 다리와의 최소 거리를 기록 (Claude가 MCP로 읽음)
public class LegProbe : MonoBehaviour
{
    public Animator def, att;
    public bool recording;
    public StringBuilder log = new StringBuilder();
    float t0;
    public float[] shotTimes = new float[0];
    public string shotPrefix = "C:/KHS_test/Assets/../Temp/legshot";
    int shotIdx;
    public bool focusDef;
    public Vector2 lastCom;
    public Vector3 camOffset = new Vector3(2.2f, 0.9f, 0f);

    public void Begin(Animator d, Animator a) { def = d; att = a; log.Length = 0; recording = true; t0 = Time.time; shotIdx = 0; }

    static float SegDist(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2, out float s, out float t)
    {
        Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
        float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
        float c = Vector3.Dot(d1, r), b = Vector3.Dot(d1, d2), den = a * e - b * b;
        s = den > 1e-6f ? Mathf.Clamp01((b * f - c * e) / den) : 0f;
        t = (b * s + f) / e;
        if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
        else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - c) / a); }
        return ((p1 + d1 * s) - (p2 + d2 * t)).magnitude;
    }

    Vector3 L(HumanBodyBones b) { return def.transform.InverseTransformPoint(def.GetBoneTransform(b).position); }

    void Shot(string path)
    {
        var go = new GameObject("ShotCam"); var c = go.AddComponent<Camera>();
        Vector3 mid = focusDef ? def.transform.position : (def.transform.position + att.transform.position) * 0.5f;
        Vector3 side = Vector3.Cross(Vector3.up, (def.transform.position - att.transform.position).normalized);
        go.transform.position = mid + side * camOffset.x + Vector3.up * camOffset.y + def.transform.forward * camOffset.z;
        go.transform.LookAt(mid + Vector3.up * 0.6f);
        c.fieldOfView = 40f; c.clearFlags = CameraClearFlags.SolidColor; c.backgroundColor = new Color(0.55f, 0.6f, 0.65f);
        var rt = new RenderTexture(900, 700, 24); c.targetTexture = rt; c.Render();
        RenderTexture.active = rt; var tex = new Texture2D(900, 700, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 900, 700), 0, 0); tex.Apply(); RenderTexture.active = null;
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        c.targetTexture = null; Destroy(rt); Destroy(tex); Destroy(go);
    }

    void LateUpdate()
    {
        if (!recording || def == null) return;
        var s = def.GetCurrentAnimatorStateInfo(0);
        string st = s.IsName("KICK_GUARD_LOW") ? "KGL" : s.IsName("IDLE") ? "ID" : s.IsName("HIGHGUARD_V2") ? "HG" : "??";
        var sa = att.GetCurrentAnimatorStateInfo(0);
        Vector3 hip = L(HumanBodyBones.Hips), rk = L(HumanBodyBones.RightLowerLeg), rf = L(HumanBodyBones.RightFoot), ru = L(HumanBodyBones.RightUpperLeg);
        Vector3 lk = L(HumanBodyBones.LeftLowerLeg), lf = L(HumanBodyBones.LeftFoot);
        // 뒷다리(오른) 정강이 기울기: 무릎이 발목보다 앞(+z)으로 나간 각도
        Vector3 shin = rk - rf; float shinLean = Mathf.Atan2(shin.z, shin.y) * Mathf.Rad2Deg;
        Vector3 thigh = ru - rk; float thighLean = Mathf.Atan2(thigh.z, thigh.y) * Mathf.Rad2Deg;
        // 차는 다리 정강이 vs 막는 다리 정강이·허벅지
        Vector3 ak = att.GetBoneTransform(HumanBodyBones.RightLowerLeg).position, af = att.GetBoneTransform(HumanBodyBones.RightFoot).position;
        Vector3 dk = def.GetBoneTransform(HumanBodyBones.LeftLowerLeg).position, dfp = def.GetBoneTransform(HumanBodyBones.LeftFoot).position, du = def.GetBoneTransform(HumanBodyBones.LeftUpperLeg).position;
        float s1, t1, s2, t2;
        float dShin = SegDist(ak, af, dk, dfp, out s1, out t1);
        float dThigh = SegDist(ak, af, du, dk, out s2, out t2);
        HumanBodyBones[] cb = { HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.Head, HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm, HumanBodyBones.LeftHand, HumanBodyBones.RightHand };
        float[] cw = { 0.15f, 0.15f, 0.2f, 0.08f, 0.1f, 0.1f, 0.05f, 0.05f, 0.015f, 0.015f, 0.03f, 0.03f, 0.02f, 0.02f };
        Vector3 com = Vector3.zero; float tw = 0f; for (int k = 0; k < cb.Length; k++) { com += L(cb[k]) * cw[k]; tw += cw[k]; } com /= tw;
        Vector3 rtoe = L(HumanBodyBones.RightToes); Vector3 sup = (rf + rtoe) * 0.5f;
        lastCom = new Vector2(com.x - sup.x, com.z - sup.z);
        log.AppendFormat("com-sup=({0:F2},{1:F2}) ", com.x - sup.x, com.z - sup.z);
        log.AppendFormat("{0:F2} {1} nt={2:F2} att={3:F2} hip=({4:F2},{5:F2},{6:F2}) rf=({7:F2},{8:F2},{9:F2}) lf=({10:F2},{11:F2},{12:F2}) lk=({13:F2},{14:F2},{15:F2}) shinLean={16:F0} thighLean={17:F0} dShin={18:F2}@{19:F1}/{20:F1} dThigh={21:F2}\n",
            Time.time - t0, st, s.normalizedTime, sa.normalizedTime, hip.x, hip.y, hip.z, rf.x, rf.y, rf.z, lf.x, lf.y, lf.z, lk.x, lk.y, lk.z, shinLean, thighLean, dShin, s1, t1, dThigh);
        if (shotIdx < shotTimes.Length && Time.time - t0 >= shotTimes[shotIdx]) { Shot(shotPrefix + shotIdx + ".png"); shotIdx++; }
        if (Time.time - t0 > 2.6f) recording = false;
    }
}
