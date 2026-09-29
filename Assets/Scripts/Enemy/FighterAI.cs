using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>AI 가 고를 수 있는 공격 패턴. 거리로 후보를 거른 뒤 가중치로 뽑는다.</summary>
[System.Serializable]
public class AIAttackPattern
{
    public string name = "중단 지르기";
    public AttackHeight height = AttackHeight.Mid;
    [Min(0f)] public float minRange = 0f;
    [Min(0f)] public float maxRange = 1.3f;
    [Min(0f)] public float weight = 1f;

    public bool InRange(float distance) => distance >= minRange && distance <= maxRange;
}

/// <summary>
/// 선제형 격투 AI. 자기 타이머로 공격을 주도하고, 플레이어 공격에는 방어로만 제한적으로 반응한다.
///
///   공격: 틱 확률 p = 1 - exp(-attackRate * dt), minAttackGap ~ maxAttackGap 사이에서 결정
///         Telegraph → Active → Recovery → Idle
///   방어: 플레이어 공격이 확정된 순간(Fighter.AttackDeclared, 모션 재생 전) 한 번만 굴리고
///         판정 프레임(Fighter.AttackHitFrame)에서 저장된 결과를 그대로 적용한다.
///
/// Fighter / Health / 애니메이터(PlayerAnimator)는 EnemyAI 와 똑같이 재사용한다.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Health))]
[RequireComponent(typeof(Fighter))]
public class FighterAI : MonoBehaviour
{
    public enum AIState { Idle, Move, Telegraph, Active, Recovery, Guard, Hit }

    [Header("타겟")]
    [SerializeField] private Transform target;              // 비워두면 Player 를 자동으로 찾음

    [Header("난이도 (비워두면 보통 수치)")]
    [SerializeField] private FighterAIDifficulty difficulty;

    [Header("공격 패턴")]
    [SerializeField] private List<AIAttackPattern> attackPatterns = new List<AIAttackPattern>
    {
        new AIAttackPattern { name = "중단 지르기", height = AttackHeight.Mid, minRange = 0f, maxRange = 1.3f, weight = 3f },
        new AIAttackPattern { name = "하단 차기",   height = AttackHeight.Low, minRange = 0f, maxRange = 1.1f, weight = 1.5f },
    };
    [Tooltip("직전에 쓴 패턴의 가중치에 곱하는 값. 같은 공격만 반복하지 않게 한다.")]
    [Range(0f, 1f)] [SerializeField] private float repeatWeightMultiplier = 0.35f;

    [Header("이동")]
    [SerializeField] private float moveSpeed = 2.6f;
    [SerializeField] private float keepDistance = 0.8f;     // 너무 붙으면 물러남
    [Tooltip("거리를 조금씩 좁히거나 벌리는 스텝 한 번의 길이.")]
    [SerializeField] private float stepDuration = 0.25f;
    [SerializeField] private Vector2 stepInterval = new Vector2(0.6f, 1.4f);
    [Tooltip("스텝이 앞으로 갈 확률. 나머지는 뒤로.")]
    [Range(0f, 1f)] [SerializeField] private float stepForwardChance = 0.6f;

    [Header("빈틈 (가드를 내려 공격을 유도)")]
    [Tooltip("초당 빈틈 발생률.")]
    [SerializeField] private float openingRate = 0.15f;
    [SerializeField] private float openingDuration = 0.8f;
    [Tooltip("빈틈 동안 guardChance 에 곱하는 값.")]
    [Range(0f, 1f)] [SerializeField] private float openingGuardMultiplier = 0.3f;

    [Header("가드")]
    [Tooltip("연속 가드가 이 횟수에 도달하면 가드가 풀린다 (가드 크러시).")]
    [Min(1)] [SerializeField] private int guardCrushCount = 3;
    [SerializeField] private float guardCrushStun = 0.6f;
    [Tooltip("가드 성공 뒤 자세를 유지하는 시간. 연타를 이어서 막을 수 있게 한다.")]
    [SerializeField] private float guardHoldTime = 0.25f;
    [Tooltip("판정 프레임이 끝내 오지 않을 때(상대 공격이 끊김) 가드를 푸는 여유 시간.")]
    [SerializeField] private float guardTimeout = 0.5f;

    [Header("반격")]
    [Tooltip("가드 후 반격의 예비동작 길이. 일반 공격보다 짧다.")]
    [SerializeField] private float counterTelegraph = 0.1f;

    [Header("플레이스홀더 (전용 애니메이션이 없는 상태)")]
    [SerializeField] private Color telegraphColor = new Color(1f, 0.65f, 0.2f, 1f);
    [SerializeField] private Color openingColor = new Color(0.6f, 1f, 0.6f, 1f);
    [SerializeField] private Color guardCrushColor = new Color(0.75f, 0.45f, 1f, 1f);
    [SerializeField] private bool debugLog = true;

    public AIState State { get; private set; } = AIState.Idle;

    private Rigidbody2D rb;
    private Animator animator;
    private SpriteRenderer spriteRenderer;
    private Collider2D bodyCollider;
    private Health health;
    private Fighter fighter;
    private Fighter playerFighter;

    private bool isGrounded = true;
    private Color baseColor = Color.white;
    private Color currentTint = Color.white;

    // 공격
    private float lastAttackEndTime;
    private int lastPatternIndex = -1;
    private Coroutine actionRoutine;

    // 이동
    private float stepUntil;
    private float nextStepTime;
    private float stepDir;
    private float openingUntil;

    // 방어
    private readonly Dictionary<int, bool> guardDecisions = new Dictionary<int, bool>();
    private int consecutiveGuards;
    private float guardHoldUntil;      // 0 이면 아직 판정 프레임 전
    private float guardTimeoutAt;
    private bool pendingCounter;
    private bool pendingCrush;
    private float crushUntil;

    private FighterAIDifficulty D => difficulty;
    private bool IsOpening => Time.time < openingUntil;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        bodyCollider = GetComponent<Collider2D>();
        health = GetComponent<Health>();
        fighter = GetComponent<Fighter>();

        if (difficulty == null) difficulty = FighterAIDifficulty.CreateDefault();
    }

    void Start()
    {
        if (spriteRenderer != null) baseColor = currentTint = spriteRenderer.color;

        if (target == null)
        {
            var player = FindAnyObjectByType<PlayerController>();
            if (player != null) target = player.transform;
        }
        if (target == null)
        {
            Debug.LogWarning("[FighterAI] 타겟(Player)을 찾지 못했습니다.");
        }
        else
        {
            playerFighter = target.GetComponent<Fighter>();
            if (playerFighter != null)
            {
                playerFighter.AttackDeclared += HandleAttackDeclared;
                playerFighter.AttackHitFrame += HandleAttackHitFrame;
            }
        }

        health.OnDamaged += HandleDamaged;
        lastAttackEndTime = Time.time;
        nextStepTime = Time.time + Random.Range(stepInterval.x, stepInterval.y);
    }

    void OnDestroy()
    {
        if (playerFighter != null)
        {
            playerFighter.AttackDeclared -= HandleAttackDeclared;
            playerFighter.AttackHitFrame -= HandleAttackHitFrame;
        }
        if (health != null) health.OnDamaged -= HandleDamaged;
    }

    /// <summary>스테이지 등에서 난이도를 바꿀 때 사용.</summary>
    public void SetDifficulty(FighterAIDifficulty value)
    {
        if (value != null) difficulty = value;
    }

    // ─────────────────────── 매 프레임 ───────────────────────

    void Update()
    {
        // K.O. 연출 중에는 이긴 쪽도 행동하지 않는다. (BattleSceneBridge 가 켠다)
        if (fighter.InputLocked || health.IsDead)
        {
            CancelAction();
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            fighter.IsBlocking = false;
            fighter.IsCrouching = false;
            SetAnimatorFlags(false);
            return;
        }

        // 피격 경직. 가드 경직은 Guard 상태가 직접 처리한다.
        // 속도를 건드리지 않아야 넉백이 정상적으로 밀린다.
        if (health.IsHitStunned && State != AIState.Guard)
        {
            if (State != AIState.Hit) EnterHit();
            SetAnimatorFlags(false);
            ApplyTint();
            return;
        }

        if (target == null) return;

        FaceTarget();
        float distance = Mathf.Abs(target.position.x - transform.position.x);
        float moveX = 0f;

        switch (State)
        {
            case AIState.Hit:
                if (Time.time >= crushUntil) State = AIState.Idle;
                break;

            case AIState.Idle:
            case AIState.Move:
                UpdateOpening();
                moveX = DecideMovement(distance);
                State = moveX != 0f ? AIState.Move : AIState.Idle;
                TryStartAttack(distance);
                break;

            case AIState.Guard:
                UpdateGuard(distance);
                break;

            // Telegraph / Active / Recovery 는 AttackSequence 코루틴이 진행한다.
        }

        if (State == AIState.Hit)
        {
            // 크러시 경직 중. 넉백이 밀리도록 속도를 건드리지 않는다.
            SetAnimatorFlags(false);
            ApplyTint();
            return;
        }

        rb.linearVelocity = new Vector2(moveX * moveSpeed, rb.linearVelocity.y);
        SetAnimatorFlags(moveX != 0f);
        ApplyTint();
    }

    // ─────────────────────── 이동 / 빈틈 ───────────────────────

    private float DecideMovement(float distance)
    {
        bool mustAttack = Time.time - lastAttackEndTime >= D.maxAttackGap;

        // 사거리 밖이면(또는 공격해야 하는데 후보가 없으면) 접근
        if (distance > MaxPatternRange() || (mustAttack && PickPattern(distance, out _) == null))
            return fighter.Facing;

        if (distance < keepDistance)
            return -fighter.Facing;

        // 거리를 조금씩 좁히거나 벌리는 스텝
        if (Time.time < stepUntil) return stepDir;

        if (Time.time >= nextStepTime)
        {
            stepDir = Random.value < stepForwardChance ? fighter.Facing : -fighter.Facing;
            stepUntil = Time.time + stepDuration;
            nextStepTime = stepUntil + Random.Range(stepInterval.x, stepInterval.y);
            return stepDir;
        }
        return 0f;
    }

    private void UpdateOpening()
    {
        if (IsOpening) return;
        if (Random.value < 1f - Mathf.Exp(-openingRate * Time.deltaTime))
        {
            openingUntil = Time.time + openingDuration;
            Log("빈틈 (가드를 내림) [플레이스홀더: 초록 틴트]");
        }
    }

    // ─────────────────────── 공격 ───────────────────────

    private void TryStartAttack(float distance)
    {
        float since = Time.time - lastAttackEndTime;
        if (since < D.minAttackGap) return;

        bool forced = since >= D.maxAttackGap;
        if (!forced)
        {
            float p = 1f - Mathf.Exp(-D.attackRate * Time.deltaTime);
            if (Random.value >= p) return;
        }

        var pattern = PickPattern(distance, out int index);
        if (pattern == null) return;   // 후보가 없으면 DecideMovement 가 접근한다

        StartAttack(pattern, index, D.telegraphDuration, false);
    }

    private AIAttackPattern PickPattern(float distance, out int index)
    {
        index = -1;
        float total = 0f;
        for (int i = 0; i < attackPatterns.Count; i++)
            if (attackPatterns[i].InRange(distance)) total += PatternWeight(i);
        if (total <= 0f) return null;

        float roll = Random.value * total;
        for (int i = 0; i < attackPatterns.Count; i++)
        {
            if (!attackPatterns[i].InRange(distance)) continue;
            roll -= PatternWeight(i);
            if (roll < 0f) { index = i; return attackPatterns[i]; }
        }

        // 부동소수 오차로 끝까지 온 경우 마지막 후보
        for (int i = attackPatterns.Count - 1; i >= 0; i--)
            if (attackPatterns[i].InRange(distance) && PatternWeight(i) > 0f) { index = i; return attackPatterns[i]; }
        return null;
    }

    private float PatternWeight(int i)
    {
        float w = attackPatterns[i].weight;
        return i == lastPatternIndex ? w * repeatWeightMultiplier : w;
    }

    private float MaxPatternRange()
    {
        float max = 0f;
        foreach (var p in attackPatterns) max = Mathf.Max(max, p.maxRange);
        return max;
    }

    private void StartAttack(AIAttackPattern pattern, int index, float telegraph, bool isCounter)
    {
        CancelAction();
        lastPatternIndex = index;
        consecutiveGuards = 0;
        actionRoutine = StartCoroutine(AttackSequence(pattern, telegraph, isCounter));
    }

    private IEnumerator AttackSequence(AIAttackPattern pattern, float telegraph, bool isCounter)
    {
        // ── Telegraph: 예비동작 ──
        State = AIState.Telegraph;
        rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
        fighter.IsBlocking = false;
        fighter.IsCrouching = pattern.height == AttackHeight.Low;
        Log((isCounter ? "반격 " : "") + "예비동작: " + pattern.name + " [플레이스홀더: 주황 틴트]");

        if (telegraph > 0f) yield return new WaitForSeconds(telegraph);

        // ── Active: 공격 판정 (Fighter 의 선딜 → 판정) ──
        if (!fighter.TryAttack(pattern.height))
        {
            FinishAttack();
            yield break;
        }
        if (animator != null) animator.SetTrigger("Attack");
        State = AIState.Active;
        yield return new WaitForSeconds(fighter.Windup);

        // ── Recovery: 후딜. 플레이어의 확정 반격 타이밍 ──
        State = AIState.Recovery;
        while (fighter.IsAttacking) yield return null;

        FinishAttack();
    }

    private void FinishAttack()
    {
        actionRoutine = null;
        fighter.IsCrouching = false;
        lastAttackEndTime = Time.time;
        State = AIState.Idle;
    }

    private void CancelAction()
    {
        if (actionRoutine != null)
        {
            StopCoroutine(actionRoutine);
            actionRoutine = null;
        }
    }

    // ─────────────────────── 방어 ───────────────────────

    private void HandleAttackDeclared(FighterAttack attack) => OnPlayerAttackRecognized(attack.Id, attack);
    private void HandleAttackHitFrame(FighterAttack attack) => OnPlayerAttackHitFrame(attack.Id, attack);

    /// <summary>
    /// 플레이어 공격이 인식된 직후, 아바타 공격 모션이 재생되기 전에 호출된다.
    /// 방어 확률은 여기서 한 번만 굴리고 attackId 별로 저장한다.
    /// </summary>
    public void OnPlayerAttackRecognized(int attackId, FighterAttack attack)
    {
        bool guard = false;
        string reason;

        if (State == AIState.Telegraph || State == AIState.Active || State == AIState.Recovery)
        {
            reason = "공격 중(" + State + ") → 피격 확정";
        }
        else if (State == AIState.Hit || health.IsDead)
        {
            reason = "경직 중 → 피격 확정";
        }
        else if (!AttackInRange(attack))
        {
            reason = "사거리 밖 → 굴리지 않음";
        }
        else
        {
            float chance = D.guardChance * (IsOpening ? openingGuardMultiplier : 1f);
            guard = Random.value < chance;
            reason = "방어 확률 " + chance.ToString("F2") + (IsOpening ? " (빈틈)" : "") + " → " + (guard ? "방어" : "피격");
        }

        guardDecisions[attackId] = guard;
        Log("플레이어 공격 #" + attackId + " " + attack.Height + ": " + reason);

        if (guard) EnterGuard(attack);
    }

    /// <summary>
    /// 아바타 공격의 판정 프레임. 저장된 결과를 그대로 적용한다 (다시 굴리지 않음).
    /// 이 직후 Fighter.ResolveHit 이 IsBlocking / IsCrouching 을 보고 가드 성립을 판정한다.
    /// </summary>
    public void OnPlayerAttackHitFrame(int attackId, FighterAttack attack)
    {
        if (!guardDecisions.TryGetValue(attackId, out bool guard)) guard = false;
        guardDecisions.Remove(attackId);

        if (!AttackInRange(attack))
        {
            Log("플레이어 공격 #" + attackId + " 헛침");
            if (State == AIState.Guard) ReleaseGuard();
            return;
        }

        if (guard && State == AIState.Guard)
        {
            // 판정 직전에 가드 자세를 확정한다. 하단은 앉아막기, 그 외는 서서막기.
            fighter.IsBlocking = true;
            fighter.IsCrouching = attack.Height == AttackHeight.Low;

            consecutiveGuards++;
            guardHoldUntil = Time.time + guardHoldTime;

            if (consecutiveGuards >= guardCrushCount)
            {
                pendingCrush = true;
            }
            else
            {
                pendingCounter = Random.value < D.counterChance;
                Log("가드 성공 (연속 " + consecutiveGuards + ")" + (pendingCounter ? " → 반격 예정" : ""));
            }
        }
        else if (State == AIState.Guard)
        {
            // 방어로 정했지만 그 사이 상태가 바뀌었거나 다른 공격 — 가드를 풀고 맞는다.
            ReleaseGuard();
        }
    }

    private void EnterGuard(FighterAttack attack)
    {
        CancelAction();
        State = AIState.Guard;
        guardHoldUntil = 0f;
        guardTimeoutAt = Time.time + D.guardStartDelay + attack.Attacker.Windup + guardTimeout;
        rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
        actionRoutine = StartCoroutine(RaiseGuard(D.guardStartDelay, attack.Height));
    }

    private IEnumerator RaiseGuard(float delay, AttackHeight height)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        actionRoutine = null;
        if (State != AIState.Guard) yield break;

        fighter.IsBlocking = true;
        fighter.IsCrouching = height == AttackHeight.Low;
    }

    private void UpdateGuard(float distance)
    {
        if (pendingCrush)
        {
            pendingCrush = false;
            pendingCounter = false;
            consecutiveGuards = 0;
            ReleaseGuard();
            State = AIState.Hit;
            crushUntil = Time.time + guardCrushStun;
            Log("가드 크러시! " + guardCrushStun + "초 무방비 [플레이스홀더: 보라 틴트]");
            return;
        }

        bool resolved = guardHoldUntil > 0f;
        if (!resolved)
        {
            // 판정 프레임이 오지 않았다 (플레이어 공격이 끊김)
            if (Time.time >= guardTimeoutAt) ReleaseGuard();
            return;
        }

        // 가드 경직과 유지 시간이 끝날 때까지 자세 유지. 그 사이 다음 공격이 오면 다시 굴린다.
        if (Time.time < guardHoldUntil || health.IsHitStunned) return;

        if (pendingCounter)
        {
            pendingCounter = false;
            var pattern = PickPattern(distance, out int index);
            ReleaseGuard();
            if (pattern != null)
            {
                StartAttack(pattern, index, counterTelegraph, true);
                return;
            }
        }
        ReleaseGuard();
    }

    private void ReleaseGuard()
    {
        CancelAction();
        fighter.IsBlocking = false;
        fighter.IsCrouching = false;
        guardHoldUntil = 0f;
        if (State == AIState.Guard) State = AIState.Idle;
    }

    private void HandleDamaged(bool guarded)
    {
        if (guarded) return;
        EnterHit();
    }

    private void EnterHit()
    {
        CancelAction();
        fighter.IsBlocking = false;
        fighter.IsCrouching = false;
        guardHoldUntil = 0f;
        pendingCounter = false;
        pendingCrush = false;
        consecutiveGuards = 0;
        openingUntil = 0f;
        if (State == AIState.Telegraph || State == AIState.Active || State == AIState.Recovery)
            lastAttackEndTime = Time.time;
        State = AIState.Hit;
    }

    /// <summary>플레이어 공격이 나에게 닿는 거리인지. 공격자가 나를 보고 있어야 한다.</summary>
    private bool AttackInRange(FighterAttack attack)
    {
        if (attack == null || attack.Attacker == null) return false;
        float dx = transform.position.x - attack.Attacker.transform.position.x;
        if (dx * attack.Attacker.Facing <= 0f) return false;

        float bodyHalf = bodyCollider != null ? bodyCollider.bounds.extents.x : 0.3f;
        return Mathf.Abs(dx) <= attack.Reach + bodyHalf;
    }

    // ─────────────────────── 표시 ───────────────────────

    private void FaceTarget()
    {
        bool targetOnRight = target.position.x > transform.position.x;
        fighter.Facing = targetOnRight ? 1 : -1;

        // 원본 스프라이트가 오른쪽을 보고 있으므로, 왼쪽을 볼 때 flip
        if (spriteRenderer != null)
            spriteRenderer.flipX = !targetOnRight;
    }

    private void SetAnimatorFlags(bool moving)
    {
        if (animator == null) return;
        animator.SetBool("IsMoving", moving);
        animator.SetBool("IsCrouching", fighter.IsCrouching);
        animator.SetBool("IsBlocking", fighter.IsBlocking);
        animator.SetBool("IsGrounded", isGrounded);
    }

    /// <summary>전용 애니메이션이 없는 상태를 색으로 표시한다. 바뀔 때만 칠해서 Health 의 피격 플래시를 덮지 않는다.</summary>
    private void ApplyTint()
    {
        if (spriteRenderer == null || health.IsDead) return;

        Color want = baseColor;
        if (State == AIState.Telegraph) want = telegraphColor;
        else if (State == AIState.Hit && Time.time < crushUntil) want = guardCrushColor;
        else if (IsOpening && (State == AIState.Idle || State == AIState.Move)) want = openingColor;

        if (want == currentTint) return;
        currentTint = want;
        spriteRenderer.color = want;
    }

    private void Log(string message)
    {
        if (debugLog) Debug.Log("[FighterAI] " + message);
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
