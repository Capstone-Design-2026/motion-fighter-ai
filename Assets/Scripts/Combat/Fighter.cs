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
    [Tooltip("지금은 쓰지 않는다 — 방어에 성공하면 피해가 0 이다. ConfigureGuard / GuardedDamage 호환용으로 남겨 둔다.")]
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

    [Header("공격 규칙 (공격하는 쪽 기준)")]
    [Tooltip("공격이 직격했을 때 맞은 쪽의 피격 경직에 더해지는 시간(초). "
           + "너무 길면 같은 공격을 연달아 확정으로 맞히게 된다 — 잽 연타 기준 0.2초 미만으로 둘 것.")]
    [Min(0f)]
    [SerializeField] private float hitStunBonus = 0.15f;
    [Tooltip("공격이 아무에게도 맞지 않았을 때(헛침) 공격한 쪽의 후딜에 더해지는 시간(초). "
           + "막힌 경우는 방어 규칙의 blockPunish(방어한 쪽 값)를 따른다.")]
    [Min(0f)]
    [SerializeField] private float whiffPunish = 0.3f;

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

    /// <summary>
    /// 상대가 하기로 정한 공격. 없으면 null.
    /// 방어 자세(상단/하단)와 방어 모션이 이걸 따른다 — 상단 공격이면 서서 상단 방어, 하단 공격이면 앉아서 하단 방어.
    /// 상대가 DeclareIntent 로 알리고, 공격 판정이 끝나거나 취소되면 ClearIntent 로 거둔다.
    /// </summary>
    public CombatMove? IncomingMove => incomingFrom != null ? incomingMove : (CombatMove?)null;

    /// <summary>무방비가 된 이유. OpeningIndicator 가 화면에 적는다.</summary>
    public enum OpeningCause { None, Blocked, Whiffed, GuardFailed }

    /// <summary>
    /// 딜레이가 걸려 아무것도 못 하는 무방비 상태인지. 상대에게는 공격 기회다.
    ///   Blocked    : 공격이 막혀 후딜이 길어진 동안 (판정 직후 ~ 후딜 끝)
    ///   Whiffed    : 공격이 빗나가 후딜이 길어진 동안 (판정 직후 ~ 후딜 끝)
    ///   GuardFailed: 아무것도 막지 못하고 방어가 끝나 딜레이가 걸린 동안
    /// 그 틈에 공격이 들어가면(막히지 않은 타격) 바로 끝난다.
    /// </summary>
    public bool IsOpen => Time.time < openUntil && !Health.IsDead && !InputLocked;
    public float OpenRemaining => Mathf.Max(0f, openUntil - Time.time);
    public float OpenDuration { get; private set; }
    public OpeningCause OpenCause { get; private set; }

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
                          && !guarding && !IsGuardRecovering && !IsSpecialActive;

    /// <summary>필살기(SpecialMove)가 진행 중인지. 그동안은 이동·공격·방어가 나가지 않는다.</summary>
    public bool IsSpecialActive { get; private set; }

    /// <summary>필살기 시작. 지금 다른 동작을 할 수 있을 때만 잠근다.</summary>
    public bool BeginSpecial()
    {
        if (!CanAct) return false;
        IsSpecialActive = true;
        ClearIntent();
        return true;
    }

    /// <summary>필살기 끝. 잠금을 푼다.</summary>
    public void EndSpecial() { IsSpecialActive = false; }

    /// <summary>필살기가 빗나간 뒤처럼, 외부 동작이 만든 무방비 시간을 표시한다.</summary>
    public void OpenFor(float seconds) { MarkOpen(seconds, OpeningCause.Whiffed); }
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

    // 공격 의도를 주고받을 상대 목록. 1:1 이라 보통 둘뿐이다.
    private static readonly List<Fighter> active = new List<Fighter>();
    private Fighter incomingFrom;     // 공격을 예고한 상대
    private CombatMove incomingMove;

    private float openUntil;          // 무방비가 끝나는 시각
    private Fighter3DView view3D;
    private Coroutine attackRoutine;
    private bool preciseRoutine;
    public CombatBodyPose Contacts { get; private set; }
    public float AttackStarted { get; private set; }
    public int AttackSerial { get; private set; }
    public bool PreciseAttackActive => preciseRoutine && IsAttacking && isActiveAndEnabled && !InputLocked && !Health.IsDead && !Health.IsHitStunned && Contacts.Ready;
    public event Action<Fighter, Vector3, CombatBodyPart, bool> ContactResolved;
    public bool HasContacted(Health target) => alreadyHit.Contains(target);
    public bool IsGuardedBy(Fighter target)
    {
        if (!target || !target.IsBlocking || !FacesMe(target)) return false;
        var height=HeightOf(CurrentMove);
        return height==AttackHeight.Mid || (height==AttackHeight.Low && target.IsCrouching) || (height==AttackHeight.High && !target.IsCrouching);
    }

    // ── 3D(포즈 접촉) 공격의 판정 결과 ──
    // CombatContactWorld 가 접촉마다 ResolveContact 를 부르고, 매 프레임 끝에 FinishPreciseAttack 을 부른다.
    // 판정 구간(Window)이 끝나면 직격/막힘/헛침을 확정하고 2D 경로(ResolveHit)와 같은 후딜 규칙을 적용한다.
    private FighterAttack preciseAttack;
    private bool preciseHitFrameSent;   // AttackHitFrame 을 보냈는지
    private bool preciseResolved;       // 판정 구간이 끝나 결과를 확정했는지
    private bool preciseLanded, preciseBlocked;
    private float precisePunish;        // 막힘이면 방어한 쪽 blockPunish 중 가장 큰 값
    private float preciseEndsAt;        // 후딜(+벌칙)까지 끝나는 시각

    public void FinishPreciseAttack(float now)
    {
        if (!preciseRoutine || !IsAttacking) return;
        if (InputLocked || Health.IsDead || Health.IsHitStunned) { CancelAttack(); return; }

        if (!preciseResolved && now - AttackStarted > CombatContactWorld.Window(CurrentMove).y)
        {
            preciseResolved = true;
            ClearIntent();
            float punish = preciseBlocked ? precisePunish : !preciseLanded ? whiffPunish : 0f;
            OpeningCause cause = preciseBlocked ? OpeningCause.Blocked : !preciseLanded ? OpeningCause.Whiffed : OpeningCause.None;
            preciseEndsAt = AttackStarted + Fighter3DView.Duration(CurrentMove) + punish;
            // 남은 후딜 전체가 상대에게는 공격 기회다.
            if (punish > 0f) MarkOpen(preciseEndsAt - now, cause);
        }

        if (preciseResolved && now >= preciseEndsAt) CancelAttack();
    }

    public void ResolveContact(Fighter target, Vector3 point, CombatBodyPart part)
    {
        alreadyHit.Add(target.Health);
        bool targetGuarding = target.IsBlocking;
        bool guarded=IsGuardedBy(target);
        // 방어에 성공하면 피해를 입지 않는다. 넉백도 없다.
        int damage=guarded ? 0 : attackDamage;
        Vector2 force=guarded ? Vector2.zero : new Vector2(knockback.x*Facing,knockback.y);
        if (!target.Health.TakeDamage(damage,force,guarded)) return;

        if (guarded)
        {
            // 성공: 막은 쪽은 경직 없이 즉시 풀리고(기억해 둔 동작이 있으면 바로 나간다), 공격한 쪽은 후딜이 길어진다.
            target.Health.ClearStun();
            target.GuardSucceeded();
            preciseBlocked = true;
            precisePunish = Mathf.Max(precisePunish, target.blockPunish);
        }
        else
        {
            // 뚫림: 방어는 끝나고 일반 피격 경직을 받는다. 직격이면 맞은 쪽 경직이 늘어난다.
            if (targetGuarding) target.EndGuard();
            preciseLanded = true;
            target.Health.ExtendStun(hitStunBonus);
        }

        HitVfx.Play(point,guarded);
        bool finisher=target.Health.IsDead;
        CombatSfx.PlayHit(guarded, finisher);
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

    void OnEnable()
    {
        if (!active.Contains(this)) active.Add(this);
        if (Health != null)
        {
            Health.OnDamaged += OnDamaged;
            Health.OnDamaged += InterruptAttack;
        }
        CombatContactWorld.Register(this);
    }

    void OnDisable()
    {
        if (Health != null)
        {
            Health.OnDamaged -= OnDamaged;
            Health.OnDamaged -= InterruptAttack;
        }
        CancelAttack();
        CombatContactWorld.Unregister(this);
        active.Remove(this);
        incomingFrom = null;
    }

    // 공격 도중 맞으면(막혀도) 공격은 끊긴다.
    void InterruptAttack(bool guarded) { if (IsAttacking) CancelAttack(); }

    /// <summary>진행 중인 공격을 접는다. 상대에게 알린 공격 예고도 거둔다.</summary>
    public void CancelAttack()
    {
        if (attackRoutine != null) StopCoroutine(attackRoutine);
        attackRoutine = null; preciseRoutine = false; preciseAttack = null; IsAttacking = false;
        ClearIntent();
    }

    void Update()
    {
        // 3D 공격: 판정 구간이 시작되면 AttackHitFrame 을 한 번 보낸다. (FighterAI 가 여기서 가드를 확정한다)
        if (preciseRoutine && IsAttacking && !preciseHitFrameSent
            && Time.time - AttackStarted >= CombatContactWorld.Window(CurrentMove).x)
        {
            preciseHitFrameSent = true;
            if (preciseAttack != null) AttackHitFrame?.Invoke(preciseAttack);
        }

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
            MarkOpen(guardFailDelay, OpeningCause.GuardFailed);
            Debug.Log($"[Fighter] {name} 방어 실패 (막은 공격 없음) → {guardFailDelay:0.##}초 딜레이");
        }
    }

    /// <summary>방어 시전. 시작됐으면 true. low 면 앉아막기(하단 가드)로 끝날 때까지 고정된다.</summary>
    public bool TryGuard(bool low)
    {
        if (!CanAct) return false;

        guarding = true;
        // 상대가 이미 공격을 정했으면 그 공격에 맞는 자세로 막는다. 이후 예고가 바뀌면 ReceiveIntent 가 다시 맞춘다.
        guardCrouching = AutoGuardLow(low);
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

    private void MarkOpen(float seconds, OpeningCause cause)
    {
        if (seconds <= 0f) return;
        openUntil = Time.time + seconds;
        OpenDuration = seconds;
        OpenCause = cause;
    }

    /// <summary>막히지 않은 타격을 맞으면 무방비는 끝난다 — 기회를 살린 것이다.</summary>
    private void OnDamaged(bool guarded)
    {
        if (!guarded) openUntil = 0f;
    }

    public static AttackHeight HeightOf(CombatMove move)
    {
        return move == CombatMove.HighKick ? AttackHeight.High
             : move == CombatMove.LowKick ? AttackHeight.Low
             : AttackHeight.Mid;
    }

    /// <summary>
    /// 방어 높이는 자동이다. 상대가 예고한 공격이 하단(로우킥)이면 하단 방어, 아니면 서서 방어.
    /// 예고가 없을 때만 입력한 자세를 쓴다 — 단, 3D 복싱에는 앉기가 없으므로 항상 서서 방어로 시작한다.
    /// </summary>
    private bool AutoGuardLow(bool requestedLow)
    {
        if (IncomingMove.HasValue) return HeightOf(IncomingMove.Value) == AttackHeight.Low;
        return view3D == null && requestedLow;
    }

    /// <summary>
    /// 어떤 공격을 할지 정했다고 상대에게 알린다. 상대는 방어 중이면 그 즉시, 아니면 이후 방어를 시전할 때
    /// 그 공격에 맞는 방어 자세와 모션을 자동으로 취한다. 판정이 끝나거나 공격을 접으면 ClearIntent 로 거둔다.
    /// </summary>
    public void DeclareIntent(CombatMove move)
    {
        for (int i = 0; i < active.Count; i++)
            if (active[i] != this) active[i].ReceiveIntent(this, move);
    }

    /// <summary>DeclareIntent 로 알린 공격을 거둔다.</summary>
    public void ClearIntent()
    {
        for (int i = 0; i < active.Count; i++)
            if (active[i] != this && active[i].incomingFrom == this) active[i].incomingFrom = null;
    }

    private void ReceiveIntent(Fighter attacker, CombatMove move)
    {
        incomingFrom = attacker;
        incomingMove = move;

        // 이미 방어 중이면 자세를 바로 맞춘다. 상단·중단은 서서, 하단은 앉아서.
        if (guarding) guardCrouching = HeightOf(move) == AttackHeight.Low;
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
        alreadyHit.Clear(); AttackSerial++; AttackStarted=Time.time;
        AttackHeight height = HeightOf(move);

        // 어떤 공격인지 상대에게 알린다. 상대 방어 자세·모션이 이걸 따른다.
        // (EnemyAI 는 공격 딜레이 전에, 공격을 정한 순간 이미 알렸다. 여기서 다시 알려도 같은 값이다)
        DeclareIntent(move);

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

        // Humanoid(3D)는 포즈 접촉 판정(CombatContactWorld), 스프라이트는 기존 판정 박스(AttackRoutine).
        preciseRoutine = view3D != null && Contacts.Ready;
        if (preciseRoutine)
        {
            IsAttacking = true;
            preciseAttack = attack;
            preciseHitFrameSent = preciseResolved = preciseLanded = preciseBlocked = false;
            precisePunish = 0f;
        }
        else attackRoutine = StartCoroutine(AttackRoutine(attack));
        return true;
    }

    private IEnumerator AttackRoutine(FighterAttack attack)
    {
        IsAttacking = true;

        float startup = view3D != null ? Fighter3DView.Impact(CurrentMove) : windup;
        float ending = view3D != null ? Fighter3DView.Duration(CurrentMove) - startup : recovery;
        yield return new WaitForSeconds(startup);

        float punish = 0f;
        OpeningCause cause = OpeningCause.None;
        if (!Health.IsDead && !InputLocked && !Health.IsHitStunned)
        {
            AttackHitFrame?.Invoke(attack);
            punish = ResolveHit(attack.Height, out cause);
        }

        // 판정이 끝났다(혹은 맞아서 끊겼다). 예고를 거둔다.
        ClearIntent();

        // 막히거나 헛치면 후딜이 길어진다. 그동안은 공격 중(IsAttacking)이라 이동·공격·방어 모두 못 한다.
        // 남은 후딜 전체가 상대에게는 공격 기회다.
        if (punish > 0f) MarkOpen(ending + punish, cause);

        yield return new WaitForSeconds(ending + punish);

        IsAttacking = false;
        attackRoutine = null;
    }

    /// <summary>
    /// 판정. 공격한 쪽 후딜에 더할 시간(초)을 돌려준다.
    ///   직격: 0. 대신 맞은 쪽 경직이 hitStunBonus 만큼 늘어난다.
    ///   막힘: 방어한 쪽의 blockPunish.
    ///   헛침(아무에게도 대미지가 들어가지 않음): 내 whiffPunish.
    /// </summary>
    private float ResolveHit(AttackHeight height, out OpeningCause cause)
    {
        float punish = 0f;
        bool landed = false;    // 직격이 하나라도 들어갔는지
        bool blocked = false;   // 막힌 타격이 하나라도 있었는지
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
                    damage = 0;   // 방어에 성공하면 피해를 입지 않는다. 넉백도 없다.
                    kb = Vector2.zero;
                    Debug.Log($"[Fighter] {name} 의 {height} 공격을 {targetFighter.name} 가 방어 (데미지 {damage})");
                }
                else
                {
                    Debug.Log($"[Fighter] {name} 의 {height} 공격이 {targetFighter.name} 의 가드를 뚫음");
                }
            }

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
                    // 뚫림: 방어는 끝나고 일반 피격 경직을 받는다.
                    targetFighter.EndGuard();
                }
            }

            // 공격 규칙: 직격이면 맞은 쪽 경직이 늘어난다. 막혔으면 위 방어 규칙이 처리했다.
            if (damaged)
            {
                if (guarded)
                {
                    blocked = true;
                }
                else
                {
                    landed = true;
                    targetHealth.ExtendStun(hitStunBonus);
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

        // 공격 실패(헛침): 공격한 쪽 후딜이 늘어난다.
        if (!landed && !blocked) punish = whiffPunish;

        cause = blocked ? OpeningCause.Blocked : !landed ? OpeningCause.Whiffed : OpeningCause.None;
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
