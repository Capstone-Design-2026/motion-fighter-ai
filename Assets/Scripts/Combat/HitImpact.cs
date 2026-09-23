using System.Collections;
using UnityEngine;

/// <summary>
/// 타격감 연출. 공격이 실제로 대미지를 입힌 순간에만 카메라를 확대하고 시간을 늦춘다.
///
/// 두 가지가 동시에 돌아간다.
///   시간  : 모든 타격. 히트스톱(거의 정지) → 원래 속도로 부드럽게 복귀. 이 복귀 구간이 "슬로우" 로 보인다.
///   카메라: 마무리 일격에만. 확대(orthographicSize 축소) + 타격 지점 쪽으로 살짝 밀기.
///
/// 카메라 확대를 마무리 일격으로 제한한 이유:
/// 타격마다 확대/축소가 반복되면 화면이 쉴 새 없이 출렁여서 보는 사람이 어지럽다.
/// 기본 타격은 시간만 늦추고 화면은 가만히 두는 편이 훨씬 읽기 편하다.
///
/// 시간 연출은 실제 시간(unscaled) 기준으로 재기 때문에 자기가 늦춘 시간에 자기가 갇히지 않는다.
///
/// 씬에 이 컴포넌트가 없으면 첫 타격 때 자동으로 하나 만들어 쓴다.
/// 수치를 인스펙터에서 조정하고 싶으면 아무 GameObject(Main Camera 등)에 직접 붙이면 된다.
///
/// timeScale 소유권:
/// K.O. 슬로우모션은 BattleSceneBridge 가 따로 갖고 있다. 둘이 같은 프레임에 timeScale 을
/// 건드리면 서로를 덮어써서 연출이 끊기므로, 마무리 타격에서는 Fighter 가 시간 연출을 넘기고
/// (allowTimeStop = false) 카메라 확대만 남긴다. 반대편 안전장치로 BattleSceneBridge 는
/// K.O. 연출을 시작하기 전에 ReleaseTimeControl() 을 불러 진행 중인 히트스톱을 정리한다.
/// </summary>
[DefaultExecutionOrder(-400)]
[DisallowMultipleComponent]
public class HitImpact : MonoBehaviour
{
    [Header("전체 On/Off")]
    [SerializeField] private bool enableImpact = true;
    [Tooltip("가드로 막힌 타격에도 (훨씬 약하게) 연출을 넣을지")]
    [SerializeField] private bool impactOnGuard = true;

    [Header("대상 카메라 (비워두면 Camera.main)")]
    [SerializeField] private Camera targetCamera;

    [Header("히트스톱 - 직격 (실제 시간 기준, 초)")]
    [Tooltip("멈추는 정도. 0 에 가까울수록 딱 멈춘다. 완전히 0 으로 두지 않아야 모션이 얼지 않는다.")]
    [Range(0.01f, 1f)]
    [SerializeField] private float hitStopScale = 0.05f;
    [SerializeField] private float hitStopDuration = 0.07f;
    [Tooltip("멈춤에서 원래 속도로 돌아오는 시간. 여기가 길수록 슬로우모션처럼 보인다.")]
    [SerializeField] private float slowRecoverTime = 0.63f;

    [Header("히트스톱 - 가드")]
    [Range(0.01f, 1f)]
    [SerializeField] private float guardStopScale = 0.3f;
    [SerializeField] private float guardStopDuration = 0.04f;
    [SerializeField] private float guardRecoverTime = 0.18f;

    [Header("마무리 일격 - 카메라 확대 (배율, 1 미만 = 확대)")]
    [Range(0.5f, 1f)]
    [SerializeField] private float zoomRatio = 0.85f;

    [Header("마무리 일격 - 확대 타이밍 (실제 시간 기준, 초)")]
    [Tooltip("확대/복귀가 빠르면 화면이 튀어서 어지럽다. 넉넉하게 잡는 편이 좋다.")]
    [SerializeField] private float zoomInTime = 0.25f;
    [SerializeField] private float zoomHoldTime = 0.35f;
    [SerializeField] private float zoomOutTime = 0.60f;

    [Header("타격 지점 쪽으로 밀어주기")]
    [Tooltip("확대하면서 카메라를 맞은 지점 쪽으로 얼마나 당길지. 0 이면 제자리에서 확대만 한다.")]
    [Range(0f, 1f)]
    [SerializeField] private float focusWeight = 0.35f;
    [Tooltip("아무리 멀어도 이 거리(월드 단위) 이상은 밀지 않는다.")]
    [SerializeField] private float maxFocusOffset = 1.2f;

    [Header("화면 흔들림 (기본 꺼짐)")]
    [SerializeField] private bool useShake = false;
    [SerializeField] private float shakeStrength = 0.1f;

    private static HitImpact instance;
    private static bool quitting;

    private Camera cam;
    private float baseOrthoSize = 5f;
    private float baseFov = 60f;
    private Vector3 appliedOffset;          // 우리가 카메라에 더해 둔 값. 매 프레임 되돌렸다 다시 더한다.

    private float defaultFixedDelta = 0.02f;

    private Coroutine timeRoutine;
    private Coroutine cameraRoutine;

    /// <summary>씬에 없으면 자동으로 만들어 준다. 종료 중에는 만들지 않는다.</summary>
    public static HitImpact Instance
    {
        get
        {
            if (quitting) return null;
            if (instance != null) return instance;

            instance = FindAnyObjectByType<HitImpact>();
            if (instance == null)
            {
                var go = new GameObject("HitImpact (auto)");
                instance = go.AddComponent<HitImpact>();
            }
            return instance;
        }
    }

    /// <summary>타격 연출 재생.</summary>
    /// <param name="hitPoint">맞은 지점(월드 좌표). 카메라를 이쪽으로 살짝 당긴다.</param>
    /// <param name="guarded">가드에 막힌 타격이면 true. 훨씬 약한 연출이 나간다.</param>
    /// <param name="finisher">쓰러뜨린 타격이면 true. 이때만 카메라가 확대된다.</param>
    /// <param name="allowTimeStop">false 면 카메라 확대만 하고 timeScale 은 건드리지 않는다.</param>
    public static void Play(Vector2 hitPoint, bool guarded, bool finisher, bool allowTimeStop = true)
    {
        var inst = Instance;
        if (inst != null) inst.Trigger(hitPoint, guarded, finisher, allowTimeStop);
    }

    /// <summary>
    /// 진행 중인 히트스톱을 끝내고 시간을 원래대로 돌려놓는다.
    /// timeScale 을 직접 다루는 다른 연출(K.O. 슬로우모션)이 시작되기 직전에 부른다.
    /// 카메라 확대는 건드리지 않는다.
    /// </summary>
    public static void ReleaseTimeControl()
    {
        if (instance != null) instance.StopTimeEffect();
    }

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);
            return;
        }

        quitting = false;
        instance = this;
        defaultFixedDelta = Time.fixedDeltaTime;
    }

    void OnDisable()
    {
        // 연출 도중에 씬이 넘어가면 카메라가 확대된 채, 시간이 느려진 채로 남는다. 반드시 원복.
        bool hadTimeEffect = timeRoutine != null;

        StopAllCoroutines();
        timeRoutine = null;
        cameraRoutine = null;

        ApplyZoom(1f);
        ClearOffset();

        if (hadTimeEffect) RestoreTime();
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    void OnApplicationQuit()
    {
        quitting = true;
    }

    public void Trigger(Vector2 hitPoint, bool guarded, bool finisher, bool allowTimeStop = true)
    {
        if (!enableImpact) return;
        if (guarded && !impactOnGuard) return;

        if (allowTimeStop)
        {
            if (timeRoutine != null) StopCoroutine(timeRoutine);
            timeRoutine = StartCoroutine(TimeRoutine(guarded));
        }

        // 기본 타격은 여기서 끝. 화면은 건드리지 않고 시간만 늦춘다.
        if (!finisher) return;

        StartZoom(hitPoint, zoomRatio, zoomInTime, zoomHoldTime, zoomOutTime);
    }

    /// <summary>
    /// 타격과 무관하게 카메라만 확대한다. 필살기처럼 자기 길이를 아는 연출이 쓴다.
    /// 확대 배율과 길이를 직접 넘기므로 마무리 일격보다 약하게(= ratio 를 1 에 가깝게) 잡을 수 있다.
    /// </summary>
    public static void PlayZoom(Vector2 focusPoint, float ratio, float inTime, float holdTime, float outTime)
    {
        var inst = Instance;
        if (inst != null) inst.StartZoom(focusPoint, ratio, inTime, holdTime, outTime);
    }

    private void StartZoom(Vector2 focusPoint, float ratio, float inTime, float holdTime, float outTime)
    {
        if (!enableImpact) return;

        // 연출이 이미 돌고 있으면 원래 카메라 값을 다시 재지 않는다.
        // 확대된 상태를 기준으로 잡아버리면 연타할 때 카메라가 계속 파고든다.
        if (cameraRoutine != null) StopCoroutine(cameraRoutine);
        else if (!CaptureCameraBase()) return;

        cameraRoutine = StartCoroutine(CameraRoutine(focusPoint, ratio, inTime, holdTime, outTime));
    }

    // ─────────────────────── 시간 ───────────────────────

    private IEnumerator TimeRoutine(bool guarded)
    {
        float stopScale = guarded ? guardStopScale : hitStopScale;
        float stopTime  = guarded ? guardStopDuration : hitStopDuration;
        float recover   = guarded ? guardRecoverTime : slowRecoverTime;

        SetTimeScale(stopScale);

        if (stopTime > 0f)
            yield return new WaitForSecondsRealtime(stopTime);

        // 멈춤 → 정속. 이 구간이 눈에는 슬로우모션으로 보인다.
        float t = 0f;
        while (t < recover)
        {
            t += Time.unscaledDeltaTime;
            SetTimeScale(Mathf.Lerp(stopScale, 1f, t / recover));
            yield return null;
        }

        RestoreTime();
        timeRoutine = null;
    }

    private void SetTimeScale(float scale)
    {
        Time.timeScale = scale;
        // 물리 간격도 같이 줄여야 느려지는 동안 움직임이 뚝뚝 끊기지 않는다.
        Time.fixedDeltaTime = defaultFixedDelta * scale;
    }

    private void RestoreTime()
    {
        Time.timeScale = 1f;
        Time.fixedDeltaTime = defaultFixedDelta > 0f ? defaultFixedDelta : 0.02f;
    }

    private void StopTimeEffect()
    {
        if (timeRoutine == null) return;

        StopCoroutine(timeRoutine);
        timeRoutine = null;
        RestoreTime();
    }

    // ─────────────────────── 카메라 ───────────────────────

    private bool CaptureCameraBase()
    {
        cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam == null) cam = FindAnyObjectByType<Camera>();
        if (cam == null) return false;

        baseOrthoSize = cam.orthographicSize;
        baseFov = cam.fieldOfView;
        appliedOffset = Vector3.zero;
        return true;
    }

    private IEnumerator CameraRoutine(Vector2 focusPoint, float targetRatio, float zoomIn, float zoomHold, float zoomOut)
    {
        float shake = useShake ? shakeStrength : 0f;

        float inTime  = Mathf.Max(0.0001f, zoomIn);
        float outTime = Mathf.Max(0.0001f, zoomOut);
        float hold    = Mathf.Max(0f, zoomHold);
        float total   = inTime + hold + outTime;

        float elapsed = 0f;
        while (elapsed < total)
        {
            if (cam == null) break;

            // 확대 정도 0 → 1 → 0
            float w;
            if (elapsed < inTime)             w = elapsed / inTime;
            else if (elapsed < inTime + hold) w = 1f;
            else                              w = 1f - (elapsed - inTime - hold) / outTime;

            w = Mathf.Clamp01(w);
            float eased = w * w * (3f - 2f * w);   // smoothstep

            ApplyZoom(Mathf.Lerp(1f, targetRatio, eased));

            // 위치 보정은 항상 "직전 프레임에 우리가 더한 값을 빼고 → 새로 더한다".
            // 이렇게 해야 카메라를 움직이는 다른 스크립트가 나중에 붙어도 값이 누적되지 않는다.
            ClearOffset();

            Vector3 rest = cam.transform.position;
            Vector3 offset = Vector3.zero;

            if (focusWeight > 0f)
            {
                Vector2 toHit = focusPoint - new Vector2(rest.x, rest.y);
                Vector2 focus = Vector2.ClampMagnitude(toHit * focusWeight, maxFocusOffset) * eased;
                offset += new Vector3(focus.x, focus.y, 0f);
            }

            if (shake > 0f)
            {
                float s = shake * eased;
                offset += new Vector3(Random.Range(-s, s), Random.Range(-s, s), 0f);
            }

            ApplyOffset(offset);

            elapsed += Time.unscaledDeltaTime;   // 히트스톱 중에도 카메라는 계속 움직여야 한다
            yield return null;
        }

        ApplyZoom(1f);
        ClearOffset();
        cameraRoutine = null;
    }

    private void ApplyZoom(float ratio)
    {
        if (cam == null) return;

        if (cam.orthographic) cam.orthographicSize = baseOrthoSize * ratio;
        else                  cam.fieldOfView      = baseFov * ratio;
    }

    private void ClearOffset()
    {
        if (cam == null || appliedOffset == Vector3.zero) return;

        cam.transform.position -= appliedOffset;
        appliedOffset = Vector3.zero;
    }

    private void ApplyOffset(Vector3 offset)
    {
        if (cam == null) return;

        cam.transform.position += offset;
        appliedOffset = offset;
    }
}
