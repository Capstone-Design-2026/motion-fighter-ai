using System;
using UnityEngine;

[RequireComponent(typeof(Health))]
[RequireComponent(typeof(Fighter))]
public class PlayerController : MonoBehaviour
{
    private Animator animator;
    private Rigidbody2D rb;
    private SpriteRenderer spriterenderer;
    private Health health;
    private Fighter fighter;

    [Header("이동")]
    public float moveSpeed = 5f;
    public float jumpForce = 12f;

    // ── 테스트용 키 배치 ────────────────────────────────────────────
    // 최종적으로는 웹캠 동작인식(UDP)이 입력을 담당하므로, 아래 키는
    // 인식 없이 전투를 굴려보기 위한 임시 수단이다.
    // 인식 테스트만 하고 싶으면 useKeyboard 를 꺼라.
    [Header("테스트 키 (Inspector 에서 변경 가능)")]
    [Tooltip("끄면 키보드 입력을 완전히 무시한다. 웹캠 인식만으로 테스트할 때 사용.")]
    public bool useKeyboard = true;
    public KeyCode moveLeftKey  = KeyCode.A;
    public KeyCode moveRightKey = KeyCode.D;
    public KeyCode jumpKey      = KeyCode.W;
    public KeyCode crouchKey    = KeyCode.S;
    public KeyCode attackKey    = KeyCode.P;
    [Tooltip("누르고 있는 동안 막기. 기본값 U.")]
    public KeyCode blockKey     = KeyCode.U;

    [Header("상대 (격투게임 방향 처리)")]
    [Tooltip("비워두면 씬에서 EnemyAI 를 자동으로 찾는다. 항상 이쪽을 바라본다.")]
    [SerializeField] private Transform opponent;
    [Tooltip("뒤로 걸을 때 속도 배율. 격투게임은 보통 후진이 조금 느리다.")]
    [SerializeField] private float backwardSpeedMultiplier = 0.8f;
    [Tooltip("앉은 채로 이동할 때 속도 배율. 0 으로 두면 앉아서 못 움직인다.")]
    [SerializeField] private float crouchSpeedMultiplier = 0.45f;

    private bool isGrounded = true;
    private bool isCrouching = false;
    private bool isBlocking = false;
    public String currentAction = "IDLE";

    // UDP(웹캠 동작인식) 패킷이 한 번이라도 들어오면 true 가 된다.
    // 이때부터 앉기/막기 자세는 키보드가 아니라 인식 결과가 결정한다.
    // (이동/점프/공격 키는 테스트용으로 계속 살아있다)
    public bool externalControl = false;
    private float externalMoveX;

    public void SetOpponent(Transform value) { opponent = value; }

    void Start()
    {
        animator = GetComponent<Animator>();
        rb = GetComponent<Rigidbody2D>();
        spriterenderer = GetComponent<SpriteRenderer>();
        health = GetComponent<Health>();
        fighter = GetComponent<Fighter>();

        // 상대를 지정하지 않았으면 씬에서 찾는다. (EnemyAI 가 플레이어를 찾는 것과 대칭)
        if (opponent == null)
        {
            var enemy = FindAnyObjectByType<EnemyAI>();
            if (enemy != null) opponent = enemy.transform;
        }

        // 파이썬(UDP) 연동은 UdpActionReceiver 가 ExternalAction() 을 호출하는 방식으로 처리한다.
    }

    void Update()
    {
        // K.O. 연출 중. 진 쪽은 물론 이긴 쪽도 움직이지 못한다.
        // 물리는 그대로 돌아가므로 쓰러지는 모션과 넉백은 끝까지 재생된다.
        if (fighter != null && fighter.InputLocked)
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            isCrouching = false;
            isBlocking  = false;
            fighter.IsCrouching = false;
            fighter.IsBlocking  = false;
            animator.SetBool("IsMoving", false);
            animator.SetBool("IsMovingBack", false);
            animator.SetBool("IsCrouching", false);
            animator.SetBool("IsBlocking", false);
            return;
        }

        if (health.IsDead)
        {
            // 쓰러진 뒤에는 조작 불가. IsDead 파라미터는 Health 가 켠다.
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            animator.SetBool("IsMoving", false);
            animator.SetBool("IsMovingBack", false);
            animator.SetBool("IsCrouching", false);
            animator.SetBool("IsBlocking", false);
            return;
        }

        if (health.IsHitStunned)
        {
            // 피격 경직 중에는 입력을 받지 않는다.
            // 속도를 건드리지 않아야 넉백이 정상적으로 밀린다.
            animator.SetBool("IsMoving", false);
            animator.SetBool("IsMovingBack", false);
            return;
        }

        HandleKeyInput();
    }

    void HandleKeyInput()
    {
        // ── 유지 상태: 누르고 있는 동안 true ──
        // 외부(웹캠) 제어 중에는 ExternalAction() 이 정한 자세를 덮어쓰지 않는다.
        if (useKeyboard && !externalControl)
        {
            isCrouching = Input.GetKey(crouchKey);
            isBlocking  = Input.GetKey(blockKey);
        }

        animator.SetBool("IsCrouching", isCrouching);
        animator.SetBool("IsBlocking",  isBlocking);

        // 전투 시스템에 현재 자세를 알려준다 (방어 성립 판정에 사용)
        fighter.IsCrouching = isCrouching;
        fighter.IsBlocking  = isBlocking;

        // ── 이동 (막기/앉기 중에는 정지) ──
        // 이동으로 캐릭터를 회전시키지 않는다. 방향은 아래 FaceOpponent() 가 정한다.
        // 막는 중에는 못 움직인다. 앉은 채로는 느리게 움직일 수 있다.
        float moveX = 0f;
        if (!isBlocking)
        {
            if (externalControl) moveX = externalMoveX;
            // A/D still work for testing when the vision detector reports neutral movement.
            if (useKeyboard && Mathf.Abs(moveX) < 0.01f)
            {
                if (Input.GetKey(moveLeftKey))       moveX = -1f;
                else if (Input.GetKey(moveRightKey)) moveX =  1f;
            }
        }

        // 바라보는 방향을 먼저 확정해야 전진/후진 판정이 맞는다.
        FaceOpponent(moveX);

        // 바라보는 방향과 이동 방향이 반대면 후진.
        bool movingBack = Mathf.Abs(moveX) > 0.01f && moveX * fighter.Facing < 0f;

        float speed = moveSpeed;
        if (movingBack)  speed *= backwardSpeedMultiplier;
        if (isCrouching) speed *= crouchSpeedMultiplier;

        rb.linearVelocity = new Vector2(moveX * speed, rb.linearVelocity.y);
        animator.SetBool("IsMoving", Mathf.Abs(moveX) > 0.01f);
        animator.SetBool("IsMovingBack", movingBack);

        if (!useKeyboard) return;

        // ── 점프 (막는 중엔 불가) ──
        if (Input.GetKeyDown(jumpKey) && isGrounded && !isBlocking)
        {
            DoJump();
        }

        // ── 공격 (막는 중엔 불가) ──
        if (Input.GetKeyDown(attackKey) && !isBlocking)
        {
            DoAttack();
        }
    }

    /// <summary>
    /// 항상 상대를 바라본다. 격투게임이므로 이동 방향이 아니라 상대 위치가 방향을 정한다.
    /// 이래야 뒤로 걷기가 성립하고, Fighter 의 가드 성립 판정(FacesMe)도 정확해진다.
    /// 상대가 없으면(테스트 씬 등) 예전처럼 이동 방향을 바라본다.
    /// </summary>
    private void FaceOpponent(float moveX)
    {
        bool onRight;

        if (opponent != null)
        {
            onRight = opponent.position.x > transform.position.x;
        }
        else
        {
            if (moveX == 0f) return;          // 상대도 없고 멈춰 있으면 방향 유지
            onRight = moveX > 0f;
        }

        fighter.Facing = onRight ? 1 : -1;
        if (spriterenderer != null) spriterenderer.flipX = !onRight;
    }

    void DoJump()
    {
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
        animator.SetTrigger("Jump");
        animator.SetBool("IsGrounded", false);
        isGrounded = false;
    }

    void DoAttack()
    {
        // 상황에 따라 공격 높이가 달라진다.
        //   공중 = High(오버헤드) / 앉기 = Low / 그 외 = Mid
        AttackHeight height = !isGrounded ? AttackHeight.High
                            : isCrouching ? AttackHeight.Low
                                          : AttackHeight.Mid;

        // 후딜(recovery) 중이거나 경직 중이면 공격이 나가지 않는다.
        // TryAttack 안에서 애니메이터의 AttackHeight(int) 도 함께 세팅된다.
        if (!fighter.TryAttack(height))
            return;

        animator.SetTrigger("Attack");

        if (!isGrounded)      Debug.Log("점프 공격!");
        else if (isCrouching) Debug.Log("앉기 공격!");
        else                  Debug.Log("기본 공격!");
    }

    void OnCollisionEnter2D(Collision2D col)
    {
        if (col.gameObject.CompareTag("Ground"))
        {
            isGrounded = true;
            animator.SetBool("IsGrounded", true);
        }
    }

    void OnCollisionExit2D(Collision2D col)
    {
        if (col.gameObject.CompareTag("Ground"))
        {
            isGrounded = false;
            animator.SetBool("IsGrounded", false);
        }
    }

    // ── 파이썬(웹캠 동작인식) 연동용 ────────────────────────────────
    // UdpActionReceiver 가 인식 결과를 여기로 넘긴다.
    // 애니메이터를 직접 건드리지 않고 이 함수를 거쳐야 데미지 판정까지 함께 들어간다.
    // Shoulder movement is a continuous signed axis, independent of the action confidence.
    public void ExternalMovement(float axis)
    {
        externalControl = true;
        externalMoveX = Mathf.Clamp(axis, -1f, 1f);
    }

    public void ClearExternalInput()
    {
        externalControl = false;
        externalMoveX = 0f;
        isCrouching = false;
        isBlocking = false;
        if (fighter != null)
        {
            fighter.IsCrouching = false;
            fighter.IsBlocking = false;
        }
        if (animator != null)
        {
            animator.SetBool("IsCrouching", false);
            animator.SetBool("IsBlocking", false);
        }
    }

    public void ExternalAction(string action)
    {
        if (health.IsDead || health.IsHitStunned || fighter.InputLocked) return;

        currentAction = action;

        switch (action)
        {
            case "ATTACK":
                isBlocking = false;
                fighter.IsBlocking = false;
                animator.SetBool("IsBlocking", false);
                DoAttack();
                break;

            case "JUMP":
                isBlocking = false;
                fighter.IsBlocking = false;
                animator.SetBool("IsBlocking", false);
                if (isGrounded) DoJump();
                break;

            case "CROUCH":
                isCrouching = true;
                isBlocking = false;
                animator.SetBool("IsCrouching", true);
                animator.SetBool("IsBlocking", false);
                fighter.IsCrouching = true;
                fighter.IsBlocking = false;
                break;

            case "BLOCK":
                isCrouching = false;
                isBlocking = true;
                animator.SetBool("IsCrouching", false);
                animator.SetBool("IsBlocking", true);
                fighter.IsCrouching = false;
                fighter.IsBlocking = true;
                break;

            // 앉아막기. 인식 쪽에서 이 라벨을 보내면 하단 가드가 성립한다.
            case "CROUCH_BLOCK":
                isCrouching = true;
                isBlocking = true;
                animator.SetBool("IsCrouching", true);
                animator.SetBool("IsBlocking", true);
                fighter.IsCrouching = true;
                fighter.IsBlocking = true;
                break;

            case "IDLE":
            case "NONE":
                isCrouching = false;
                isBlocking = false;
                animator.SetBool("IsCrouching", false);
                animator.SetBool("IsBlocking", false);
                fighter.IsCrouching = false;
                fighter.IsBlocking = false;
                break;

            default:
                Debug.LogWarning($"[PlayerController] 알 수 없는 액션: {action}");
                break;
        }
    }
}
