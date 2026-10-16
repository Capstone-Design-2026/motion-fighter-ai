using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>공격 높이. 방어 성립 여부를 결정한다.</summary>
public enum CombatMove { Jab, Straight, HighKick, LowKick }

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

    [Header("방어 규칙 (방어하는 쪽 기준)")]
    [Tooltip("방어를 시전하면 이 시간(초) 동안 그대로 유지된다. 그동안 다른 동작은 나가지 않고 입력만 기억해 둔다.")]
    [Min(0.05f)]
    [SerializeField] private float guardDuration = 1.5f;
    [Tooltip("방어 실패 — 방어하는 동안 상대 공격을 하나도 막지 못하고 시간이 끝남 — 뒤에 아무 동작도 못 하는 시간(초).")]
    [Min(0f)]
    [SerializeField] private float guardFailDelay = 0.5f;
    [Tooltip("공격을 막아냈을 때 공격한 쪽의 후딜에 더해지는 시간(초). "
           + "후딜이 끝날 때까지 공격한 쪽은 움직이지도, 다시 공격하지도, 방어하지도 못한다.")]
    [Min(0f)]
    [SerializeField] private float blockPunish = 0.4f;

    [Header("타격감")]
    [Tooltip("대미지가 실제로 들어간 순간 카메라 확대 + 슬로우 연출을 넣는다. 수치는 HitImpact 컴포넌트에서 조정.")]
    [SerializeField] private bool useHitImpact = true;

    [Header("디버그")]
    [SerializeField] private bool drawHitboxGizmo = true;

    /// <summary>+1 = 오른쪽, -1 = 왼쪽</summary>
    public int Facing { get; set; } = 1;

    /// <summary>
    /// 방어 중인지. 방어는 TryGuard 로 시전하는 동작이다. 시전하면 guardDuration 동안 그대로 유지되고,
    /// 그동안에는 이동·점프·공격·필살기가 나가지 않는다(입력은 BufferAction 으로 기억해 둔다). 끝나는 경우는 셋이다.
    ///   성공: 상대 공격을 막아냄 → 남은 시간과 상관없이 즉시 풀리고, 기억해 둔 동작이 있으면 바로 나간다.
    ///   실패: 아무것도 막지 못하고 시간이 다 됨 → guardFailDelay 동안 아무 동작도 못 한다.
    ///   뚫림: 높이가 안 맞거나 등 뒤에서 맞음 → 일반 피격 경직. 기억해 둔 입력은 버려진다.
    /// setter 는 예전 코드(FighterAI) 호환용이다. true 는 TryGuard 와 같고 false 는 무시된다 — 방어는 중간에 내릴 수 없다.
    /// </summary>
    public bool IsBlocking
    {
        get => guarding;
        set { if (value) TryGuard(IsCrouching); }
    }
    public bool IsGuarding => guarding;
    public float GuardDuration => guardDuration;

    /// <summary>방어 실패 딜레이 중인지.</summary>
    public bool IsGuardRecovering => Time.time < guardRecoverUntil;

    /// <summary>앉아 있는지. 방어 중에는 방어를 시전한 순간의 자세로 고정된다(가드 높이).</summary>
    public bool IsCrouching
    {
        get => guarding ? guardCrouching : crouching;
        set => crouching = value;
    }
    public bool IsAttacking { get; private set; }

    /// <summary>
    /// K.O. 연출 중에는 true. 진 쪽뿐 아니라 이긴 쪽 조작까지 전부 막는다.
    /// 이게 없으면 쓰러진 상대를 계속 때릴 수 있다. BattleSceneBridge 가 켜고 끈다.
    /// </summary>
    public bool InputLocked { get; set; }

    public Health Health { get; private set; }
    /// <summary>새 동작(이동·점프·공격·방어)을 시작할 수 있는지.</summary>
    public bool CanAct => !Health.IsDead && !InputLocked && !Health.IsHitStunned && !IsAttacking
                          && !guarding && !IsGuardRecovering;
    public bool CanAttack => CanAct;

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

    private bool crouching;
    private bool guarding;
    private bool guardCrouching;      // 방어를 시전한 순간의 자세
    private float guardStartedAt;
    private float guardRecoverUntil;  // 방어 실패 딜레이가 끝나는 시각
    private Action bufferedAction;    // 방어 중에 들어온 입력. 성공하면 실행, 아니면 버린다.
    private Fighter3DView view3D;
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
    }

    void Update()
    {
        if (!guarding) return;

        // K.O. 연출·사망·피격(뚫림)으로 끝난 경우. 실패 딜레이는 주지 않는다.
        if (Health.IsDead || InputLocked || Health.IsHitStunned)
        {
            EndGuard();
            return;
        }

        // 실패: 방어하는 동안 상대 공격을 하나도 막지 못했다.
        if (Time.time - guardStartedAt >= guardDuration)
        {
            EndGuard();
            guardRecoverUntil = Time.time + guardFailDelay;
            Debug.Log($"[Fighter] {name} 방어 실패 (막은 공격 없음) → {guardFailDelay:0.##}초 딜레이");
        }
    }

    /// <summary>방어 시전. 시작됐으면 true. low 면 앉아막기(하단 가드)로 끝날 때까지 고정된다.</summary>
    public bool TryGuard(bool low)
    {
        if (!CanAct) return false;

        guarding = true;
        guardCrouching = low;
        guardStartedAt = Time.time;
        bufferedAction = null;
        return true;
    }

    /// <summary>
    /// 방어 중에 들어온 동작 입력을 기억해 둔다. 마지막 하나만 남는다.
    /// 방어에 성공하면 그 순간 실행되고, 실패하거나 뚫리면 버려진다. 방어 중이 아니면 아무 일도 하지 않는다.
    /// </summary>
    public void BufferAction(Action action)
    {
        if (guarding) bufferedAction = action;
    }

    /// <summary>방어 성공. 남은 시간과 상관없이 즉시 풀고, 기억해 둔 동작이 있으면 바로 시전한다.</summary>
    private void GuardSucceeded()
    {
        Action next = bufferedAction;
        EndGuard();
        Debug.Log($"[Fighter] {name} 방어 성공 → 즉시 해제{(next != null ? " + 미리 입력한 동작 시전" : "")}");
        next?.Invoke();
    }

    private void EndGuard()
    {
        guarding = false;
        bufferedAction = null;
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
        AttackHeight height = move == CombatMove.HighKick ? AttackHeight.High : move == CombatMove.LowKick ? AttackHeight.Low : AttackHeight.Mid;

        var attack = new FighterAttack
        {
            Id = ++nextAttackId,
            Attacker = this,
            Height = height,
            Reach = Reach
        };
        AttackDeclared?.Invoke(attack);

        if (view3D != null) view3D.BeginAttack(move);
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

        float startup = view3D != null ? Fighter3DView.Impact(CurrentMove) : windup;
        float ending = view3D != null ? Fighter3DView.Duration(CurrentMove) - startup : recovery;
        yield return new WaitForSeconds(startup);

        float punish = 0f;
        if (!Health.IsDead && !InputLocked && !Health.IsHitStunned)
        {
            AttackHitFrame?.Invoke(attack);
            punish = ResolveHit(attack.Height);
        }

        // 상대가 막아냈으면 후딜이 길어진다. 그동안은 공격 중(IsAttacking)이라 이동·공격·방어 모두 못 한다.
        yield return new WaitForSeconds(ending + punish);

        IsAttacking = false;
    }

    /// <summary>판정. 상대가 막아냈으면 그 상대가 정한 후딜 페널티(초)를, 아니면 0 을 돌려준다.</summary>
    private float ResolveHit(AttackHeight height)
    {
        float punish = 0f;
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
            bool targetGuarding = targetFighter != null && targetFighter.IsBlocking;
            if (targetGuarding && FacesMe(targetFighter))
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

            if (guarded) targetHealth.GetComponent<Fighter3DView>()?.GuardAgainst(CurrentMove);
            bool damaged = targetHealth.TakeDamage(damage, kb, guarded);

            // 방어 규칙. 값은 방어하는 쪽(targetFighter) 설정을 따른다.
            if (damaged && targetFighter != null)
            {
                if (guarded)
                {
                    // 성공: 막은 쪽은 경직 없이 즉시 풀리고(기억해 둔 동작이 있으면 바로 나간다), 공격한 쪽은 후딜이 길어진다.
                    targetHealth.ClearStun();
                    targetFighter.GuardSucceeded();
                    punish = Mathf.Max(punish, targetFighter.blockPunish);
                }
                else if (targetGuarding)
                {
                    // 뚫림: 방어는 끝나고 일반 피격 경직만 받는다.
                    targetFighter.EndGuard();
                }
            }

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
                    // 마무리 일격에만 카메라 확대가 붙는다. 기본 타격의 슬로우는 HitImpact 에서 꺼 둔다(slowOnRegularHits).
                    // 마무리 타격의 시간 연출은 BattleSceneBridge 의 K.O. 슬로우모션이 맡는다.
                    // 여기서 또 timeScale 을 건드리면 둘이 서로를 덮어써서 연출이 끊긴다.
                    HitImpact.Play(impactPoint, guarded, finisher, !finisher);
                }
            }
        }

        return punish;
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
