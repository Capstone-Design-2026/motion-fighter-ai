using System.Text;
using UnityEngine;

// 확인용 임시 기록기: 매 프레임 머리·왼손·엉덩이 위치와 애니메이터 상태를 기록 (Claude가 MCP로 읽음)
public class MotionProbe : MonoBehaviour
{
    public bool recording;
    public StringBuilder log = new StringBuilder();
    Animator a; Vector3 pH, pL, pB; float t0;

    public void Begin() { a = GetComponent<Animator>(); log.Length = 0; recording = true; t0 = Time.time; }

    void LateUpdate()
    {
        if (!recording || a == null) return;
        var tr = transform;
        Vector3 h = tr.InverseTransformPoint(a.GetBoneTransform(HumanBodyBones.Head).position);
        Vector3 l = tr.InverseTransformPoint(a.GetBoneTransform(HumanBodyBones.LeftHand).position);
        Vector3 b = tr.InverseTransformPoint(a.GetBoneTransform(HumanBodyBones.Hips).position);
        var s = a.GetCurrentAnimatorStateInfo(0);
        string st = s.IsName("KICK_GUARD") ? "KG" : s.IsName("IDLE") ? "ID" : "??";
        log.AppendFormat("{0:F3} {1} nt={2:F2} tr={3} sp={4:F1} pos=({5:F3},{6:F3}) head=({7:F3},{8:F3},{9:F3}) dH={10:F1}cm dL={11:F1}cm dB={12:F1}cm\n",
            Time.time - t0, st, s.normalizedTime, a.IsInTransition(0) ? 1 : 0, a.speed, tr.position.x, tr.position.z, h.x, h.y, h.z,
            (h - pH).magnitude * 100f, (l - pL).magnitude * 100f, (b - pB).magnitude * 100f);
        pH = h; pL = l; pB = b;
        if (Time.time - t0 > 3f) recording = false;
    }
}
