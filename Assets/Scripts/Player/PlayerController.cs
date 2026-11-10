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
    private bool unifiedBoxing;
    private Battle3DCamera battleCamera;

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
    [Tooltip("누르면 방어 시전. Fighter 의 방어 시간 동안 유지된다. 기본값 U.")]
    public KeyCode blockKey     = KeyCode.U;
    public KeyCode jabKey = KeyCode.J;
    public KeyCode straightKey = KeyCode.K;
    public KeyCode highKickKey = KeyCode.L;
    public KeyCode lowKickKey = KeyCode.H;

    [Header("상대 (격투게임 방향 처리)")]
    [Tooltip("비워두면 씬에서 EnemyAI 를 자동으로 찾는다. 항상 이쪽을 바라본다.")]
    [SerializeField] private Transform opponent;
    [Tooltip("뒤로 걸을 때 속도 배율. 격투게임은 보통 후진이 조금 느리다.")]
    [SerializeField] private float backwardSpeedMultiplier = 0.8f;
    [Tooltip("앉은 채로 이동할 때 속도 배율. 0 으로 두면 앉아서 못 움직인다.")]
    [SerializeField] private float crouchSpeedMultiplier = 0.45f;

    private bool isGrounded = true;
    private bool isCrouching = false;
    public String currentAction = "IDLE";

    // UDP(웹캠 동작인식) 패킷이 한 번이라도 들어오면 true 가 된다.
    // 이때부터 앉기/막기 자세는 키보드가 아니라 인식 결과가 결정한다.
    // (이동/점프/공격 키는 테스트용으로 계속 살아있다)
    public bool externalControl = false;
    private float externalMoveX;

    public void SetOpponent(Transform value) { opponent = value; }

    void Awake()
    {
        animator = GetComponent<Animator>();
        rb = GetComponent<Rigidbody2D>();
        spriterenderer = GetComponent<SpriteRenderer>();
        health = GetComponent<Health>();
        fighter = GetComponent<Fighter>();
        unifiedBoxing = GetComponent<Fighter3DView>() != null;
        if(Camera.main!=null)battleCamera=Camera.main.GetComponent<Battle3DCamera>();

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
            fighter.IsCrouching = false;
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
        // ── 앉기: 누르고 있는 동안 true ──
        // 외부(웹캠) 제어 중에는 ExternalAction() 이 정한 자세를 덮어쓰지 않는다.
        if (useKeyboard && !externalControl)
            isCrouching = Input.GetKey(crouchKey);
        // 3D 복싱에는 앉기가 없다. 하단 방어도 상대 공격 예고에 맞춰 자동으로 정해진다(Fighter.TryGuard).
        if (unifiedBoxing) isCrouching = false;

        // 전투 시스템에 현재 자세를 알려준다. 방어 중에는 Fighter 가 방어를 시전한 자세로 고정하므로
        // 화면은 넣은 값이 아니라 Fighter 값을 따른다.
        fighter.IsCrouching = isCrouching;
        animator.SetBool("IsCrouching", fighter.IsCrouching);
        animator.SetBool("IsBlocking",  fighter.IsBlocking);

        // ── 이동 ──
        // 이동으로 캐릭터를 회전시키지 않는다. 방향은 아래 FaceOpponent() 가 정한다.
        // 공격·방어·방어 실패 딜레이 중에는 못 움직인다. 앉은 채로는 느리게 움직일 수 있다.
        float moveX = 0f;
        if (fighter.CanAct)
        {
            if (externalControl) moveX = externalMoveX;
            // A/D still work for testing when the vision detector reports neutral movement.
            if (useKeyboard && Mathf.Abs(moveX) < 0.01f)
            {
                if (Input.GetKey(moveLeftKey))       moveX = -1f;
                else if (Input.GetKey(moveRightKey)) moveX =  1f;
            }
        }

        // Rotating behind the fighters must not reverse screen-left/right keyboard or vision intent.
        if(battleCamera!=null)moveX=battleCamera.ScreenHorizontalToWorld(moveX);

        // 바라보는 방향을 먼저 확정해야 전진/후진 판정이 맞는다.
        FaceOpponent(moveX);

        // 바라보는 방향과 이동 방향이 반대면 후진.
        bool movingBack = Mathf.Abs(moveX) > 0.01f && moveX * fighter.Facing < 0f;

        float speed = moveSpeed * (unifiedBoxing ? Fighter3DView.BoxingTravelScale : 1f);
        if (movingBack)  speed *= backwardSpeedMultiplier;
        if (isCrouching) speed *= crouchSpeedMultiplier;

        rb.linearVelocity = new Vector2(moveX * speed, rb.linearVelocity.y);
        animator.SetBool("IsMoving", Mathf.Abs(moveX) > 0.01f);
        animator.SetBool("IsMovingBack", movingBack);

        if (!useKeyboard) return;

        // ── 동작 입력 ──
        // 방어 중에 누른 동작은 바로 나가지 않고 기억된다. 방어에 성공하면 그 즉시 나간다. (Act 참고)
        if (Input.GetKeyDown(blockKey))    Act(Guard);
        // 3D 복싱에는 점프가 없다(애니메이터에 JUMP 상태가 없다).
        if (!unifiedBoxing && Input.GetKeyDown(jumpKey)) Act(TryJump);
        if (Input.GetKeyDown(jabKey))      Act(() => AttackMove(CombatMove.Jab));
        if (Input.GetKeyDown(straightKey)) Act(() => AttackMove(CombatMove.Straight));
        if (Input.GetKeyDown(highKickKey)) Act(() => AttackMove(CombatMove.HighKick));
        if (Input.GetKeyDown(lowKickKey))  Act(() => AttackMove(CombatMove.LowKick));
        if (Input.GetKeyDown(attackKey))   Act(DoAttack);
    }

    /// <summary>
    /// 동작 입력 하나를 처리한다. 방어 중이면 실행하지 않고 Fighter 에 맡겨 둔다(마지막 하나만).
    /// 맡긴 동작은 방어에 성공하면 그 순간 나가고, 방어가 실패하거나 뚫리면 버려진다.
    /// </summary>
    private void Act(Action action)
    {
        if (fighter.IsGuarding) fighter.BufferAction(action);
        else action();
    }

    /// <summary>방어 시전. 지금 자세(서서/앉아)로 방어가 끝날 때까지 고정된다.</summary>
    private void Guard()
    {
        fighter.TryGuard(isCrouching);
    }

    private void TryJump()
    {
        if (isGrounded && fighter.CanAct) DoJump();
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

    public void AttackMove(CombatMove move)
    {
        if (fighter.TryAttack(move)) animator.SetTrigger("Attack");
    }

    void DoAttack()
    {
        if (unifiedBoxing) { AttackMove(CombatMove.Straight); return; }
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
        if (fighter != null) fighter.IsCrouching = false;
        if (animator != null) animator.SetBool("IsCrouching", false);
    }

    public void ExternalAction(string action)
    {
        if (health.IsDead || health.IsHitStunned || fighter.InputLocked) return;

        // Removed 3D actions cannot leave the fighter airborne or in an unmapped pose.
        if (unifiedBoxing && (action == "JUMP" || action == "CROUCH")) return;
        if (fighter.IsAttacking && (action == "BLOCK" || action == "CROUCH_BLOCK"
            || action == "LOW_BLOCK" || action == "GUARD_LOWKICK")) return;
        currentAction = action;

        // 자세(앉기·방어)는 여기서 정하고, 화면과 Fighter 반영은 매 프레임 HandleKeyInput 이 한다.
        // 공격·점프는 방어 중이면 기억해 뒀다가 방어에 성공하면 나간다. (Act 참고)
        switch (action)
        {
            case "JAB":
            case "STRAIGHT":
            case "CROSS":
            case "HIGH_KICK":
            case "HIGHKICK":
            case "LOW_KICK":
            case "LOWKICK":
            {
                CombatMove move = action == "JAB" ? CombatMove.Jab
                                : action.StartsWith("HIGH") ? CombatMove.HighKick
                                : action.StartsWith("LOW") ? CombatMove.LowKick
                                : CombatMove.Straight;
                Act(() => AttackMove(move));
                break;
            }

            case "ATTACK":
                Act(DoAttack);
                break;

            case "JUMP":
                Act(TryJump);
                break;

            case "CROUCH":
                isCrouching = true;
                break;

            // 인식은 방어 자세를 패킷마다 보낸다. 그래서 방어 중에 들어온 것은 기억하지 않는다.
            // 기억해 두면 미리 입력한 공격을 덮어쓴다. 방어가 끝난 뒤 다음 패킷이 오면 그때 다시 시전된다.
            case "BLOCK":
                isCrouching = false;
                if (!fighter.IsGuarding) Guard();
                break;

            // 앉아막기. 스프라이트 전투에서는 하단 가드가 된다.
            // 3D 복싱에서는 BLOCK 과 같다 — 방어 높이는 상대 공격 예고에 맞춰 자동으로 정해진다.
            case "LOW_BLOCK":
            case "GUARD_LOWKICK":
            case "CROUCH_BLOCK":
                isCrouching = true;
                if (!fighter.IsGuarding) Guard();
                break;

            // 파운장. 인식은 기 모으는 자세를 패킷마다 SPECIAL_CHARGE 로, 양손 밀기를 SPECIAL_RELEASE 로 보낸다.
            // SPECIAL 은 한 번짜리 입력(최소 시간만 모으고 바로 발사).
            case "SPECIAL_CHARGE":
            {
                var special = GetComponent<SpecialMove>();
                if (special != null && !special.IsPlaying)
                {
                    if (fighter.IsGuarding) fighter.BufferAction(() => special.BeginCharge());
                    else special.BeginCharge();
                }
                break;
            }
            case "SPECIAL_RELEASE":
                GetComponent<SpecialMove>()?.Release();
                break;
            case "SPECIAL":
                GetComponent<SpecialMove>()?.Activate();
                break;

            case "IDLE":
            case "NONE":
                isCrouching = false;
                break;

            default:
                Debug.LogWarning($"[PlayerController] 알 수 없는 액션: {action}");
                break;
        }
    }
}
