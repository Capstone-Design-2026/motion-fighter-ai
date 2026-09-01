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
    [SerializeField] private float blockDuration = 0.7f;

    [Header("행동 확률 (합이 1이 아니어도 됨)")]
    [SerializeField] private float attackWeight = 5f;
    [SerializeField] private float blockWeight = 3f;
    [SerializeField] private float backstepWeight = 1.5f;
    [SerializeField] private float crouchWeight = 1f;

    private Rigidbody2D rb;
    private Animator animator;
    private SpriteRenderer spriteRenderer;
    private Health health;
    private Fighter fighter;

    private bool isGrounded = true;
    private float nextDecisionTime;
    private float blockUntil;
    private float backstepUntil;
    private float crouchUntil;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        health = GetComponent<Health>();
        fighter = GetComponent<Fighter>();
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
        if (health.IsDead)
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            SetAnimatorFlags(false, false, false);
            return;
        }

        if (health.IsHitStunned)
        {
            // 피격 경직 중에는 행동하지 않는다.
            // 속도를 건드리지 않아야 넉백이 정상적으로 밀린다.
            if (animator != null) animator.SetBool("IsMoving", false);
            return;
        }

        if (target == null) return;

        FaceTarget();

        bool isBlocking = Time.time < blockUntil;
        bool isCrouching = Time.time < crouchUntil;

        fighter.IsBlocking = isBlocking;
        fighter.IsCrouching = isCrouching;

        float distance = Mathf.Abs(target.position.x - transform.position.x);

        // ── 이동 결정 ──
        float moveX = 0f;
        if (!isBlocking && !isCrouching && !fighter.IsAttacking)
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

        // ── 행동 결정 ──
        if (Time.time >= nextDecisionTime)
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
            AttackNow();
        }
        else if ((roll -= blockWeight) < 0f)
        {
            blockUntil = Time.time + blockDuration;
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

    private void AttackNow()
    {
        bool crouching = Time.time < crouchUntil;
        AttackHeight height = crouching ? AttackHeight.Low : AttackHeight.Mid;

        if (fighter.TryAttack(height) && animator != null)
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
