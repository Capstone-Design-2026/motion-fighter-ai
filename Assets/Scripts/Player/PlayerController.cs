using System;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    private Animator animator;
    private Rigidbody2D rb;
    private SpriteRenderer spriterenderer;

    public float moveSpeed = 5f;
    public float jumpForce = 12f;
    private bool isGrounded = true;
    private bool isCrouching = false;
    public String cuurentAction = "IDLE";
    
    void Start()
    {
        animator = GetComponent<Animator>();
        rb = GetComponent<Rigidbody2D>();
        spriterenderer = GetComponent<SpriteRenderer>();
        
        // 파이썬 연동시 주석 해제
        // GameManager.OnActionReceived += HandleAction;    
    }

    void Update()
    {
         HandleKeyInput();
    }

    void HandleKeyInput()
    {
        // 이동(A/D)
        float moveX = 0f;
        if(Input.GetKey(KeyCode.A))
        {
            moveX = -1f;
            spriterenderer.flipX = true;
        }
        else if (Input.GetKey(KeyCode.D))
        {
            moveX = 1f;
            spriterenderer.flipX = false;
        }

        rb.linearVelocity = new Vector2(moveX * moveSpeed, rb.linearVelocity.y);
        // 속도 기준으로 판단 (키 입력 기준 말고)
        bool isMoving = Mathf.Abs(rb.linearVelocity.x) > 0.1f;
        animator.SetBool("IsMoving", moveX != 0f);

        // 점프 W
        if(Input.GetKeyDown(KeyCode.W) && isGrounded)
        {
            DoJump();
        }

        // 앉기 S
        if (Input.GetKeyDown(KeyCode.S))
        {
            isCrouching = true;
            animator.SetBool("IsCrouching", true);
            animator.SetTrigger("Crouch");
            animator.SetTrigger("Crouch");
        }
        if (Input.GetKeyUp(KeyCode.S))
        {
            isCrouching = false;
            animator.SetBool("IsCrouching", false);
            // ← Idle로 자동 복귀 (Animator Transition이 처리)
        }

        // 공격 P
        if (Input.GetKeyDown(KeyCode.P))
        {
            DoAttack();
        }
        }

    void DoJump()
    {
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
        animator.SetTrigger("Jump");
        animator.SetBool("IsGrounded", false);
        isGrounded = false;
    }

        void OnCollisionEnter2D(Collision2D col)
    {
        if (col.gameObject.CompareTag("Ground"))
        {
            isGrounded = true;
            animator.SetBool("IsGrounded", true);
        }
    }

    void DoAttack()
    {
                // 점프 중 공격
        if (!isGrounded)
        {
            animator.SetTrigger("Attack");
            Debug.Log("점프 공격!");
            return;
        }

        // 앉은 상태 공격
        if (isCrouching)
        {
            animator.SetTrigger("Attack");
            Debug.Log("앉기 공격!");
            return;
        }

        // 기본 공격
        animator.SetTrigger("Attack");
        Debug.Log("기본 공격!");
    }
    void OnCollisionExit2D(Collision2D col)
    {
        if (col.gameObject.CompareTag("Ground"))
        {
            isGrounded = false;
            animator.SetBool("IsGrounded", false);
        }
    }

      // ── 나중에 Python 연동할 때 주석 해제 ──────────────
    // void HandleAction(string action)
    // {
    //     currentAction = action;
    //     switch (action)
    //     {
    //         case "ATTACK":
    //             animator.SetTrigger("Attack");
    //             break;
    //         case "JUMP":
    //             if (isGrounded) DoJump();
    //             break;
    //         case "CROUCH":
    //             animator.SetTrigger("Crouch");
    //             break;
    //         case "IDLE":
    //             break;
    //     }
    // }

    // void OnDestroy()
    // {
    //     GameManager.OnActionReceived -= HandleAction;
    // }
}
