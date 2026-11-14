using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 적 필살기. 적마다 하나씩 다른 기술을 가진다(스토리 칸 11곳). 주인공의 파운장(SpecialMove)과 짝이 되는 적 쪽 컴포넌트다.
///
/// 규칙
///   게이지  : 적의 일반 공격이 막히지 않고 들어갈 때마다 1칸. hitsToCharge 칸이 차면 가득 찬다.
///   발동    : 가득 차면 AI 가 스스로 쓴다(움직일 수 있게 되는 즉시). 스테이지당 1회. 쓰고 나면 게이지는 더 차지 않는다.
///   끊기    : 기를 모으는 동안(물러서기·컷인·모으기) 맞으면 끊기고 게이지 일부를 돌려받는다. 이때는 쓴 것으로 치지 않는다.
///             철문의 금종폐관만 예외로, 모으는 동안 맞아도 끊기지 않는다.
///   방어    : 첫 타격이 닿는 순간 주인공이 막고 있으면 그 필살기 전체가 막힌 것으로 친다.
///             피해가 guardDamageRatio 만 들어가고 부가 효과(끌려감·띄우기·봉인·가둠·게이지 탈취·흡수)는 걸리지 않는다.
///   피하기  : 자리를 노리는 타격(쇠문, 창살, 바위 등)은 그 자리에서 벗어나 있으면 맞지 않는다.
///
/// 연출 도구는 EnemySpecial.Fx.cs, 기술 11종은 EnemySpecial.Moves.cs 에 있다. 이펙트 재료는 EnemySpecialAssets(Resources)에서 온다.
/// EnemyAI 가 Awake 에서 붙인다. EnemyAI 가 꺼져 있으면(검사·미리보기) 게이지만 차고 발동하지 않는다.
/// timeScale 은 발동 슬로우 동안만 쥔다. K.O. 가 나면 BattleSceneBridge 가 주인이므로 손을 뗀다(SpecialMove 와 같은 규칙).
/// </summary>
[RequireComponent(typeof(Health))]
[RequireComponent(typeof(Fighter))]
[DisallowMultipleComponent]
[DefaultExecutionOrder(1000)]   // EnemyAI 와 Fighter3DView 뒤에 돌아야 물러서기 속도와 몸 띄우기가 덮이지 않는다
public sealed partial class EnemySpecial : MonoBehaviour
{
    public enum Phase { Idle, Retreat, CutIn, Gather, Acting }

    sealed class Spec
    {
        public string id, scene, hangul, hanja;
        public Color tint, smoke;
        public float gap = 2.2f;      // 발동 전에 이만큼 거리를 벌린다
        public bool armoured;         // 모으는 동안 맞아도 끊기지 않는다
        public Func<EnemySpecial, IEnumerator> run;
    }

    static readonly Spec[] Specs =
    {
        new Spec { id = "scout", scene = "ScoutPass", hangul = "잔영연환각", hanja = "殘影連環脚", tint = new Color(.45f, 1f, .7f), smoke = new Color(.55f, .7f, .62f), run = s => s.AfterimageKicks() },
        new Spec { id = "river", scene = "RiverAmbush", hangul = "잠룡출수", hanja = "潛龍出水", tint = new Color(.25f, .75f, .9f), smoke = new Color(.62f, .76f, .8f), run = s => s.DragonRise() },
        new Spec { id = "ridge", scene = "WindRidge", hangul = "천근추지렬", hanja = "千斤墜地裂", tint = new Color(1f, .55f, .15f), smoke = new Color(.42f, .32f, .22f), gap = 2.6f, run = s => s.ThousandWeight() },
        new Spec { id = "old_ferry", scene = "OldFerry", hangul = "승표탈기", hanja = "繩鏢奪氣", tint = new Color(.8f, .35f, 1f), smoke = new Color(.12f, .05f, .16f), gap = 2.6f, run = s => s.RopeDart() },
        new Spec { id = "gate", scene = "SealedGate", hangul = "금종폐관", hanja = "金鐘閉關", tint = new Color(1f, .82f, .42f), smoke = new Color(.3f, .29f, .27f), gap = 2.8f, armoured = true, run = s => s.GoldenBellGate() },
        new Spec { id = "quarry", scene = "HiddenQuarry", hangul = "격산비석", hanja = "隔山飛石", tint = new Color(1f, .8f, .25f), smoke = new Color(.46f, .4f, .3f), gap = 2.8f, run = s => s.FlyingBoulder() },
        new Spec { id = "east", scene = "EasternRidge", hangul = "선풍승룡", hanja = "旋風昇龍", tint = new Color(1f, .5f, .7f), smoke = new Color(.75f, .68f, .7f), run = s => s.Whirlwind() },
        new Spec { id = "camp", scene = "SearchCamp", hangul = "삼점봉혈", hanja = "三點封穴", tint = new Color(.75f, 1f, .15f), smoke = new Color(.12f, .18f, .05f), run = s => s.ThreePoints() },
        new Spec { id = "rescue", scene = "PrisonStockade", hangul = "진옥후", hanja = "震獄吼", tint = new Color(1f, .3f, .1f), smoke = new Color(.12f, .04f, .03f), run = s => s.PrisonRoar() },
        new Spec { id = "entrance", scene = "BlackCloudEntrance", hangul = "흑영교", hanja = "黑影咬", tint = new Color(.6f, .22f, .95f), smoke = new Color(.02f, 0f, .04f), run = s => s.ShadowBite() },
        new Spec { id = "final", scene = "SluiceFortress", hangul = "흑운압성", hanja = "黑雲壓城", tint = new Color(1f, .82f, .35f), smoke = new Color(.17f, .16f, .22f), gap = 2.8f, run = s => s.BlackCloud() },
    };

    [Header("게이지")]
    [Tooltip("적의 일반 공격이 이만큼 들어가면 가득 찬다. 막힌 공격은 세지 않는다.")]
    [SerializeField] private int hitsToCharge = 5;
    [Tooltip("가득 찬 뒤 이만큼 지나야 발동한다(초). 다섯 번째 타격과 겹치지 않게 하는 틈.")]
    [SerializeField] private float fireDelay = .35f;
    [Tooltip("모으는 중에 맞아 끊기면 돌려받는 게이지 비율.")]
    [Range(0f, 1f)] [SerializeField] private float refundRatio = .5f;

    [Header("피해")]
    [Tooltip("주인공이 막았을 때 들어가는 비율.")]
    [Range(0f, 1f)] [SerializeField] private float guardDamageRatio = .3f;

    [Header("발동 연출")]
    [Range(0f, 1f)] [SerializeField] private float cutInSlowScale = .06f;
    [SerializeField] private float cutInDuration = .32f;

    /// <summary>(현재 게이지, 만충 수치). 발동·사용 상태가 바뀔 때도 불린다.</summary>
    public event Action<int, int> OnChargeChanged;
    public event Action ChargeInterruptedByDamage;

    public int Charge => charge;
    public int HitsToCharge => hitsToCharge;
    /// <summary>이 적에게 필살기가 있고 이펙트 재료도 준비됐는지. 아니면 게이지를 보여 주지 않는다.</summary>
    public bool Available => Current != null && fx != null && qi != null;
    public bool Used => used;
    public bool IsReady => !used && charge >= hitsToCharge;
    public bool IsPlaying => phase != Phase.Idle;
    /// <summary>아직 기술이 나가지 않았고, 맞으면 끊기는 구간.</summary>
    public bool IsCharging => phase == Phase.Retreat || phase == Phase.CutIn || phase == Phase.Gather;
    public Phase CurrentPhase => phase;
    public string MoveName => Current != null ? Current.hangul : "";
    public string MoveHanja => Current != null ? Current.hanja : "";
    /// <summary>마지막 필살기가 주인공의 방어에 막혔는지(검사용).</summary>
    public bool LastGuarded => guardedSpecial;
    public int LastDamage { get; private set; }

    private Health foeHp;
    private Fighter foe;
    private Fighter3DView foeView;
    private Rigidbody2D body;
    private EnemyAI ai;
    private AudioSource voice;
    private Fighter hero;
    private Health heroHp;
    private Fighter3DView heroView;
    private SpecialMove heroSpecial;
    private QiPalmAssets qi;
    private EnemySpecialAssets fx;
    private Camera cam;

    private Spec spec;
    private bool specResolved;
    private int charge, spent;
    private bool used, interrupted, ownsTime, guardDecided, guardedSpecial;
    private Phase phase;
    private Coroutine routine;
    private float readyAt = -1f, S = 1f, defaultFixedDelta = .02f;
    private QiChargeFx chargeFx;
    private const float MinFixedDelta = .001f;

    /// <summary>이 전투의 적이 쓰는 기술. 스토리 칸 id 로 찾고, 없으면 씬 이름으로 찾는다. 둘 다 없으면 필살기가 없다.</summary>
    private Spec Current
    {
        get
        {
            if (specResolved) return spec;
            specResolved = true;
            var run = RunState.Instance;
            string id = run != null && run.PendingNode != null ? run.PendingNode.id : null;
            string scene = SceneManager.GetActiveScene().name;
            foreach (var candidate in Specs) if (candidate.id == id) { spec = candidate; break; }
            if (spec == null) foreach (var candidate in Specs) if (candidate.scene == scene) { spec = candidate; break; }
            return spec;
        }
    }

    void Awake()
    {
        foeHp = GetComponent<Health>();
        foe = GetComponent<Fighter>();
        foeView = GetComponent<Fighter3DView>();
        body = GetComponent<Rigidbody2D>();
        ai = GetComponent<EnemyAI>();
        qi = QiPalmAssets.Load();
        fx = EnemySpecialAssets.Load();
        defaultFixedDelta = Time.fixedDeltaTime;
        voice = gameObject.AddComponent<AudioSource>(); voice.playOnAwake = false; voice.spatialBlend = 0f;
    }

    void OnEnable()
    {
        foeHp.OnDamaged += OnDamaged;
        foe.AttackLanded += OnAttackLanded;
        Notify();
    }

    void OnDisable()
    {
        foeHp.OnDamaged -= OnDamaged;
        foe.AttackLanded -= OnAttackLanded;
        ClearOwnedEffects();
    }
    void OnDestroy() { ClearVisualMaterialCache(); }

    void Notify() => OnChargeChanged?.Invoke(charge, hitsToCharge);

    // ─────────────────────── PvP: 사람이 고른 인물의 필살기 ───────────────────────
    // PvP 에서는 AI 가 없다. RedboolPvp 가 인물에 맞는 기술을 맡기고(ConfigureForPlayer), 사람이 X자 동작을 하면 TryFire 로 쓴다.
    // 스테이지당 1회 제한 없이, 쓰고 나면 다시 모을 수 있다(파운장과 같은 규칙).
    // 두 화면 대전의 손님 화면은 판정 없이 연출만 따라 한다(visualOnly): 피해·경직·시간·카메라는 건드리지 않는다.
    private bool playerOwned, visualOnly, remoteGuarded;
    public bool PlayerOwned => playerOwned;
    /// <summary>발동할 때마다 1씩 오른다. 손님 화면이 같은 연출을 시작하는 신호다.</summary>
    public int FireSerial { get; private set; }
    public bool GuardDecided => guardDecided;
    public bool GuardedSpecial => guardedSpecial;

    /// <summary>이 인물(스토리 칸 id)의 기술을 맡기고 상대를 정한다. 기술과 이펙트 재료가 있으면 true.</summary>
    public bool ConfigureForPlayer(string id, Fighter target, bool visualsOnly)
    {
        ClearOwnedEffects();
        ClearVisualMaterialCache();
        spec = null;
        foreach (var candidate in Specs) if (candidate.id == id) { spec = candidate; break; }
        specResolved = true; playerOwned = true; visualOnly = visualsOnly;
        hero = target;
        heroHp = target != null ? target.GetComponent<Health>() : null;
        heroView = target != null ? target.GetComponent<Fighter3DView>() : null;
        heroSpecial = target != null ? target.GetComponent<SpecialMove>() : null;
        charge = 0; used = false; readyAt = -1f;
        Notify();
        return spec != null && Available && hero != null && heroHp != null;
    }

    /// <summary>사람이 필살기 동작을 했다. 게이지가 가득 차 있고 지금 쓸 수 있으면 시작한다.</summary>
    public bool TryFire()
    {
        if (!playerOwned || visualOnly || phase != Phase.Idle || !IsReady || !Available) return false;
        if (!foe.CanAttack || foe.IsAttackSealed || hero == null || heroHp == null) return false;
        if (heroHp.IsDead || hero.InputLocked || hero.VisionPreparationLocked || (heroSpecial != null && heroSpecial.enabled && heroSpecial.IsPlaying)) return false;
        var other = hero.GetComponent<EnemySpecial>();
        if (other != null && other.enabled && other.IsPlaying) return false;   // 두 사람의 필살기가 겹치지 않게 한다
        ClearIdlePresentation(); other?.ClearIdlePresentation();
        if (!foe.BeginSpecial()) return false;
        FireSerial++;
        routine = StartCoroutine(Run());
        return true;
    }

    /// <summary>손님 화면: 호스트에서 이 기술이 시작됐다. 판정 없이 연출만 처음부터 따라 한다.</summary>
    public void PlayRemote()
    {
        if (!playerOwned || !visualOnly || spec == null || !Available || hero == null) return;
        ClearOwnedEffects();
        remoteGuarded = false;
        routine = StartCoroutine(Run());
    }

    /// <summary>손님 화면: 호스트가 정한 '막혔는지'를 받아 둔다. 첫 타격 연출 때 쓴다.</summary>
    public void RemoteGuard(bool guarded) { remoteGuarded = guarded; if (visualOnly && guardDecided) guardedSpecial = guarded; }

    /// <summary>손님 화면: 호스트에서 기술이 모으는 중에 끊겼다. 아직 모으는 연출 중이면 같이 멈춘다.</summary>
    public void StopRemoteCharge() { if (visualOnly && IsCharging) { StopRoutine(); Cleanup(false); } }

    /// <summary>Stop the presentation on stale view, round end or host cancellation; never replay it on reconnect.</summary>
    public void StopRemote() { if (visualOnly) ClearOwnedEffects(); }

    /// <summary>Finish only an old idle fade before another cast captures lighting.
    /// Does not move the actor, unlock gameplay or add a wait between techniques.</summary>
    public void ClearIdlePresentation()
    {
        if (!playerOwned || phase != Phase.Idle || foe.IsSpecialActive || !HasOwnedEffects) return;
        StopRoutine(); Cleanup(false, false);
    }

    /// <summary>상대의 게이지 탈취(승표탈기)에 당했다.</summary>
    public void DrainCharge() { if (phase != Phase.Idle) return; charge = 0; readyAt = -1f; Notify(); }

    /// <summary>대전이 멈출 때(연결 끊김 등) 진행 중인 기술을 접는다. 아직 나가지 않았으면 게이지를 돌려준다.</summary>
    public void CancelForPause()
    {
        if (!HasOwnedEffects) return;
        bool fired = phase == Phase.Acting;
        bool charging = IsCharging;
        ClearOwnedEffects();
        if (charging && !fired) charge = Mathf.Clamp(spent, 0, hitsToCharge);
        used = false; readyAt = -1f;
        Notify();
    }

    // 승표탈기: 상대가 모아 둔 필살기 게이지를 비운다 (파운장이든 인물 고유 기술이든).
    void DrainTarget()
    {
        if (visualOnly || hero == null) return;
        if (heroSpecial != null) heroSpecial.ResetCharge();
        var other = hero.GetComponent<EnemySpecial>();
        if (other != null && other.enabled) other.DrainCharge();
    }

    /// <summary>게이지를 채운다(검사용으로도 쓴다). 이미 썼거나 필살기가 없는 적이면 무시한다.</summary>
    public void AddCharge(int amount = 1)
    {
        if (amount <= 0 || visualOnly || used || !Available || charge >= hitsToCharge) return;
        charge = Mathf.Min(hitsToCharge, charge + amount);
        if (charge >= hitsToCharge)
        {
            readyAt = Time.time + fireDelay;
            PlayVoice(qi.readyClip, .5f);
            Debug.Log($"[EnemySpecial] {foeHp.DisplayName} {spec.hangul} 준비 완료");
        }
        Notify();
    }

    /// <summary>라운드가 다시 시작될 때 처음 상태로 돌린다.</summary>
    public void ResetForRound()
    {
        ClearOwnedEffects();
        charge = 0; used = false; readyAt = -1f;
        Notify();
    }

    /// <summary>내 일반 공격이 막히지 않고 들어갔다 → 게이지 1칸.</summary>
    void OnAttackLanded(Fighter target)
    {
        if (phase == Phase.Idle) AddCharge();
    }

    void OnDamaged(bool guarded)
    {
        if (IsCharging) interrupted = true;
    }

    bool FindHero()
    {
        if (hero != null) return true;
        var player = FindAnyObjectByType<PlayerController>();
        if (player == null) return false;
        hero = player.GetComponent<Fighter>(); heroHp = player.GetComponent<Health>();
        heroView = player.GetComponent<Fighter3DView>(); heroSpecial = player.GetComponent<SpecialMove>();
        if (hero == null || heroHp == null) { hero = null; return false; }
        return true;
    }

    bool KoTookOver() => foe.InputLocked || foeHp.IsDead || (heroHp != null && heroHp.IsDead);

    // AI 적: PvP 이거나 AI 가 꺼졌거나 카메라 준비로 묶였으면 멈춘다(웅빈 v34).
    // PvP 에서 사람이 쓰는 기술(playerOwned): 라운드가 멈춰 두 사람이 묶였을 때만 멈춘다. 손님 화면의 연출 전용은 호스트 신호만 따른다.
    bool PreparationInterrupted => playerOwned
        ? !visualOnly && (foe.VisionPreparationLocked || (hero != null && hero.VisionPreparationLocked))
        : RedboolPvp.Active || ai == null || !ai.isActiveAndEnabled
          || foe.VisionPreparationLocked || (hero != null && hero.VisionPreparationLocked);

    // No coroutine, movement callback or late pose may outlive a camera pause.
    // A cast interrupted before release keeps its charge; an already released cast remains used.
    bool HasOwnedEffects => phase != Phase.Idle || routine != null || ownsTime || updaters.Count > 0
        || root != null || extras.Count > 0 || lightingCaptured;

    // Idle may still own the settle fades. Disabling or restarting must clear those
    // callbacks too, without overriding a newer KO/PvP time owner.
    void ClearOwnedEffects()
    {
        if (!HasOwnedEffects) return;
        StopRoutine();
        if (!visualOnly && body != null) body.linearVelocity = new Vector2(0f, body.linearVelocity.y);
        Cleanup(ownsTime && !RedboolPvp.Active && !KoTookOver());
    }

    public void CancelForPreparation()
    {
        bool charging = IsCharging;
        if (!HasOwnedEffects) return;
        ClearOwnedEffects();
        if (charging) {
            charge = Mathf.Clamp(spent, 0, hitsToCharge);
            readyAt = charge >= hitsToCharge ? Time.time + fireDelay : -1f;
            Notify();
        }
    }

    void Update()
    {
        if (hero == null && !playerOwned) FindHero();   // PvP 에서는 상대를 RedboolPvp 가 정해 준다(첫 PlayerController 가 자기 자신일 수 있다)
        if (PreparationInterrupted) { CancelForPreparation(); return; }
        StepUpdaters();
        StepLook();
        if (phase != Phase.Idle)
        {
            if (KoTookOver()) { StopRoutine(); Cleanup(false); return; }
            if (interrupted && IsCharging && !spec.armoured) Break();
            interrupted = false;
            return;
        }
        if (playerOwned || !IsReady || !Available || Time.time < readyAt) return;
        if (ai == null || !ai.enabled || !foe.CanAttack || !FindHero()) return;
        if (heroHp.IsDead || hero.InputLocked || hero.VisionPreparationLocked || (heroSpecial != null && heroSpecial.IsPlaying)) return;
        if (!foe.BeginSpecial()) return;
        routine = StartCoroutine(Run());
    }

    void LateUpdate() => ApplyPose();

    // ─────────────────────── 진행 ───────────────────────

    IEnumerator Run()
    {
        phase = Phase.Retreat;
        interrupted = guardDecided = guardedSpecial = false; LastDamage = 0;
        spent = charge; charge = 0;
        Notify();
        S = FoeX >= HeroX ? 1f : -1f;
        cam = Camera.main;
        CaptureLighting();
        Debug.Log($"[EnemySpecial] {foeHp.DisplayName} {spec.hangul} 발동");
        yield return Retreat();
        yield return spec.run(this);
        phase = Phase.Idle; routine = null;
        if (playerOwned) used = false;   // PvP: 다시 모아서 또 쓸 수 있다
        Notify();
    }

    /// <summary>기술이 한 화면에 담기도록 뒤로 물러선다. 벽에 막히면 물리가 멈춰 세우므로 그 자리에서 쓴다.</summary>
    IEnumerator Retreat()
    {
        float t = 0f;
        if (Mathf.Abs(FoeX - HeroX) < spec.gap && foeView != null) foeView.PlaySpecial("WALK_BACK");
        while (t < .5f && Mathf.Abs(FoeX - HeroX) < spec.gap)
        {
            t += Time.deltaTime;
            if (!visualOnly && body != null) body.linearVelocity = new Vector2(S * 6.5f, body.linearVelocity.y);
            yield return null;
        }
        if (!visualOnly && body != null) body.linearVelocity = new Vector2(0f, body.linearVelocity.y);
    }

    /// <summary>발동: 화면이 어두워지고 컷인이 지나간 뒤(짧은 슬로우) 기술마다 다른 시간만큼 기를 모은다. 여기까지가 끊길 수 있는 구간이다.</summary>
    IEnumerator Gather(float seconds, float scale, string state = "SPECIAL_CHARGE")
    {
        phase = Phase.CutIn;
        if (!visualOnly) HitImpact.ReleaseTimeControl();
        QiPostFx.Set(1f); Dim(.6f, .25f);
        PlayVoice(qi.chargeStart, .9f);
        if (foeView != null) foeView.PlaySpecial(state);
        var charging = Fx(qi.charge, Hands(), Quaternion.identity, scale);
        chargeFx = charging != null ? charging.GetComponent<QiChargeFx>() : null;
        CutIn();

        ownsTime = !visualOnly && !foe.InputLocked;
        if (ownsTime) SetTimeScale(cutInSlowScale);
        float t = 0f;
        while (t < cutInDuration) { t += Time.unscaledDeltaTime; FollowCharge(0f, scale); yield return null; }
        if (ownsTime) { RestoreTime(); ownsTime = false; }

        phase = Phase.Gather;
        float held = 0f;
        while (held < seconds) { held += Time.deltaTime; FollowCharge(held / Mathf.Max(.01f, seconds), scale); yield return null; }
        if (chargeFx != null) { chargeFx.End(); chargeFx = null; }

        phase = Phase.Acting; used = true;
        PlayVoice(qi.release, 1f);
        Notify();
    }

    void FollowCharge(float level, float scale)
    {
        if (chargeFx == null) return;
        chargeFx.Follow(Hands(), Ground(FoeX)); chargeFx.SetLevel(Mathf.Clamp01(level)); chargeFx.transform.localScale = Vector3.one * scale;
    }

    IEnumerator Settle()
    {
        yield return Hold(.35f);
        foeLift = Vector3.zero; foeSpin = foeTilt = 0f;
        if (foeView != null) foeView.PlaySpecial("IDLE");
        if (!visualOnly) foe.EndSpecial();
        QiPostFx.Set(0f); Dim(1f, .5f); Pull(0f, .5f);
    }

    /// <summary>모으는 중에 맞아 끊겼다. 게이지 일부를 돌려받고, 쓴 것으로 치지 않는다.</summary>
    void Break()
    {
        StopRoutine();
        PlayVoice(qi.fizzle, .8f);
        QiFxLife.Spawn(qi.impact, Hands(), Quaternion.identity, .35f, 0f);
        int refund = Mathf.FloorToInt(spent * refundRatio);
        Cleanup(ownsTime && !foe.InputLocked);
        charge = Mathf.Clamp(refund, 0, hitsToCharge - 1); readyAt = -1f;
        Notify();
        if (playerOwned && !visualOnly) ChargeInterruptedByDamage?.Invoke();
        Debug.Log($"[EnemySpecial] {foeHp.DisplayName} {spec.hangul} 끊김 → 게이지 {charge} 반환");
    }

    void StopRoutine()
    {
        if (routine != null) StopCoroutine(routine);
        routine = null;
    }

    /// <summary>도중에 멈출 때(끊김, K.O.) 띄워 둔 것을 치우고 화면·조명·시간을 원래대로 돌린다.</summary>
    void Cleanup(bool restoreTime, bool releaseGameplay = true)
    {
        updaters.Clear();
        if (chargeFx != null) { chargeFx.End(); chargeFx = null; }
        foeLift = heroLift = Vector3.zero; foeSpin = foeTilt = heroSpin = heroTilt = 0f;
        ShowFoe(true);
        QiPostFx.Set(0f); if (!visualOnly) QiCameraFx.Pull(0f);
        SetDim(1f); RestoreSky();
        if (restoreTime) RestoreTime();
        ownsTime = false;
        if (!visualOnly && releaseGameplay) foe.EndSpecial();
        if (root != null) { Destroy(root.gameObject, .5f); root = null; }
        foreach (var extra in extras) if (extra) Destroy(extra);
        extras.Clear();
        phase = Phase.Idle;
        Notify();
    }

    void PlayVoice(AudioClip clip, float volume) { if (clip != null && voice != null) voice.PlayOneShot(clip, volume); }

    void SetTimeScale(float scale)
    {
        if (visualOnly) return;
        Time.timeScale = scale;
        Time.fixedDeltaTime = Mathf.Max(defaultFixedDelta * scale, MinFixedDelta);
    }
    void RestoreTime()
    {
        if (visualOnly) return;
        Time.timeScale = 1f;
        Time.fixedDeltaTime = defaultFixedDelta > 0f ? defaultFixedDelta : .02f;
    }

    // ─────────────────────── 타격 ───────────────────────

    /// <summary>
    /// 주인공에게 한 대. at 을 주면 그 자리에서 reach 안에 있을 때만 맞는다(자리를 노리는 타격). 안 주면 따라가서 맞는다.
    /// 첫 타격 때 막고 있었으면 이 필살기 전체가 막힌 것으로 친다. 닿았으면(막혔어도) true.
    /// </summary>
    bool Strike(int damage, Vector2 knockback, float fxScale, float shake, float height = 1.15f, float at = float.NaN, float reach = 1.5f)
    {
        if (visualOnly)
        {
            // 손님 화면: 피해는 호스트가 준다. 같은 자리 조건으로 맞았는지만 가려서 터지는 연출을 낸다.
            if (hero == null) return false;
            if (!float.IsNaN(at) && Mathf.Abs(HeroX - at) > reach) return false;
            if (!guardDecided) { guardDecided = true; guardedSpecial = remoteGuarded; }
            if (fxScale > 0f) Burst(new Vector3(HeroX, height, 0f), guardedSpecial ? fxScale * .6f : fxScale);
            return true;
        }
        if (hero == null || heroHp.IsDead || PreparationInterrupted) return false;
        if (!float.IsNaN(at) && Mathf.Abs(HeroX - at) > reach) return false;
        if (!guardDecided) { guardDecided = true; guardedSpecial = hero.IsBlocking; }
        bool guarded = guardedSpecial;
        int dealt = guarded ? Mathf.Max(1, Mathf.RoundToInt(damage * guardDamageRatio)) : damage;
        Vector3 point = new Vector3(HeroX, height, 0f);
        if (!heroHp.TakeDamage(dealt, guarded ? new Vector2(knockback.x * .25f, 0f) : knockback, guarded, true)) return false;
        LastDamage += dealt;
        if (fxScale > 0f) Burst(point, guarded ? fxScale * .6f : fxScale);
        Shake(guarded ? shake * .5f : shake, 7f);
        if (shake >= .2f)
        {
            // 큰 타격만 타격음과 히트스톱을 준다. 잔타까지 멈추면 연타가 끊겨 보인다.
            bool finisher = heroHp.IsDead;
            PlayVoice(qi.impactClip, guarded ? .65f : 1f);
            if (finisher) CombatSfx.PlayHit(false, true);
            HitImpact.Play(point, guarded, finisher, !finisher);
        }
        return true;
    }

    /// <summary>막히지 않았을 때만 주인공을 묶어 둔다.</summary>
    void Stun(float seconds) { if (visualOnly) return; if (guardDecided && !guardedSpecial && heroHp != null) heroHp.ExtendStun(seconds); }

    /// <summary>주인공을 밀어내는 넉백(적에게서 멀어지는 쪽).</summary>
    Vector2 Away(float speed) => new Vector2(-S * speed, 0f);
}
