using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

// 팔(글러브 포함)이 몸통·머리에 파묻히는지 재는 도구. 편집 모드에서 Play 없이 사용.
public static class BoxerArmCheck
{
    static GameObject go; static Animator an; static SkinnedMeshRenderer smr;
    static int[] group; // 0=core(몸통/머리/골반), 1=오른팔(뒷손), 2=왼팔(앞손), 3=그 외(다리)
    static int[] tri;
    static Mesh baked;
    static Quaternion tHead;

    // 얼굴 기준 축: side(캐릭터 오른쪽), up, fwd(얼굴 앞)
    public static void FaceAxes(out Vector3 side, out Vector3 up, out Vector3 fwd)
    {
        var h = an.GetBoneTransform(HumanBodyBones.Head);
        var d = h.rotation * Quaternion.Inverse(tHead);
        side = d * Vector3.right; up = d * Vector3.up; fwd = d * Vector3.forward;
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

    // 뒷팔(오른팔) 손목을 머리 기준 (side,up,fwd)로, 팔꿈치 방향을 (pside,pup,pfwd)로 다시 맞춤
    public static void ApplyRear(Vector3 wrist, Vector3 pole)
    {
        Vector3 side, up, fwd; FaceAxes(out side, out up, out fwd);
        var head = an.GetBoneTransform(HumanBodyBones.Head).position;
        Vector3 w = head + side * wrist.x + Vector3.up * wrist.y + fwd * wrist.z;
        Vector3 p = side * pole.x + Vector3.up * pole.y + fwd * pole.z;
        TwoBone(an.GetBoneTransform(HumanBodyBones.RightUpperArm), an.GetBoneTransform(HumanBodyBones.RightLowerArm), an.GetBoneTransform(HumanBodyBones.RightHand), w, p);
    }

    public static GameObject Spawn()
    {
        if (go != null) Object.DestroyImmediate(go);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Boxer_Character/Boxer_Character.prefab");
        go = (GameObject)Object.Instantiate(prefab);
        go.name = "TMP_CHECK"; go.hideFlags = HideFlags.DontSave;
        go.transform.position = new Vector3(12, 0, 0);
        foreach (var m in go.GetComponents<MonoBehaviour>()) m.enabled = false;
        an = go.GetComponent<Animator>();
        an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
        tHead = an.GetBoneTransform(HumanBodyBones.Head).rotation;
        Classify();
        return go;
    }

    static void Classify()
    {
        var mesh = smr.sharedMesh;
        var bones = smr.bones;
        var bw = mesh.boneWeights;
        // 각 뼈 → 그룹
        var bg = new int[bones.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            string n = bones[i] != null ? bones[i].name.ToLower() : "";
            bool right = n.Contains("right") || n.EndsWith(".r") || n.Contains("_r");
            bool left = n.Contains("left") || n.EndsWith(".l") || n.Contains("_l");
            if (n.Contains("arm") || n.Contains("hand") || n.Contains("finger") || n.Contains("thumb") || n.Contains("index") || n.Contains("middle") || n.Contains("ring") || n.Contains("pinky"))
                bg[i] = right ? 1 : (left ? 2 : 3);
            else if (n.Contains("leg") || n.Contains("foot") || n.Contains("toe")) bg[i] = 3;
            else bg[i] = 0;
        }
        group = new int[mesh.vertexCount];
        for (int v = 0; v < group.Length; v++)
        {
            var w = bw[v];
            float[] acc = new float[4];
            acc[bg[w.boneIndex0]] += w.weight0; acc[bg[w.boneIndex1]] += w.weight1;
            acc[bg[w.boneIndex2]] += w.weight2; acc[bg[w.boneIndex3]] += w.weight3;
            int best = 0; for (int k = 1; k < 4; k++) if (acc[k] > acc[best]) best = k;
            group[v] = best;
        }
        tri = mesh.triangles;
    }

    public static string BoneNames()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var b in smr.bones) sb.Append(b ? b.name : "null").Append(", ");
        return sb.ToString();
    }

    public static void Pose(string state, float t, float len)
    {
        an.Rebind();
        an.Play(state, 0, Mathf.Clamp01(t / len));
        an.Update(0f);
    }

    public static Animator Anim { get { return an; } }
    public static GameObject Root { get { return go; } }

    // 점이 core 삼각형 안쪽인지(광선 홀짝) + 가장 가까운 core 꼭짓점 거리
    static bool RayHit(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 e1 = b - a, e2 = c - a, p = Vector3.Cross(d, e2);
        float det = Vector3.Dot(e1, p);
        if (Mathf.Abs(det) < 1e-9f) return false;
        float inv = 1f / det; Vector3 s = o - a;
        float u = Vector3.Dot(s, p) * inv; if (u < 0 || u > 1) return false;
        Vector3 q = Vector3.Cross(s, e1);
        float v = Vector3.Dot(d, q) * inv; if (v < 0 || u + v > 1) return false;
        return Vector3.Dot(e2, q) * inv > 1e-6f;
    }

    // 결과: 오른팔/왼팔 각각 (파묻힌 꼭짓점 수, 가장 깊은 깊이 cm, 가장 가까운 거리 cm)
    public static bool onlyRight = false;
    public static string Measure(float attachZone)
    {
        if (baked == null) baked = new Mesh();
        smr.BakeMesh(baked, true);
        var vs = baked.vertices; var T = smr.transform;
        var wv = new Vector3[vs.Length];
        for (int i = 0; i < vs.Length; i++) wv[i] = T.TransformPoint(vs[i]);
        // core 삼각형
        var coreTris = new List<int>();
        for (int i = 0; i < tri.Length; i += 3)
            if (group[tri[i]] == 0 && group[tri[i + 1]] == 0 && group[tri[i + 2]] == 0) { coreTris.Add(tri[i]); coreTris.Add(tri[i + 1]); coreTris.Add(tri[i + 2]); }
        var coreV = new List<Vector3>();
        for (int i = 0; i < wv.Length; i++) if (group[i] == 0) coreV.Add(wv[i]);
        Vector3[] shoulder = { Vector3.zero, an.GetBoneTransform(HumanBodyBones.RightUpperArm).position, an.GetBoneTransform(HumanBodyBones.LeftUpperArm).position };
        var dirs = new[] { new Vector3(0.31f, 0.93f, 0.2f).normalized, new Vector3(-0.5f, 0.2f, 0.84f).normalized, new Vector3(0.1f, -0.9f, -0.42f).normalized };
        var sb = new System.Text.StringBuilder();
        for (int g = 1; g <= (onlyRight ? 1 : 2); g++)
        {
            int inside = 0, cnt = 0; float maxDepth = 0, minDist = 9;
            for (int i = 0; i < wv.Length; i++)
            {
                if (group[i] != g) continue;
                if ((wv[i] - shoulder[g]).magnitude < attachZone) continue;
                cnt++;
                float md = 9;
                foreach (var cv in coreV) { float d = (cv - wv[i]).sqrMagnitude; if (d < md) md = d; }
                md = Mathf.Sqrt(md);
                int votes = 0;
                foreach (var dir in dirs)
                {
                    int hits = 0;
                    for (int k = 0; k < coreTris.Count; k += 3)
                        if (RayHit(wv[i], dir, wv[coreTris[k]], wv[coreTris[k + 1]], wv[coreTris[k + 2]])) hits++;
                    if ((hits & 1) == 1) votes++;
                }
                bool ins = votes >= 2;
                if (ins) { inside++; if (md > maxDepth) maxDepth = md; }
                else if (md < minDist) minDist = md;
            }
            sb.Append(g == 1 ? "R(뒷팔) " : " | L(앞팔) ").Append("verts ").Append(cnt).Append(" 파묻힘 ").Append(inside)
              .Append(" 최대깊이 ").Append((maxDepth * 100).ToString("F1")).Append("cm 바깥최단 ").Append((minDist * 100).ToString("F1")).Append("cm");
        }
        return sb.ToString();
    }
}
