using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>공격 높이. 방어 성립 여부를 결정한다.</summary>
public enum AttackHeight
{
    High,   // 점프 공격 (오버헤드) - 앉아막기로는 못 막음
    Mid,    // 기본 공격          - 서서/앉아 둘 다 막힘
    Low     // 앉아 공격          - 서서막기로는 못 막음
}

/// <summary>공격 한 번의 정보. 연타도 공격마다 id 가 다르다.</summary>
public class FighterAttack
{
    public int Id;
    public Fighter Attacker;
    public AttackHeight Height;

    /// <summary>공격자 중심에서 판정 박스 앞끝까지의 거리.</summary>
    public float Reach;
}

/// <summary>
/// 공격 판정을 담당하는 공통 전투 컴포넌트. 플레이어와 적이 함께 사용한다.
/// 애니메이션 이벤트에 의존하지 않고 코드 타이밍(선딜 → 판정 → 후딜)으로 처리하므로
/// .anim 파일을 건드리지 않아도 동작한다.
/// </summary>
[RequireComponent(typeof(Health))]
[DisallowMultipleComponent]
public class Fighter : MonoBehaviour
{
    [Header("공격 수치")]
    [SerializeField] private int attackDamage = 10;
    [SerializeField] private Vector2 knockback = new Vector2(3.5f, 2f);

    [Header("판정 박스 (캐릭터 정면 기준)")]
    [SerializeField] private float hitboxDistance = 0.85f;   // 앞으로 얼마나
    [SerializeField] private float hitboxHeight = 0f;        // 위아래 오프셋
    [SerializeField] private Vector2 hitboxSize = new Vector2(1.2f, 1.1f);

    [Header("타이밍 (초)")]
    [SerializeField] private float windup = 0.12f;    // 선딜: 애니메이션 나가고 판정 뜨기까지
    [SerializeField] private float recovery = 0.35f;  // 후딜: 다음 공격까지

    [Header("방어")]
    [Range(0f, 1f)]
    [SerializeField] private float blockDamageMultiplier = 0.2f;

    [Header("타격감")]
    [Tooltip("대미지가 실제로 들어간 순간 카메라 확대 + 슬로우 연출을 넣는다. 수치는 HitImpact 컴포넌트에서 조정.")]
    [SerializeField] private bool useHitImpact = true;

    [Header("디버그")]
    [SerializeField] private bool drawHitboxGizmo = true;

    /// <summary>+1 = 오른쪽, -1 = 왼쪽</summary>
    public int Facing { get; set; } = 1;
    public bool IsBlocking { get; set; }
    public bool IsCrouching { get; set; }
    public bool IsAttacking { get; private set; }

    /// <summary>
    /// K.O. 연출 중에는 true. 진 쪽뿐 아니라 이긴 쪽 조작까지 전부 막는다.
    /// 이게 없으면 쓰러진 상대를 계속 때릴 수 있다. BattleSceneBridge 가 켜고 끈다.
    /// </summary>
    public bool InputLocked { get; set; }

    public Health Health { get; private set; }
    public bool CanAttack => !Health.IsDead && !InputLocked && !IsAttacking && !Health.IsHitStunned;

    public float Windup => windup;
    public float Recovery => recovery;
    public float Reach => hitboxDistance + hitboxSize.x * 0.5f;

    /// <summary>
    /// 공격이 확정된 직후, 공격 모션(애니메이터)이 나가기 전에 호출된다.
    /// 상대 AI 는 여기서 방어 여부를 한 번만 정한다. 웹캠 인식과 키 입력이 모두 이 경로를 탄다.
    /// </summary>
    public event Action<FighterAttack> AttackDeclared;

    /// <summary>판정 프레임. ResolveHit 직전에 호출되므로 상대는 여기서 가드 자세를 확정할 수 있다.</summary>
    public event Action<FighterAttack> AttackHitFrame;

    private readonly List<Health> alreadyHit = new List<Health>();
    private Animator animator;
    private int nextAttackId;

    void Awake()
    {
        Health = GetComponent<Health>();
        animator = GetComponent<Animator>();
    }

    /// <summary>공격 시작. 실제로 시작됐으면 true.</summary>
    public bool TryAttack(AttackHeight height)
    {
        if (!CanAttack) return false;

        var attack = new FighterAttack
        {
            Id = ++nextAttackId,
            Attacker = this,
            Height = height,
            Reach = Reach
        };
        AttackDeclared?.Invoke(attack);

        // 공격 높이를 애니메이터에 정수로 고정 통보한다.
        // 자세 bool 로 분기하면 공격 도중 S 를 떼는 순간 상태가 튀기 때문.
        //   0 = High(점프) / 1 = Mid(서서) / 2 = Low(앉아)
        if (animator != null)
            animator.SetInteger("AttackHeight", (int)height);

        CombatSfx.PlayAttack();

        StartCoroutine(AttackRoutine(attack));
        return true;
    }

    private IEnumerator AttackRoutine(FighterAttack attack)
    {
        IsAttacking = true;

        yield return new WaitForSeconds(windup);

        if (!Health.IsDead)
        {
            AttackHitFrame?.Invoke(attack);
            ResolveHit(attack.Height);
        }

        yield return new WaitForSeconds(recovery);

        IsAttacking = false;
    }

    private void ResolveHit(AttackHeight height)
    {
        alreadyHit.Clear();

        Vector2 center = GetHitboxCenter();
        Collider2D[] hits = Physics2D.OverlapBoxAll(center, hitboxSize, 0f);

        for (int i = 0; i < hits.Length; i++)
        {
            var col = hits[i];
            if (col == null) continue;

            var targetHealth = col.GetComponentInParent<Health>();
            if (targetHealth == null) continue;
            if (targetHealth == Health) continue;          // 자기 자신
            if (targetHealth.IsDead) continue;
            if (alreadyHit.Contains(targetHealth)) continue;
            alreadyHit.Add(targetHealth);

            int damage = attackDamage;
            bool guarded = false;
            Vector2 kb = new Vector2(knockback.x * Facing, knockback.y);

            var targetFighter = col.GetComponentInParent<Fighter>();
            if (targetFighter != null && targetFighter.IsBlocking && FacesMe(targetFighter))
            {
                guarded =
                    height == AttackHeight.Mid ||
                    (height == AttackHeight.Low && targetFighter.IsCrouching) ||
                    (height == AttackHeight.High && !targetFighter.IsCrouching);

                if (guarded)
                {
                    damage = Mathf.Max(1, Mathf.CeilToInt(damage * blockDamageMultiplier));
                    kb = Vector2.zero;
                    Debug.Log($"[Fighter] {name} 의 {height} 공격을 {targetFighter.name} 가 방어 (데미지 {damage})");
                }
                else
                {
                    Debug.Log($"[Fighter] {name} 의 {height} 공격이 {targetFighter.name} 의 가드를 뚫음");
                }
            }

            bool damaged = targetHealth.TakeDamage(damage, kb, guarded);

            // 헛방(무적시간에 씹힘)에는 반응하지 않는다. 실제로 대미지가 들어갔을 때만 연출.
            if (damaged)
            {
                // 판정 박스 중심과 맞은 쪽 몸통 사이. 두 캐릭터 사이 지점이라 카메라가 한쪽으로 쏠리지 않는다.
                Vector2 impactPoint = Vector2.Lerp(center, col.bounds.center, 0.5f);

                // 이 타격으로 쓰러졌으면 마무리 일격이다.
                bool finisher = targetHealth.IsDead;

                // 타격 이펙트는 카메라 연출과 별개다. useHitImpact 를 꺼도 이펙트는 나온다.
                HitVfx.Play(impactPoint, guarded);
                CombatSfx.PlayHit(guarded, finisher);

                if (useHitImpact)
                {
                    // 마무리 일격에만 카메라 확대가 붙는다. 기본 타격은 슬로우만.
                    // 마무리 타격의 시간 연출은 BattleSceneBridge 의 K.O. 슬로우모션이 맡는다.
                    // 여기서 또 timeScale 을 건드리면 둘이 서로를 덮어써서 연출이 끊긴다.
                    HitImpact.Play(impactPoint, guarded, finisher, !finisher);
                }
            }
        }
    }

    /// <summary>상대가 나를 바라보고 있는지 (등 뒤 공격은 가드 불가)</summary>
    private bool FacesMe(Fighter target)
    {
        float dx = transform.position.x - target.transform.position.x;
        return dx * target.Facing > 0f;
    }

    private Vector2 GetHitboxCenter()
    {
        return (Vector2)transform.position
             + new Vector2(hitboxDistance * Facing, hitboxHeight);
    }

    void OnDrawGizmosSelected()
    {
        if (!drawHitboxGizmo) return;
        Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.5f);
        Gizmos.DrawWireCube(GetHitboxCenter(), hitboxSize);
    }
}
