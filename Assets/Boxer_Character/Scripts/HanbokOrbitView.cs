using UnityEngine;
using UnityEngine.InputSystem;

// Play 중 화면 돌려보기: 캐릭터 주변을 카메라가 빙 돌아봄
// 조작: 마우스 오른쪽 버튼 드래그 = 회전, 휠 = 확대/축소, Q/E = 좌우 회전, R = 처음 시점
// 카메라를 피벗(캐릭터 가슴 높이) 자식으로 옮겨서 회전 → 타격 카메라 흔들림(HanbokActionFeel)과 같이 동작
[DefaultExecutionOrder(-100)]
public class HanbokOrbitView : MonoBehaviour
{
    public Vector3 pivotPosition = new Vector3(0f, 1.0f, 0f);
    public float dragSpeed = 0.25f;
    public float keySpeed = 90f;
    public float zoomSpeed = 0.1f;
    public Vector2 pitchRange = new Vector2(-30f, 70f);
    public Vector2 zoomRange = new Vector2(0.4f, 2.5f);

    Transform pivot;
    float yaw, pitch, zoom = 1f;

    void Awake()
    {
        var go = new GameObject("OrbitPivot");
        pivot = go.transform;
        pivot.position = pivotPosition;
        transform.SetParent(pivot, true);
    }

    void Update()
    {
        var mouse = Mouse.current;
        var kb = Keyboard.current;
        if (mouse != null)
        {
            if (mouse.rightButton.isPressed)
            {
                Vector2 d = mouse.delta.ReadValue();
                yaw += d.x * dragSpeed;
                pitch -= d.y * dragSpeed;
            }
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
                zoom = Mathf.Clamp(zoom - Mathf.Sign(scroll) * zoomSpeed, zoomRange.x, zoomRange.y);
        }
        if (kb != null)
        {
            if (kb.qKey.isPressed) yaw -= keySpeed * Time.deltaTime;
            if (kb.eKey.isPressed) yaw += keySpeed * Time.deltaTime;
            if (kb.rKey.wasPressedThisFrame) { yaw = 0; pitch = 0; zoom = 1f; }
        }
        pitch = Mathf.Clamp(pitch, pitchRange.x, pitchRange.y);
        pivot.rotation = Quaternion.Euler(pitch, yaw, 0f);
        pivot.localScale = Vector3.one * zoom;
    }
}
