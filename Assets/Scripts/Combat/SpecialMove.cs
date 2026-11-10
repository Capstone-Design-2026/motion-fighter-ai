using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 필살기. 맞을 때마다 게이지가 차고, 다 차면 한 번 쓸 수 있다.
///
/// 연출은 두 가지가 같이 간다.
///   시간  : 화면 전체를 늦춘다. duration(실제 시간) 동안 지속되고 마지막 구간에서 정속으로 돌아온다.
///   컷인  : 캐릭터의 idle 스프라이트를 크게 키워 자기 진영 화면 끝에서 반대편 끝으로 흘려보낸다.
///           왼쪽 진영이면 왼쪽 끝에서 나타나 오른쪽으로, 오른쪽 진영이면 그 반대로 빠진다.
///
/// 전용 스프라이트가 없으므로 시작 프레임의 idle 스프라이트를 그대로 재활용한다.
/// 따로 쓰고 싶은 그림이 생기면 cutInSprite 에 넣으면 그쪽이 우선한다.
///
/// timeScale 소유권:
/// 타격 히트스톱(HitImpact)과 K.O. 슬로우모션(BattleSceneBridge)도 같은 값을 건드린다.
/// 발동할 때 HitImpact 의 시간 연출을 먼저 정리하고, 도중에 K.O. 가 나서 조작이 잠기면
/// (BattleSceneBridge 가 timeScale 을 가져간 것이므로) 여기서는 손을 뗀다.
/// </summary>
[RequireComponent(typeof(Health))]
[RequireComponent(typeof(Fighter))]
[DisallowMultipleComponent]
public class SpecialMove : MonoBehaviour
{
    [Header("게이지")]
    [Tooltip("이만큼 맞으면 1회 사용할 수 있다. 가드로 막고 입은 대미지도 똑같이 한 번으로 친다.")]
    [SerializeField] private int hitsToCharge = 5;

    [Header("피해량")]
    [Tooltip("발동하면 상대 체력이 이만큼 깎인다. 판정도 모션도 없이 그냥 들어간다.")]
    [SerializeField] private int damage = 30;

    [Header("게이지 채우기 (테스트용)")]
    [Tooltip("맞지 않고도 게이지를 채우는 키. 필살기 연출만 확인하고 싶을 때 쓴다. None 이면 꺼진다.")]
    [SerializeField] private KeyCode chargeKey = KeyCode.I;
    [Tooltip("한 번 누를 때 차는 칸 수. hitsToCharge 이상으로 두면 한 번에 만충된다.")]
    [SerializeField] private int chargeKeyAmount = 1;

    [Header("발동 키")]
    [Tooltip("공격(P) 옆에 두는 편이 손이 편하다. PlayerController 의 useKeyboard 가 꺼져 있으면 무시된다.")]
    [SerializeField] private KeyCode activateKey = KeyCode.O;

    [Header("컷인 (자기 진영 끝에서 나타나 반대편 끝으로 흘러간다)")]
    [Tooltip("비워두면 시작할 때 서 있는 스프라이트를 그대로 쓴다.")]
    [SerializeField] private Sprite cutInSprite;
    [SerializeField] private float cutInScale = 2.8f;
    [Tooltip("캐릭터 발밑 기준 높이.")]
    [SerializeField] private float cutInHeight = 1.8f;
    [Tooltip("가로 이동 속도 배분. 가로축이 연출 진행도(0~1), 세로축이 이동 진행도(0~1)다. "
           + "기본값은 앞 60% 시간 동안 화면의 25%만 가고, 남은 40% 시간에 나머지를 지나간다.")]
    [SerializeField] private AnimationCurve sweepCurve = new AnimationCurve(
        new Keyframe(0f,   0f,   SlowSlope, SlowSlope),
        new Keyframe(0.6f, 0.25f, SlowSlope, FastSlope),
        new Keyframe(1f,   1f,   FastSlope, FastSlope));
    [SerializeField] private float fadeInTime = 0.15f;
    [SerializeField] private float fadeOutTime = 0.3f;

    [Header("시간 정지 (실제 시간 기준, 초)")]
    [Tooltip("연출 동안의 게임 속도. 0 이면 완전히 멈춘다. 컷인은 실제 시간으로 움직이므로 멈춰도 흘러간다.")]
    [Range(0f, 1f)]
    [SerializeField] private float slowScale = 0f;
    [Tooltip("연출 전체 길이. 이 안에 복귀 구간이 포함된다.")]
    [SerializeField] private float duration = 1f;
    [Tooltip("정속으로 돌아오는 마지막 구간. 0 이면 뚝 끊긴다.")]
    [SerializeField] private float recoverTime = 0.3f;

    [Header("카메라 확대")]
    [Tooltip("마무리 일격(HitImpact 의 zoomRatio)보다 1 에 가깝게 두어야 덜 확대된다. 현재 씬 기준 마무리는 0.5.")]
    [Range(0.5f, 1f)]
    [SerializeField] private float zoomRatio = 0.75f;
    [SerializeField] private float zoomInTime = 0.2f;
    [SerializeField] private float zoomOutTime = 0.35f;

    /// <summary>(현재 게이지, 만충 수치)</summary>
    public event Action<int, int> OnChargeChanged;

    public int Charge { get { return charge; } }
    public int HitsToCharge { get { return hitsToCharge; } }
    public bool IsReady { get { return charge >= hitsToCharge; } }
    public bool IsPlaying { get { return routine != null; } }
    public KeyCode ActivateKey { get { return activateKey; } }
    public KeyCode ChargeKey { get { return chargeKey; } }
    public int Damage { get { return damage; } }

    private Health health;
    private Fighter fighter;
    private PlayerController controller;
    private SpriteRenderer spriteRenderer;

    private int charge;
    private Sprite capturedIdle;
    private bool idleCaptured;
    private Coroutine routine;
    private float defaultFixedDelta = 0.02f;

    // sweepCurve 기본값의 두 구간 기울기(= 이동 속도).
    // 접선을 구간 기울기와 같게 두면 각 구간이 정확히 등속이 되고, 0.6 지점에서 딱 빨라진다.
    private const float SlowSlope = 0.25f / 0.6f;   // 앞 구간: 시간 60% 에 거리 25%
    private const float FastSlope = 0.75f / 0.4f;   // 뒷 구간: 시간 40% 에 거리 75%

    /// <summary>fixedDeltaTime 에 넣을 수 있는 하한. 이보다 작으면 Unity 가 받아주지 않는다.</summary>
    private const float MinFixedDelta = 0.001f;

    void Awake()
    {
        health = GetComponent<Health>();
        fighter = GetComponent<Fighter>();
        controller = GetComponent<PlayerController>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        defaultFixedDelta = Time.fixedDeltaTime;
    }

    void OnEnable()
    {
        health.OnDamaged += OnDamaged;
        // HUD 가 늦게 구독해도 현재 값을 받도록 한 번 알린다.
        OnChargeChanged?.Invoke(charge, hitsToCharge);
    }

    void OnDisable()
    {
        health.OnDamaged -= OnDamaged;

        // 연출 도중에 씬이 넘어가면 시간이 느려진 채로 남는다. 반드시 원복.
        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
            RestoreTime();
        }
    }

    void LateUpdate()
    {
        // 애니메이터가 스프라이트를 넣어준 뒤에야 읽을 수 있다.
        // 첫 프레임의 캐릭터는 서 있는 상태이므로 이때 잡으면 idle 이 잡힌다.
        if (!idleCaptured && spriteRenderer != null && spriteRenderer.sprite != null)
        {
            capturedIdle = spriteRenderer.sprite;
            idleCaptured = true;
        }
    }

    void Update()
    {
        // 게이지 채우기는 연출 확인용 단축키다.
        // 웹캠 인식으로 테스트하는 중에도 눌러야 하므로 useKeyboard 와 상관없이 항상 받는다.
        if (chargeKey != KeyCode.None && Input.GetKeyDown(chargeKey))
            AddCharge(chargeKeyAmount);

        if (controller != null && !controller.useKeyboard) return;
        if (Input.GetKeyDown(activateKey)) Activate();
    }

    /// <summary>게이지를 한 칸 채운다.</summary>
    public void AddCharge(int amount = 1)
    {
        if (amount <= 0 || charge >= hitsToCharge) return;

        charge = Mathf.Min(hitsToCharge, charge + amount);
        OnChargeChanged?.Invoke(charge, hitsToCharge);

        if (IsReady) Debug.Log($"[SpecialMove] {name} 필살기 준비 완료 ({activateKey} 키)");
    }

    /// <summary>라운드가 다시 시작될 때 게이지를 비운다.</summary>
    public void ResetCharge()
    {
        charge = 0;
        OnChargeChanged?.Invoke(charge, hitsToCharge);
    }

    /// <summary>
    /// 필살기 발동. 게이지가 안 찼거나 이미 연출 중이면 아무 일도 일어나지 않는다.
    /// 웹캠 인식에서 부를 수 있도록 public 으로 열어 둔다.
    /// </summary>
    public bool Activate()
    {
        if (!IsReady || IsPlaying) return false;
        if (health.IsDead || health.IsHitStunned) return false;
        if (fighter != null && (fighter.InputLocked || fighter.VisionPreparationLocked)) return false;

        charge = 0;
        OnChargeChanged?.Invoke(charge, hitsToCharge);

        routine = StartCoroutine(Routine());
        return true;
    }

    private void OnDamaged(bool guarded)
    {
        AddCharge();
    }

    /// <summary>
    /// 발동 즉시 상대 체력을 깎는다. 판정 박스도 모션도 없이 확정으로 들어간다.
    ///
    /// 무적시간을 무시하는 이유:
    /// 직전에 한 대 맞았다는 이유로 필살기가 씹히면 게이지를 쓴 보람이 없다.
    /// 가드도 보지 않는다 — 막아도 그대로 들어간다.
    /// </summary>
    private void ApplyDamage()
    {
        if (damage <= 0) return;

        var target = FindOpponent();
        if (target == null)
        {
            Debug.LogWarning($"[SpecialMove] {name} 때릴 상대를 찾지 못했습니다.");
            return;
        }

        bool damaged = target.TakeDamage(damage, Vector2.zero, false, true);
        Debug.Log($"[SpecialMove] {name} 필살기 → {target.DisplayName} 에게 {damage}");

        // 필살기 한 방에 쓰러졌으면 일반 공격의 마무리 일격과 같은 소리를 낸다.
        // 쓰러뜨리지 못한 필살기에는 타격음을 넣지 않는다.
        if (damaged && target.IsDead)
            CombatSfx.PlayHit(false, true);
    }

    /// <summary>
    /// 씬에서 자기 말고 살아 있는 Health 하나.
    /// 스테이지마다 적 프리팹이 교체되므로(BattleSceneBridge.ApplyStage) 미리 캐싱하지 않는다.
    /// </summary>
    private Health FindOpponent()
    {
        foreach (var h in FindObjectsByType<Health>(FindObjectsInactive.Exclude))
            if (h != health && !h.IsDead) return h;

        return null;
    }

    private IEnumerator Routine()
    {
        // 타격 히트스톱이 돌고 있으면 정리한다. 둘이 겹치면 서로를 덮어써서 연출이 끊긴다.
        HitImpact.ReleaseTimeControl();

        ApplyDamage();

        var cutIn = SpawnCutIn();

        float total = Mathf.Max(0.05f, duration);
        float recover = Mathf.Clamp(recoverTime, 0f, total);
        float hold = total - recover;

        // 카메라 확대는 HitImpact 에 맡긴다. timeScale 과 달리 확대는 주인이 하나뿐이라
        // 마무리 일격 연출과 같은 코드를 배율만 바꿔서 그대로 쓸 수 있다.
        float zIn = Mathf.Min(zoomInTime, total);
        float zOut = Mathf.Min(zoomOutTime, total - zIn);
        HitImpact.PlayZoom(transform.position, zoomRatio, zIn, total - zIn - zOut, zOut);

        // 필살기 한 방에 상대가 쓰러지면 그 순간 BattleSceneBridge 가 timeScale 주인이 된다.
        // 여기서 또 멈추면 K.O. 슬로우모션을 덮어쓰는데, 아래 루프는 InputLocked 를 보고
        // 곧바로 빠져나가므로 복귀도 못 하고 시간이 멈춘 채로 남는다.
        bool koTookOver = fighter != null && fighter.InputLocked;
        if (!koTookOver) SetTimeScale(slowScale);

        float t = 0f;
        bool aborted = false;

        while (t < total)
        {
            // K.O. 가 나면 timeScale 주인이 BattleSceneBridge 로 넘어간다. 여기서는 손을 뗀다.
            if (fighter != null && fighter.InputLocked)
            {
                aborted = true;
                break;
            }

            t += Time.unscaledDeltaTime;

            if (recover > 0f && t > hold)
                SetTimeScale(Mathf.Lerp(slowScale, 1f, (t - hold) / recover));

            UpdateCutIn(cutIn, t, total);
            yield return null;
        }

        if (cutIn != null) Destroy(cutIn.gameObject);

        // 중단된 경우에는 K.O. 연출이 정한 시간을 덮어쓰지 않는다.
        if (!aborted) RestoreTime();

        routine = null;
    }

    // ─────────────────────── 컷인 ───────────────────────

    private SpriteRenderer SpawnCutIn()
    {
        Sprite sprite = cutInSprite != null ? cutInSprite : capturedIdle;
        if (sprite == null)
        {
            Debug.LogWarning($"[SpecialMove] {name} 컷인에 쓸 스프라이트가 없습니다.");
            return null;
        }

        var go = new GameObject(name + " SpecialCutIn");
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = new Color(1f, 1f, 1f, 0f);

        if (spriteRenderer != null)
        {
            sr.sortingLayerID = spriteRenderer.sortingLayerID;
            sr.sortingOrder = spriteRenderer.sortingOrder + 20;   // 캐릭터보다 앞에
            sr.flipX = spriteRenderer.flipX;                      // 캐릭터와 같은 방향을 본다
        }

        // 캐릭터의 자식으로 붙이지 않는다. 뒤집힘이나 크기를 따라가면 컷인이 같이 일그러진다.
        go.transform.localScale = Vector3.one * cutInScale;
        go.transform.position = CutInPosition(sr, 0f);
        return sr;
    }

    private void UpdateCutIn(SpriteRenderer cutIn, float t, float total)
    {
        if (cutIn == null) return;

        float alpha;
        if (t < fadeInTime)               alpha = fadeInTime > 0f ? t / fadeInTime : 1f;
        else if (t > total - fadeOutTime) alpha = fadeOutTime > 0f ? (total - t) / fadeOutTime : 0f;
        else                              alpha = 1f;

        var c = cutIn.color;
        c.a = Mathf.Clamp01(alpha);
        cutIn.color = c;

        cutIn.transform.position = CutInPosition(cutIn, total > 0f ? t / total : 1f);
    }

    /// <summary>
    /// 컷인이 지나가는 위치.
    /// 캐릭터는 항상 상대를 바라보므로 Facing 이 곧 진행 방향이고, 그 반대쪽이 자기 진영이다.
    /// (왼쪽 진영 = 오른쪽을 봄 = Facing 1 → 왼쪽 끝에서 나타나 오른쪽으로 빠진다)
    ///
    /// 시작은 자기 진영 화면 끝에 딱 붙어 전부 보이는 자리, 끝은 반대편 화면 끝을 완전히 빠져나간 자리다.
    /// 이동 거리는 화면 폭과 같으므로 해상도나 카메라 크기가 달라져도 연출이 그대로 유지된다.
    /// </summary>
    private Vector3 CutInPosition(SpriteRenderer cutIn, float progress)
    {
        float y = transform.position.y + cutInHeight;

        var cam = Camera.main;
        if (cam == null || !cam.orthographic || cutIn == null)
            return new Vector3(transform.position.x, y, 0f);

        Vector3 half = cutIn.bounds.extents;
        float camHalfH = cam.orthographicSize;
        float camHalfW = camHalfH * cam.aspect;
        Vector3 c = cam.transform.position;

        int dir = (fighter != null && fighter.Facing < 0) ? -1 : 1;

        float startX = c.x - dir * (camHalfW - half.x);
        float endX   = c.x + dir * (camHalfW + half.x);

        float eased = (sweepCurve != null && sweepCurve.length > 0)
            ? sweepCurve.Evaluate(Mathf.Clamp01(progress))
            : Mathf.Clamp01(progress);

        // 세로는 화면 밖으로 나가지 않게 눌러 둔다. 컷인이 화면보다 크면 그냥 가운데 맞춤.
        float py = camHalfH > half.y
            ? Mathf.Clamp(y, c.y - camHalfH + half.y, c.y + camHalfH - half.y)
            : c.y;

        return new Vector3(Mathf.LerpUnclamped(startX, endX, eased), py, 0f);
    }

    // ─────────────────────── 시간 ───────────────────────

    private void SetTimeScale(float scale)
    {
        Time.timeScale = scale;

        // 물리 간격도 같이 줄여야 느려지는 동안 움직임이 뚝뚝 끊기지 않는다.
        // 다만 0 은 넣으면 안 된다. timeScale 이 0 이면 FixedUpdate 가 아예 돌지 않는데,
        // fixedDeltaTime 에 0 을 대입하는 것 자체를 Unity 가 오류로 막는다.
        Time.fixedDeltaTime = Mathf.Max(defaultFixedDelta * scale, MinFixedDelta);
    }

    private void RestoreTime()
    {
        Time.timeScale = 1f;
        Time.fixedDeltaTime = defaultFixedDelta > 0f ? defaultFixedDelta : 0.02f;
    }
}
