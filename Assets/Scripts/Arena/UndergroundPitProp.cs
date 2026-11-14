using UnityEngine;

/// <summary>
/// 지하 투기장의 그림 소품(진영 기둥, 매달린 철창·화로·등). 평면 그림이라 옆에서 보면 종이처럼 보이므로
/// 세로축으로만 돌려 늘 카메라를 보게 하고, 사슬에 매달린 것은 천천히 흔들리게 한다. 전투 판정과는 무관하다.
/// </summary>
public sealed class UndergroundPitProp : MonoBehaviour
{
    public bool faceCamera = true;
    [Tooltip("흔들리는 각도(도). 0 이면 흔들리지 않는다.")] public float swing;
    [Tooltip("한 번 흔들리는 데 걸리는 시간(초)")] public float period = 4f;
    public float phase;

    Camera cam;
    Quaternion rest;
    bool hasRest;

    void LateUpdate()
    {
        if (!cam || !cam.isActiveAndEnabled) cam = Camera.main;
        Face(cam ? cam.transform.position : transform.position + Vector3.back, Time.time);
    }

    /// <summary>viewer 쪽을 보게 하고 time 시각의 흔들림을 입힌다. 정지 화면을 찍는 도구도 이것을 부른다.</summary>
    public void Face(Vector3 viewer, float time)
    {
        if (!hasRest) { rest = transform.rotation; hasRest = true; }
        Quaternion rotation = rest;
        if (faceCamera)
        {
            // 그림의 앞면은 -z 쪽이라, +z 가 카메라 반대편을 향하게 둔다.
            Vector3 away = transform.position - viewer; away.y = 0;
            if (away.sqrMagnitude > .0001f) rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
        }
        float angle = swing > 0 ? swing * Mathf.Sin(time * (2 * Mathf.PI / Mathf.Max(.1f, period)) + phase) : 0;
        transform.rotation = rotation * Quaternion.Euler(0, 0, angle);
    }
}
