using UnityEngine;

/// <summary>자식 리본을 축 둘레로 돌린다 (기운 구슬의 소용돌이, 기파의 나선 꼬리).</summary>
public sealed class QiOrbit : MonoBehaviour
{
    public Vector3 axis = Vector3.forward;
    public float degreesPerSecond = 720f;
    void Update() { transform.Rotate(axis, degreesPerSecond * Time.deltaTime, Space.Self); }
}
