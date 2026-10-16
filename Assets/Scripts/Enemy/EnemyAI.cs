using UnityEngine;

/// <summary>
/// 상대 캐릭터 AI. 플레이어와의 거리에 따라 접근 / 공격 / 방어 / 백스텝을 반복한다.
/// 플레이어와 같은 Animator Controller(PlayerAnimator)를 사용한다.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Health))]
[RequireComponent(typeof(Fighter))]
public class EnemyAI : MonoBehaviour
{
    [Header("타겟")]
    [SerializeField] private Transform target;              // 비워두면 Player 를 자동으로 찾음

    [Header("이동")]
    [SerializeField] private float moveSpeed = 2.6f;
    [SerializeField] private float attackRange = 1.3f;      // 이 거리 안이면 공격 시도
    [SerializeField] private float keepDistance = 0.8f;     // 너무 붙으면 백스텝

    [Header("행동 주기")]
    [SerializeField] private float decisionInterval = 0.8f; // 몇 초마다 행동을 고를지

    [Header("행동 확률 (합이 1이 아니어도 됨)")]
    [SerializeField] private float attackWeight = 5f;
    [SerializeField] private float blockWeight = 3f;
    [SerializeField] private float backstepWeight = 1.5f;
    [SerializeField] private float crouchWeight = 1f;

    [Header("공격 딜레이")]
    [Tooltip("공격하기로 정한 뒤 공격 모션이 나가기까지 기다리는 시간(초). "
           + "그동안 제자리에 멈춰 있어 플레이어가 막거나 피할 틈이 생긴다. 0 이면 바로 공격한다.")]
    [Min(0f)]
    [SerializeField] private float attackDelay = 0.3f;

    private Rigidbody2D rb;
    private Animator animator;
    private SpriteRenderer spriteRenderer;
    private Health health;
    private Fighter fighter;
    private AttackWarning warning;

    private bool isGrounded = true;
    private float nextDecisionTime;
    private float backstepUntil;
    private float crouchUntil;

    // 딜레이 중인 공격. 기술은 공격을 정한 순간에 고정해 둔다.
    private bool attackPending;
    private float attackAt;
    private AttackHeight pendingHeight;
    private CombatMove pendingMove;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        health = GetComponent<Health>();
        fighter = GetComponent<Fighter>();

        // 공격 딜레이 동안 머리 위에 느낌표를 띄운다. 따로 붙여 두지 않았으면 기본 설정으로 만든다.
        warning = GetComponent<AttackWarning>();
        if (warning == null) warning = gameObject.AddComponent<AttackWarning>();
    }

    public void ConfigureStage(StageDefinition stage)
    {
        moveSpeed = Mathf.Max(0.1f, stage.moveSpeed);
        decisionInterval = Mathf.Max(0.1f, stage.decisionInterval);
        attackWeight = Mathf.Max(0.01f, stage.attackWeight);
        blockWeight = Mathf.Max(0f, stage.blockWeight);
        backstepWeight = Mathf.Max(0f, stage.backstepWeight);
        crouchWeight = Mathf.Max(0f, stage.crouchWeight);
    }

    void Start()
    {
        if (target == null)
        {
            var player = FindAnyObjectByType<PlayerController>();
            if (player != null) target = player.transform;
        }
        if (target == null)
            Debug.LogWarning("[EnemyAI] 타겟(Player)을 찾지 못했습니다.");
    }

    void Update()
    {
        // K.O. 연출 중에는 이긴 쪽도 행동하지 않는다. (BattleSceneBridge 가 켠다)
        if (fighter != null && fighter.InputLocked)
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

        if (target == null) return;

        FaceTarget();

        // 방어는 시전형이다(Fighter.TryGuard). 방어 중에는 Fighter 가 시전한 자세로 고정한다.
        fighter.IsCrouching = Time.time < crouchUntil;
        bool isBlocking = fighter.IsBlocking;
        bool isCrouching = fighter.IsCrouching;

        float distance = Mathf.Abs(target.position.x - transform.position.x);

        // ── 이동 결정 ──
        float moveX = 0f;
        if (fighter.CanAct && !isCrouching && !attackPending)
        {
            if (Time.time < backstepUntil)
                moveX = -fighter.Facing;                    // 뒤로 물러남
            else if (distance > attackRange)
                moveX = fighter.Facing;                     // 접근
            else if (distance < keepDistance)
                moveX = -fighter.Facing;                    // 너무 붙었으면 조금 뒤로
        }

        rb.linearVelocity = new Vector2(moveX * moveSpeed, rb.linearVelocity.y);
        SetAnimatorFlags(moveX != 0f, isCrouching, isBlocking);

        // ── 딜레이 중인 공격 ──
        // 기다리는 동안에는 제자리에 멈추고 다른 행동을 고르지 않는다.
        if (attackPending)
        {
            if (Time.time >= attackAt)
            {
                CancelPendingAttack();
                StartAttack();
            }
            return;
        }

        // ── 행동 결정 ──
        // 공격·방어·방어 실패 딜레이 중에는 새 행동을 고르지 않는다.
        if (Time.time >= nextDecisionTime && fighter.CanAct)
        {
            nextDecisionTime = Time.time + decisionInterval;
            Decide(distance);
        }
    }

    private void Decide(float distance)
    {
        // 사거리 밖이면 그냥 접근만 한다.
        if (distance > attackRange + 0.4f) return;

        float total = attackWeight + blockWeight + backstepWeight + crouchWeight;
        float roll = Random.value * total;

        if ((roll -= attackWeight) < 0f)
        {
            QueueAttack();
        }
        else if ((roll -= blockWeight) < 0f)
        {
            fighter.TryGuard(Time.time < crouchUntil);
        }
        else if ((roll -= backstepWeight) < 0f)
        {
            backstepUntil = Time.time + 0.3f;
        }
        else
        {
            crouchUntil = Time.time + 0.5f;
        }
    }

    private void QueueAttack()
    {
        bool crouching = Time.time < crouchUntil;
        pendingHeight = crouching ? AttackHeight.Low : AttackHeight.Mid;
        pendingMove = (CombatMove)Random.Range(0, 4);

        if (attackDelay <= 0f)
        {
            StartAttack();
            return;
        }

        attackPending = true;
        attackAt = Time.time + attackDelay;
        warning.Show(attackDelay);
    }

    /// <summary>딜레이 중인 공격을 접고 경고 표시를 내린다. 공격이 실제로 나갈 때도 이걸로 정리한다.</summary>
    private void CancelPendingAttack()
    {
        attackPending = false;
        warning.Hide();
    }

    private void StartAttack()
    {
        bool started = GetComponent<Fighter3DView>() != null
            ? fighter.TryAttack(pendingMove) : fighter.TryAttack(pendingHeight);
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
