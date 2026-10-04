using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 체력 컴포넌트. 플레이어와 적이 공통으로 사용한다.
/// 데미지 적용 / 무적시간 / 히트스턴 / 피격 애니메이션 / HP 변화 이벤트를 담당한다.
/// </summary>
[DisallowMultipleComponent]
public class Health : MonoBehaviour
{
    [Header("HP")]
    [SerializeField] private string displayName = "FIGHTER";
    [SerializeField] private int maxHealth = 100;

    [Header("피격 경직 (초)")]
    [Tooltip("맞았을 때 조작이 잠기는 시간. 이 시간이 있어야 Hurt 애니메이션이 보인다.")]
    [SerializeField] private float hitStunTime = 0.30f;
    [Tooltip("가드 성공 시 경직. 히트스턴보다 짧아야 가드 후 반격이 가능하다.")]
    [SerializeField] private float guardStunTime = 0.15f;

    [Header("피격 연출")]
    [SerializeField] private float invulnerableTime = 0.25f;   // 연속 피격 방지
    [SerializeField] private float flashTime = 0.12f;
    [SerializeField] private Color flashColor = new Color(1f, 0.35f, 0.35f, 1f);
    [SerializeField] private Color guardFlashColor = new Color(0.6f, 0.8f, 1f, 1f);

    public string DisplayName => displayName;
    public int MaxHealth => maxHealth;
    public int CurrentHealth { get; private set; }
    public bool DamageLocked { get; set; }
    public bool IsDead => CurrentHealth <= 0;
    public bool IsInvulnerable => Time.time < invulnerableUntil;

    /// <summary>피격 경직 중. true 인 동안 PlayerController / EnemyAI 는 조작을 멈춘다.</summary>
    public bool IsHitStunned => Time.time < hitStunUntil;

    /// <summary>(현재 HP, 최대 HP)</summary>
    public event Action<int, int> OnHealthChanged;

    /// <summary>실제로 대미지를 입은 순간. 인자는 가드로 막혔는지 여부. (필살기 게이지 등)</summary>
    public event Action<bool> OnDamaged;
    public event Action<Health> OnDeath;

    private float invulnerableUntil;
    private float hitStunUntil;
    private SpriteRenderer spriteRenderer;
    private Rigidbody2D rb;
    private Animator animator;
    private Color originalColor = Color.white;
    private Coroutine flashRoutine;

    void Awake()
    {
        CurrentHealth = maxHealth;
        spriteRenderer = GetComponent<SpriteRenderer>();
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        if (spriteRenderer != null)
            originalColor = spriteRenderer.color;
    }

    void OnEnable()
    {
        // HUD 가 늦게 구독해도 현재 값을 받도록 한 프레임 뒤 한번 더 알린다.
        OnHealthChanged?.Invoke(CurrentHealth, maxHealth);
    }

    void Start()
    {
        OnHealthChanged?.Invoke(CurrentHealth, maxHealth);
    }

    /// <summary>데미지 적용. 실제로 들어갔으면 true.</summary>
    /// <param name="guarded">가드에 막혔으면 true. 애니메이션과 경직시간이 달라진다.</param>
    public bool TakeDamage(int amount, Vector2 knockback, bool guarded = false)
    {
        if (DamageLocked || IsDead || IsInvulnerable || amount <= 0)
            return false;

        CurrentHealth = Mathf.Max(0, CurrentHealth - amount);
        invulnerableUntil = Time.time + invulnerableTime;
        hitStunUntil = Time.time + (guarded ? guardStunTime : hitStunTime);

        OnHealthChanged?.Invoke(CurrentHealth, maxHealth);
        OnDamaged?.Invoke(guarded);

        if (rb != null && knockback != Vector2.zero)
            rb.linearVelocity = new Vector2(knockback.x, Mathf.Max(rb.linearVelocity.y, knockback.y));

        if (spriteRenderer != null)
        {
            if (flashRoutine != null) StopCoroutine(flashRoutine);
            flashRoutine = StartCoroutine(FlashRoutine(guarded ? guardFlashColor : flashColor));
        }

        // ── 애니메이터 통보 ──
        // 가드 성공 → GuardHit / 가드 실패(직격) → Hurt
        if (animator != null && !IsDead)
            animator.SetTrigger(guarded ? "GuardHit" : "Hurt");

        Debug.Log($"[Health] {displayName} -{amount} → {CurrentHealth}/{maxHealth}{(guarded ? " (가드)" : "")}");

        if (IsDead)
        {
            Debug.Log($"[Health] {displayName} K.O.");

            hitStunUntil = 0f;   // 사망 상태가 경직보다 우선한다

            if (animator != null)
            {
                animator.ResetTrigger("Hurt");
                animator.ResetTrigger("GuardHit");
                animator.SetBool("IsDead", true);
            }

            if (spriteRenderer != null)
                spriteRenderer.color = new Color(0.45f, 0.45f, 0.45f, 1f);

            OnDeath?.Invoke(this);
        }

        return true;
    }

    public void SetAppearance(Color tint)
    {
        originalColor = tint;
        var renderer = GetComponent<SpriteRenderer>();
        if (renderer != null) renderer.color = tint;
    }

    public void Heal(int amount)
    {
        if (IsDead || amount <= 0) return;
        CurrentHealth = Mathf.Min(maxHealth, CurrentHealth + amount);
        OnHealthChanged?.Invoke(CurrentHealth, maxHealth);
    }

    /// <summary>
    /// 스테이지 데이터로 이름과 최대 체력을 덮어쓴다.
    /// 전투가 시작되기 전(BattleSceneBridge.Awake)에만 호출할 것.
    /// </summary>
    public void Configure(string newDisplayName, int newMaxHealth)
    {
        if (!string.IsNullOrEmpty(newDisplayName)) displayName = newDisplayName;
        if (newMaxHealth > 0) maxHealth = newMaxHealth;

        CurrentHealth = maxHealth;
        OnHealthChanged?.Invoke(CurrentHealth, maxHealth);
    }

    /// <summary>이전 전투에서 이어받은 체력을 그대로 반영한다. 지도 진행용.</summary>
    public void SetCurrentHealth(int value)
    {
        CurrentHealth = Mathf.Clamp(value, 0, maxHealth);
        OnHealthChanged?.Invoke(CurrentHealth, maxHealth);
    }

    /// <summary>라운드 리셋용. HP 를 가득 채우고 색과 애니메이터 상태를 되돌린다.</summary>
    public void ResetHealth()
    {
        if (flashRoutine != null) { StopCoroutine(flashRoutine); flashRoutine = null; }

        CurrentHealth = maxHealth;
        invulnerableUntil = 0f;
        hitStunUntil = 0f;

        if (spriteRenderer != null) spriteRenderer.color = originalColor;

        if (animator != null)
        {
            animator.SetBool("IsDead", false);
            animator.ResetTrigger("Hurt");
            animator.ResetTrigger("GuardHit");
        }

        OnHealthChanged?.Invoke(CurrentHealth, maxHealth);
    }

    private IEnumerator FlashRoutine(Color color)
    {
        spriteRenderer.color = color;
        yield return new WaitForSeconds(flashTime);
        if (!IsDead)
            spriteRenderer.color = originalColor;
        flashRoutine = null;
    }
}
