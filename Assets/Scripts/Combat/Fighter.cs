using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>공격 높이. 방어 성립 여부를 결정한다.</summary>
public enum CombatMove { Jab, Straight, HighKick, LowKick }

public enum AttackHeight
{
    High,   // 하이킥: 일반 가드로 방어
    Mid,    // 펀치: 일반 / 로우 가드로 방어
    Low     // 로우킥: 로우 가드로 방어
}

/// <summary>
/// 공격 판정을 담당하는 공통 전투 컴포넌트. 플레이어와 적이 함께 사용한다.
/// Humanoid uses pose-driven swept contacts in CombatContactWorld after animation.
/// The original one-shot box remains only for legacy sprite fighters.
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

    private readonly List<Health> alreadyHit = new List<Health>();
    private Animator animator;
    private Fighter3DView view3D;
    private Coroutine attackRoutine;
    private bool preciseRoutine;
    public CombatBodyPose Contacts { get; private set; }
    public float AttackStarted { get; private set; }
    public int AttackSerial { get; private set; }
    public bool PreciseAttackActive => preciseRoutine && IsAttacking && isActiveAndEnabled && !InputLocked && !Health.IsDead && !Health.IsHitStunned && Contacts.Ready;
    public event Action<Fighter, Vector3, CombatBodyPart, bool> ContactResolved;
    public bool HasContacted(Health target) => alreadyHit.Contains(target);
    public static AttackHeight HeightOf(CombatMove move) => move == CombatMove.HighKick ? AttackHeight.High : move == CombatMove.LowKick ? AttackHeight.Low : AttackHeight.Mid;
    public bool IsGuardedBy(Fighter target)
    {
        if (!target || !target.IsBlocking || !FacesMe(target)) return false;
        var height=HeightOf(CurrentMove);
        return height==AttackHeight.Mid || (height==AttackHeight.Low && target.IsCrouching) || (height==AttackHeight.High && !target.IsCrouching);
    }
    public void FinishPreciseAttack(float now)
    {
        if (preciseRoutine && IsAttacking && (InputLocked || Health.IsDead || Health.IsHitStunned || now-AttackStarted >= Fighter3DView.Duration(CurrentMove)))
            CancelAttack();
    }
    public void ResolveContact(Fighter target, Vector3 point, CombatBodyPart part)
    {
        alreadyHit.Add(target.Health);
        bool guarded=IsGuardedBy(target);
        int damage=guarded ? target.GuardedDamage(attackDamage) : attackDamage;
        Vector2 force=guarded ? Vector2.zero : new Vector2(knockback.x*Facing,knockback.y);
        if (!target.Health.TakeDamage(damage,force,guarded)) return;
        HitVfx.Play(point,guarded);
        CombatContactAudio.Play(CurrentMove,guarded);
        bool finisher=target.Health.IsDead;
        if (useHitImpact) HitImpact.Play(point,guarded,finisher,!finisher);
        ContactResolved?.Invoke(target,point,part,guarded);
    }
    public CombatMove CurrentMove { get; private set; }

    public int AttackDamage => attackDamage;
    public float GuardReduction => 1f-blockDamageMultiplier;
    public void ConfigureGuard(float reduction) { blockDamageMultiplier=1f-Mathf.Clamp(reduction,0,.9f); }
    public int GuardedDamage(int incoming) { return Mathf.Max(1,Mathf.CeilToInt(incoming*blockDamageMultiplier-.00001f)); }
    public void ConfigureDamage(int damage) { attackDamage = Mathf.Max(1, damage); }

    void Awake()
    {
        Health = GetComponent<Health>();
        animator = GetComponent<Animator>();
        view3D = GetComponent<Fighter3DView>();
        Contacts = new CombatBodyPose(this);
    }

    void OnEnable() { if (Health != null) Health.OnDamaged += InterruptAttack; CombatContactWorld.Register(this); }
    void OnDisable() { if (Health != null) Health.OnDamaged -= InterruptAttack; CancelAttack(); CombatContactWorld.Unregister(this); }
    void InterruptAttack(bool guarded) { CancelAttack(); }
    public void CancelAttack()
    {
        if (attackRoutine != null) StopCoroutine(attackRoutine);
        attackRoutine = null; preciseRoutine = false; IsAttacking = false;
    }

    /// <summary>공격 시작. 실제로 시작됐으면 true.</summary>
    public bool TryAttack(AttackHeight height)
    {
        return TryAttack(height == AttackHeight.High ? CombatMove.HighKick : height == AttackHeight.Low ? CombatMove.LowKick : CombatMove.Straight);
    }

    public bool TryAttack(CombatMove move)
    {
        if (!CanAttack || IsBlocking) return false;
        CurrentMove = move;
        alreadyHit.Clear(); AttackSerial++; AttackStarted=Time.time;
        AttackHeight height = move == CombatMove.HighKick ? AttackHeight.High : move == CombatMove.LowKick ? AttackHeight.Low : AttackHeight.Mid;
        if (view3D != null) view3D.BeginAttack(move);
        // 공격 높이를 애니메이터에 정수로 고정 통보한다.
        // 자세 bool 로 분기하면 공격 도중 S 를 떼는 순간 상태가 튀기 때문.
        //   0 = High(점프) / 1 = Mid(서서) / 2 = Low(앉아)
        if (animator != null)
            animator.SetInteger("AttackHeight", (int)height);

        preciseRoutine = view3D != null && Contacts.Ready;
        if (preciseRoutine) IsAttacking=true;
        else attackRoutine = StartCoroutine(AttackRoutine(height));
        return true;
    }

    private IEnumerator AttackRoutine(AttackHeight height)
    {
        IsAttacking = true;

        float startup = view3D != null ? Fighter3DView.Impact(CurrentMove) : windup;
        float ending = view3D != null ? Fighter3DView.Duration(CurrentMove) - startup : recovery;
        yield return new WaitForSeconds(startup);

        if (!Health.IsDead && !InputLocked && !Health.IsHitStunned)
            ResolveHit(height);

        yield return new WaitForSeconds(ending);

        IsAttacking = false;
        attackRoutine = null;
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
                    damage = targetFighter.GuardedDamage(damage); // Defender owns mitigation, not the attacker.
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

                // 타격 이펙트는 카메라 연출과 별개다. useHitImpact 를 꺼도 이펙트는 나온다.
                HitVfx.Play(impactPoint, guarded);

                if (useHitImpact)
                {
                    // 마무리 일격에만 카메라 확대가 붙는다. 기본 타격은 슬로우만.
                    // 마무리 타격의 시간 연출은 BattleSceneBridge 의 K.O. 슬로우모션이 맡는다.
                    // 여기서 또 timeScale 을 건드리면 둘이 서로를 덮어써서 연출이 끊긴다.
                    bool finisher = targetHealth.IsDead;
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
        if (Application.isPlaying && Contacts != null && Contacts.Ready)
        {
            var w=CombatContactWorld.Window(CurrentMove);
            Gizmos.color=PreciseAttackActive && Time.time-AttackStarted>=w.x && Time.time-AttackStarted<=w.y ? Color.red : Color.gray;
            var strike=Contacts.strikes[(int)CurrentMove];
            Gizmos.DrawWireSphere(strike.a,strike.radius);Gizmos.DrawWireSphere(strike.b,strike.radius);Gizmos.DrawLine(strike.a,strike.b);
            Gizmos.color=new Color(0,.8f,1,.55f);
            for(int i=0;i<7;i++){var h=Contacts.hurt[i];Gizmos.DrawWireSphere(h.a,h.radius);Gizmos.DrawWireSphere(h.b,h.radius);Gizmos.DrawLine(h.a,h.b);}
            return;
        }
        Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.5f);
        Gizmos.DrawWireCube(GetHitboxCenter(), hitboxSize);
    }
}
