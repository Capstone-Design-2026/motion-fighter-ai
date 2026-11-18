using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 파운장 카메라 연출: 시야각 좁히기(줌)와 흔들림.
/// 전투 카메라(Battle3DCamera)가 매 프레임 위치를 다시 계산하므로, 그 결과는 건드리지 않고
/// 렌더링 직전에만 오프셋을 더했다가 렌더링이 끝나면 되돌린다. 실제 시간 기준이라 슬로우 중에도 흐른다.
/// </summary>
[DisallowMultipleComponent]
public sealed class QiCameraFx : MonoBehaviour
{
    Camera cam;
    float fovMul = 1f, fovTarget = 1f, fovVelocity, shake, shakeDecay = 6f, pull, pullTarget, pullVelocity;
    Vector3 savedPosition; float savedFov; bool applied;

    static QiCameraFx Ensure()
    {
        var c = Camera.main;
        if (c == null) return null;
        var fx = c.GetComponent<QiCameraFx>();
        return fx != null ? fx : c.gameObject.AddComponent<QiCameraFx>();
    }

    /// <summary>시야각 배율. 1 이 원래 화면, 0.85 면 15% 좁혀 당겨 보인다.</summary>
    public static void Zoom(float multiplier) { var fx = Ensure(); if (fx) fx.fovTarget = Mathf.Clamp(multiplier, .5f, 1.2f); }

    /// <summary>흔들림. amplitude 는 월드 미터, decay 는 초당 감쇠 속도.</summary>
    public static void Shake(float amplitude, float decay = 6f)
    {
        var fx = Ensure();
        if (!fx) return;
        fx.shake = Mathf.Max(fx.shake, amplitude); fx.shakeDecay = decay;
    }

    /// <summary>카메라를 뒤로 물린다(월드 미터, 0 이 원래 자리). 크게 뛰거나 넓게 퍼지는 적 필살기를 한 화면에 담을 때 쓴다.</summary>
    public static void Pull(float distance) { var fx = Ensure(); if (fx) fx.pullTarget = Mathf.Clamp(distance, 0f, 4f); }

    /// <summary>
    /// 렌더링 때 실제로 쓰는 카메라 자리와 시야각(뒤로 물림·줌 포함, 흔들림 제외).
    /// 이 효과는 그리는 순간에만 더해지므로 평소 transform 에는 없다. PvP 손님 화면에 카메라를 보낼 때 이 값을 써야
    /// 크게 뛰거나 넓게 퍼지는 필살기가 손님 화면에서도 한 화면에 담긴다(2026-11-16).
    /// </summary>
    public static void EffectivePose(Camera c, out Vector3 position, out float fov)
    {
        position = c.transform.position; fov = c.fieldOfView;
        var fx = c.GetComponent<QiCameraFx>();
        if (fx == null || !fx.isActiveAndEnabled || fx.applied) return;
        if (!c.orthographic) fov *= fx.fovMul;
        if (fx.pull > .001f) position += -c.transform.forward * fx.pull + c.transform.up * (fx.pull * .17f);
    }

    /// <summary>연출을 즉시 원래대로.</summary>
    public static void ResetAll() { var c = Camera.main; var fx = c ? c.GetComponent<QiCameraFx>() : null; if (fx) { fx.fovTarget = 1f; fx.shake = 0f; fx.pullTarget = 0f; } }

    void OnEnable()
    {
        cam = GetComponent<Camera>();
        RenderPipelineManager.beginCameraRendering += Begin;
        RenderPipelineManager.endCameraRendering += End;
    }
    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= Begin;
        RenderPipelineManager.endCameraRendering -= End;
    }
    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        fovMul = Mathf.SmoothDamp(fovMul, fovTarget, ref fovVelocity, .14f, Mathf.Infinity, dt);
        shake = Mathf.Max(0f, shake - shake * shakeDecay * dt - .002f * dt);
        pull = Mathf.SmoothDamp(pull, pullTarget, ref pullVelocity, .1f, Mathf.Infinity, dt);
    }
    void Begin(ScriptableRenderContext context, Camera c)
    {
        if (c != cam) return;
        savedPosition = c.transform.position; savedFov = c.fieldOfView; applied = true;
        if (!c.orthographic) c.fieldOfView = savedFov * fovMul;
        if (pull > .001f) c.transform.position += -c.transform.forward * pull + c.transform.up * (pull * .17f);
        if (shake > .0005f)
        {
            float t = Time.unscaledTime * 41f;
            c.transform.position += c.transform.right * ((Mathf.PerlinNoise(t, .3f) - .5f) * 2f * shake)
                                  + c.transform.up * ((Mathf.PerlinNoise(.7f, t) - .5f) * 2f * shake);
        }
    }
    void End(ScriptableRenderContext context, Camera c)
    {
        if (c != cam || !applied) return;
        c.transform.position = savedPosition; c.fieldOfView = savedFov; applied = false;
    }
}
