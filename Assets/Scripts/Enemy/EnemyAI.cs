using UnityEngine;

/// <summary>스테이지별로 고르는 적 AI 행동 타입.</summary>
public enum EnemyBehaviorType
{
    [InspectorName("밸런스 (세 타입 혼합)")] Balanced,
    [InspectorName("공격 위주")] Aggressive,
    [InspectorName("반격 위주")] Counter,
    [InspectorName("아웃 복싱")] OutBoxer
}

/// <summary>
/// 상대 캐릭터 AI. 행동 타입에 따라 움직인다.
///   공격 위주: 거리를 좁히고, 공격 거리에 들어오면 바로 공격한다.
///   반격 위주: 공격 거리에서 기다리다가 상대 공격 신호가 오면 방어하고 반격한다.
///   아웃 복싱: 공격 거리를 드나들며 헛치게 만들고, 상대 후딜에 파고들어 공격한다.
///   밸런스  : 위 세 타입을 일정 시간마다 바꿔 가며 쓴다.
/// 플레이어와 같은 Animator Controller(PlayerAnimator)를 사용한다.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Health))]
[RequireComponent(typeof(Fighter))]
public class EnemyAI : MonoBehaviour
{
    [Header("타겟")]
    [SerializeField] private Transform target;              // 비워두면 Player 를 자동으로 찾음

    [Header("행동 타입")]
    [SerializeField] private EnemyBehaviorType behaviorType = EnemyBehaviorType.Balanced;

    [Header("이동")]
    [SerializeField] private float moveSpeed = 2.6f;
    [SerializeField] private float attackRange = 1.3f;      // 이 거리 안이면 공격 시도
    [SerializeField] private float keepDistance = 0.8f;     // 반격 타입이 기다릴 때 유지하는 최소 거리

    [Header("행동 주기")]
    [Tooltip("작을수록 활발하다. 이 값의 4배 동안 상대가 공격하지 않으면 반격·아웃 복싱 타입도 먼저 공격하고, 밸런스 타입은 그 정도 간격으로 타입을 바꾼다.")]
    [SerializeField] private float decisionInterval = 0.8f;

    [Header("밸런스 타입 혼합 비율 (합이 1이 아니어도 됨)")]
    [Tooltip("공격 위주 타입을 쓸 비율.")]
    [SerializeField] private float attackWeight = 5f;
    [Tooltip("반격 위주 타입을 쓸 비율.")]
    [SerializeField] private float blockWeight = 3f;
    [Tooltip("아웃 복싱 타입을 쓸 비율.")]
    [SerializeField] private float backstepWeight = 1.5f;

    [Header("공격 딜레이")]
    [Tooltip("공격하기로 정한 뒤 공격 모션이 나가기까지 기다리는 시간(초). "
           + "그동안 제자리에 멈춰 있어 플레이어가 막거나 피할 틈이 생긴다. 0 이면 바로 공격한다. 반격과 후딜 공략에는 적용하지 않는다.")]
    [Min(0f)]
    [SerializeField] private float attackDelay = 0.3f;
    [Tooltip("플레이어가 파운장 기를 모으는 것을 보고 방어를 시도할 확률. 방어는 시간제라 너무 오래 모으면 풀린다.")]
    [Range(0f, 1f)] [SerializeField] private float specialGuardChance = 0.5f;
    private SpecialMove targetSpecial;
    private bool specialSeen;
    private float specialGuardAt = -1f;

    [Header("반격 위주")]
    [Tooltip("상대 공격 신호를 보고 방어를 시작하기까지의 평균 시간(초). 매번 이 값의 0.5~1.5배로 흔들린다.")]
    [Min(0f)]
    [SerializeField] private float reactionTime = 0.15f;

    [Tooltip("AI 에는 입력 키가 없으므로, 방어를 시작하면 최대 이 시간(초) 동안 누르고 있는 것으로 친다. 공격을 막아내거나 상대 공격이 끝나면 바로 푼다.")]
    [Min(0.05f)]
    [SerializeField] private float guardHoldTime = 1.5f;

    [Header("아웃 복싱")]
    [Tooltip("공격 거리 바깥으로 얼마나 빠져나갈지.")]
    [Min(0.1f)]
    [SerializeField] private float outBoxMargin = 0.5f;
    [Tooltip("드나들 때의 이동 속도 배율.")]
    [Min(0.1f)]
    [SerializeField] private float outBoxSpeedScale = 1.5f;

    private const float OutBoxLegTimeout = 1.5f;   // 벽에 막혀도 계속 빠지려 하지 않도록

    private Rigidbody2D rb;
    private Animator animator;
    private SpriteRenderer spriteRenderer;
    private Health health;
    private Fighter fighter;
    private Fighter targetFighter;

    private bool isGrounded = true;
    private bool aiGuarding;          // AI 가 시작한 방어를 유지하는 중인지
    private float guardReleaseAt;

    // 딜레이 중인 공격. 기술은 공격을 정한 순간에 고정해 둔다.
    private bool attackPending;
    private float attackAt;
    private AttackHeight pendingHeight;
    private CombatMove pendingMove;

    // 타입별 진행 상태
    private EnemyBehaviorType balancedMode = EnemyBehaviorType.Aggressive;
    private float balancedUntil;
    private float patienceUntil;      // 이때까지 상대가 공격하지 않으면 먼저 공격한다
    private bool counterReady;        // 방어가 끝나 반격할 차례인지
    private float reactAt = -1f;      // 공격 신호에 반응해 방어를 시작할 시각
    private bool outEntering = true;  // 공격 거리로 들어가는 중인지
    private float outDwellUntil;
    private float outLegUntil;

    public EnemyBehaviorType BehaviorType => behaviorType;
    private EnemyBehaviorType ActiveMode => behaviorType == EnemyBehaviorType.Balanced ? balancedMode : behaviorType;
    private float ThinkPeriod => decisionInterval * 4f;
    private bool Is3D => GetComponent<Fighter3DView>() != null;

    [Tooltip("3D: 잽이 닿는 가장 먼 거리보다 이만큼 더 다가가서 공격한다(m).")]
    [SerializeField] private float reachMargin = 0.04f;
    private Animator reachAnimator;
    private CombatReach.Profile jabReach;

    /// <summary>
    /// 이 거리 안에 들어오면 공격을 시작한다(몸 중심 사이 거리).
    /// 3D 는 캐릭터마다 실제 잽(왼주먹)이 상대 몸통 판정에 닿는 가장 먼 거리 − 여유이고, 최대 1.0m 다.
    /// 키 작은 상대가 1.0m 에서 헛치던 문제를 막는다. 잽이 가장 짧은 공격이라 다른 공격도 이 거리면 닿는다.
    /// </summary>
    public float StrikeRange => Is3D ? Mathf.Min(attackRange, 1.0f, ReachStrikeRange) : attackRange;

    /// <summary>잰 잽 사거리(몸 중심 → 주먹 앞면, 앞쪽 거리만). 아직 못 쟀으면 -1.</summary>
    public float MeasuredJabReach => jabReach != null ? jabReach.forwardReach : -1f;

    private float ReachStrikeRange
    {
        get
        {
            var view = GetComponent<Fighter3DView>();
            if (view == null || view.modelAnimator == null) return 1.0f;
            if (view.modelAnimator != reachAnimator)
            {
                reachAnimator = view.modelAnimator;
                jabReach = CombatReach.MeasureJab(view, transform);
            }
            if (jabReach == null) return 1.0f;
            if (target == null) FindTarget();
            float hit = CombatReach.MaxHitDistance(jabReach, fighter, TargetFighter);
            return hit > 0f ? Mathf.Max(0.6f, hit - reachMargin) : 1.0f;
        }
    }

    private void FindTarget()
    {
        var player = FindAnyObjectByType<PlayerController>();
        if (player != null) target = player.transform;
    }

    private Fighter TargetFighter
    {
        get
        {
            if (targetFighter == null && target != null) targetFighter = target.GetComponent<Fighter>();
            return targetFighter;
        }
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        health = GetComponent<Health>();
        fighter = GetComponent<Fighter>();
        // 적 필살기(게이지, 스스로 발동, 스테이지당 1회)는 EnemySpecial 이 맡는다. 적 프리팹마다 붙이지 않아도 되게 여기서 붙인다.
        if (GetComponent<EnemySpecial>() == null) gameObject.AddComponent<EnemySpecial>();
    }

    void OnEnable() { fighter.GuardBlocked += OnGuardBlocked; }
    void OnDisable() { fighter.GuardBlocked -= OnGuardBlocked; }

    private void OnGuardBlocked()
    {
        if (!aiGuarding) return;
        guardReleaseAt = 0f;
        if (ActiveMode == EnemyBehaviorType.Counter) counterReady = true;
    }

    public void ConfigureStage(StageDefinition stage)
    {
        moveSpeed = Mathf.Max(0.1f, stage.moveSpeed);
        decisionInterval = Mathf.Max(0.1f, stage.decisionInterval);
        attackWeight = Mathf.Max(0.01f, stage.attackWeight);
        blockWeight = Mathf.Max(0f, stage.blockWeight);
        backstepWeight = Mathf.Max(0f, stage.backstepWeight);
        ResetModeState();
    }

    public void SetBehavior(EnemyBehaviorType type)
    {
        behaviorType = type;
        balancedUntil = 0f;
        ResetModeState();
    }

    private void ResetModeState()
    {
        patienceUntil = Time.time + ThinkPeriod;
        counterReady = false;
        reactAt = -1f;
        outEntering = true;
        outDwellUntil = 0f;
        outLegUntil = 0f;
    }

    void Start()
    {
        if (target == null) FindTarget();
        if (target == null)
            Debug.LogWarning("[EnemyAI] 타겟(Player)을 찾지 못했습니다.");
        ResetModeState();
    }

    void Update()
    {
        // Calibration pauses combat, not the resting facing direction.
        if (fighter != null && fighter.VisionPreparationLocked && !fighter.InputLocked && !health.IsDead && target != null)
            FaceTarget();
        // K.O. 연출 중에는 이긴 쪽도 행동하지 않는다. (BattleSceneBridge 가 켠다)
        if (fighter != null && (fighter.InputLocked || fighter.VisionPreparationLocked))
        {
            CancelPendingAttack();
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            fighter.IsCrouching = false;
            SetAnimatorFlags(false, false, false);
            return;
        }

        if (health.IsDead)
        {
            CancelPendingAttack();
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            SetAnimatorFlags(false, false, false);
            return;
        }

        if (health.IsHitStunned)
        {
            // 맞으면 딜레이 중이던 공격은 취소된다.
            CancelPendingAttack();

            // 피격 경직 중에는 행동하지 않는다.
            // 속도를 건드리지 않아야 넉백이 정상적으로 밀린다.
            if (animator != null) animator.SetBool("IsMoving", false);
            return;
        }

        // 정해 둔 시간이 지나거나 공격을 막아내면 방어 입력을 뗀다.
        if (aiGuarding && (!fighter.IsGuarding || Time.time >= guardReleaseAt))
        {
            aiGuarding = false;
            fighter.ReleaseGuard();
        }

        if (target == null) return;

        FaceTarget();
        ReactToSpecial();

        float distance = Mathf.Abs(target.position.x - transform.position.x);

        // 공격 중이거나 공격을 기다리는 동안에는 제자리에 멈추고 다른 행동을 고르지 않는다.
        float moveX = 0f;
        if (fighter.CanAttack && !attackPending) moveX = Think(distance);

        float travelSpeed = moveSpeed * (Is3D ? Fighter3DView.BoxingTravelScale : 1f);
        rb.linearVelocity = new Vector2(moveX * travelSpeed, rb.linearVelocity.y);
        SetAnimatorFlags(moveX != 0f, false, fighter.IsBlocking);

        if (attackPending && Time.time >= attackAt)
        {
            CancelPendingAttack();
            StartAttack();
        }
    }

    /// <summary>
    /// 플레이어가 파운장 기를 모으기 시작하면 한 번 판단한다. 확률에 걸리면 반응 시간 뒤 방어를 시작한다.
    /// AI 방어는 guardHoldTime 뒤에 스스로 풀리므로, 플레이어가 오래 모으면 방어가 먼저 풀릴 수 있다(심리전).
    /// </summary>
    private void ReactToSpecial()
    {
        if (targetSpecial == null && target != null) targetSpecial = target.GetComponent<SpecialMove>();
        if (targetSpecial == null || !targetSpecial.IsCharging) { specialSeen = false; specialGuardAt = -1f; return; }
        if (!specialSeen)
        {
            specialSeen = true;
            specialGuardAt = Random.value < specialGuardChance ? Time.time + Random.Range(0.25f, 0.6f) : -1f;
            if (specialGuardAt > 0f) CancelPendingAttack();
        }
        if (specialGuardAt > 0f && Time.time >= specialGuardAt && fighter.CanAttack)
        {
            StartGuard();
            specialGuardAt = -1f;
        }
    }

    /// <summary>이번 프레임에 할 행동을 정하고, 이동 방향(속도 배율 포함)을 돌려준다.</summary>
    private float Think(float distance)
    {
        if (behaviorType == EnemyBehaviorType.Balanced) UpdateBalancedMode();

        switch (ActiveMode)
        {
            case EnemyBehaviorType.Counter:  return ThinkCounter(distance);
            case EnemyBehaviorType.OutBoxer: return ThinkOutBoxer(distance);
            default:                         return ThinkAggressive(distance);
        }
    }

    private void UpdateBalancedMode()
    {
        if (Time.time < balancedUntil || aiGuarding) return;

        float total = attackWeight + blockWeight + backstepWeight;
        float roll = Random.value * total;
        if (total <= 0f || (roll -= attackWeight) < 0f) balancedMode = EnemyBehaviorType.Aggressive;
        else if ((roll -= blockWeight) < 0f)            balancedMode = EnemyBehaviorType.Counter;
        else                                            balancedMode = EnemyBehaviorType.OutBoxer;

        balancedUntil = Time.time + ThinkPeriod * Random.Range(0.75f, 1.25f);
        ResetModeState();
    }

    /// <summary>거리를 좁히고, 공격 거리에 들어오면 바로 공격한다.</summary>
    private float ThinkAggressive(float distance)
    {
        if (distance > StrikeRange) return fighter.Facing;
        QueueAttack(false);
        return 0f;
    }

    /// <summary>공격 거리에서 기다리다가 상대 공격 신호가 오면 방어하고, 끝나면 바로 반격한다.</summary>
    private float ThinkCounter(float distance)
    {
        bool signal = fighter.IncomingMove.HasValue;
        if (signal) patienceUntil = Time.time + ThinkPeriod;

        if (counterReady)
        {
            counterReady = false;
            if (distance <= StrikeRange)
            {
                QueueAttack(true);
                return 0f;
            }
        }

        if (aiGuarding)
        {
            // 막지 못하고 상대 공격이 끝났어도(빗나감) 후딜을 노려 반격한다.
            if (!signal)
            {
                guardReleaseAt = 0f;
                counterReady = true;
            }
            return 0f;
        }

        if (signal)
        {
            if (reactAt < 0f) reactAt = Time.time + reactionTime * Random.Range(0.5f, 1.5f);
            if (Time.time >= reactAt) StartGuard();
            return 0f;
        }
        reactAt = -1f;

        if (distance > StrikeRange) return fighter.Facing;

        // 상대가 끝까지 공격하지 않으면 교착되므로 먼저 한 번 건드린다.
        if (Time.time >= patienceUntil)
        {
            patienceUntil = Time.time + ThinkPeriod;
            QueueAttack(false);
            return 0f;
        }

        return distance < keepDistance ? -fighter.Facing : 0f;
    }

    /// <summary>공격 거리를 드나들며 헛치게 만들고, 상대 후딜에 파고들어 공격한다.</summary>
    private float ThinkOutBoxer(float distance)
    {
        float toward = fighter.Facing * outBoxSpeedScale;

        // 상대가 헛쳐서 후딜에 묶여 있다. 파고들어 친다.
        if (TargetFighter != null && TargetFighter.IsInRecovery)
        {
            if (distance > StrikeRange) return toward;
            QueueAttack(true);
            return 0f;
        }

        float far = StrikeRange + outBoxMargin;

        // 상대가 공격을 시작했다. 사거리 밖으로 빠져나가 헛치게 만든다.
        if (fighter.IncomingMove.HasValue)
        {
            patienceUntil = Time.time + ThinkPeriod;
            outEntering = false;
            outDwellUntil = 0f;
            outLegUntil = Time.time + OutBoxLegTimeout;
            return distance < far ? -toward : 0f;
        }

        if (Time.time < outDwellUntil) return 0f;

        if (outEntering)
        {
            if (distance > StrikeRange - 0.05f) return toward;

            // 들어왔다. 상대가 끝까지 공격하지 않으면 교착되므로 먼저 한 번 건드린다.
            outEntering = false;
            outDwellUntil = Time.time + 0.15f;
            outLegUntil = outDwellUntil + OutBoxLegTimeout;
            if (Time.time >= patienceUntil)
            {
                patienceUntil = Time.time + ThinkPeriod;
                QueueAttack(false);
            }
            return 0f;
        }

        if (distance >= far || Time.time >= outLegUntil)
        {
            outEntering = true;
            outDwellUntil = Time.time + Random.Range(0.15f, 0.4f);
            return 0f;
        }
        return -toward;
    }

    private void StartGuard()
    {
        if (aiGuarding || !fighter.TryGuard(false)) return;
        aiGuarding = true;
        guardReleaseAt = Time.time + guardHoldTime;
    }

    /// <summary>공격을 정한다. punish 면 빠른 기술로 딜레이 없이 바로 나간다(반격, 후딜 공략).</summary>
    private void QueueAttack(bool punish)
    {
        // 공격하기로 정했으면 방어 입력을 뗀다.
        if (aiGuarding) guardReleaseAt = 0f;
        reactAt = -1f;

        pendingHeight = AttackHeight.Mid;
        pendingMove = (CombatMove)(punish ? Random.Range(0, 2) : Random.Range(0, 4));

        // 공격을 정한 이 순간 상대에게 알린다. 상대 방어 자세·모션이 이 공격에 맞춰진다.
        fighter.DeclareIntent(Is3D ? pendingMove : CombatMove.Straight);

        float delay = punish ? 0f : attackDelay;
        if (delay <= 0f)
        {
            StartAttack();
            return;
        }

        attackPending = true;
        attackAt = Time.time + delay;
    }

    /// <summary>딜레이 중인 공격을 접는다. 공격이 실제로 나갈 때도 이걸로 정리한다.</summary>
    private void CancelPendingAttack()
    {
        // 기다리던 공격을 접으면 예고도 거둔다. 실제로 공격이 나가면 Fighter.TryAttack 이 다시 알린다.
        if (attackPending) fighter.ClearIntent();
        attackPending = false;
    }

    private void StartAttack()
    {
        bool started = Is3D ? fighter.TryAttack(pendingMove) : fighter.TryAttack(pendingHeight);
        if (started && animator != null)
            animator.SetTrigger("Attack");
    }

    private void FaceTarget()
    {
        bool targetOnRight = target.position.x > transform.position.x;
        fighter.Facing = targetOnRight ? 1 : -1;

        // 원본 스프라이트가 오른쪽을 보고 있으므로, 왼쪽을 볼 때 flip
        if (spriteRenderer != null)
            spriteRenderer.flipX = !targetOnRight;
    }

    private void SetAnimatorFlags(bool moving, bool crouching, bool blocking)
    {
        if (animator == null) return;
        animator.SetBool("IsMoving", moving);
        animator.SetBool("IsCrouching", crouching);
        animator.SetBool("IsBlocking", blocking);
        animator.SetBool("IsGrounded", isGrounded);
    }

    void OnCollisionEnter2D(Collision2D col)
    {
        if (col.gameObject.CompareTag("Ground"))
        {
            isGrounded = true;
            if (animator != null) animator.SetBool("IsGrounded", true);
        }
    }

    void OnCollisionExit2D(Collision2D col)
    {
        if (col.gameObject.CompareTag("Ground"))
        {
            isGrounded = false;
            if (animator != null) animator.SetBool("IsGrounded", false);
        }
    }
}
