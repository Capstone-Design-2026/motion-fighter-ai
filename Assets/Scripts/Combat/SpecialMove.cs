using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 필살기 파운장(破雲掌). 내 일반 공격이 막히지 않고 들어갈 때 공격자의 게이지가 1씩 차고, 5회면 준비된다.
/// 막힌 공격·피격·방어·헛치기·파운장 자체 적중은 세지 않는다. 스토리와 PvP가 같은 규칙을 쓴다.
///
/// 흐름
///   발동  : 짧은 슬로우와 컷인(破雲掌), 화면 줌·블룸.
///   모으기: 일반 입력은 한 번으로 최소 minCharge만 모은 뒤 자동 발사한다.
///           BeginCharge/Release 검사 API만 최대 maxCharge까지 유지할 수 있다.
///           이때 맞으면 끊기고 게이지 일부를 돌려받는다. 적 AI 는 이 자세를 보고 방어를 시도할 수 있다.
///   발사  : SPECIAL_RELEASE 의 ReleaseImpact 시점에 기파(QiWave)가 손을 떠난다.
///   적중  : 정타는 큰 피해·넉백, 방어는 "기 관통"으로 일부 피해만 들어가고 방어가 풀린다. 빗나가면 짧은 무방비.
///
/// 입력: 즉발로 고정(2026-11-12). 키보드 activateKey 한 번, 웹캠 X자(SPECIAL) 한 번이면 최소 시간만 모으고 바로 발사한다.
/// 길게 모으기(BeginCharge/Release)는 API 로만 남겨 둔다(검사용).
///
/// timeScale 소유권: 발동 슬로우 동안만 timeScale 을 쥔다. 도중에 K.O. 가 나서 조작이 잠기면
/// BattleSceneBridge 가 timeScale 주인이므로 여기서는 손을 뗀다. 타격 히트스톱(HitImpact)은 발동 시 먼저 정리한다.
/// </summary>
[RequireComponent(typeof(Health))]
[RequireComponent(typeof(Fighter))]
[DisallowMultipleComponent]
public class SpecialMove : MonoBehaviour
{
    public enum Phase { Idle, CutIn, Charging, Releasing, Recovering }
    public enum Result { None, Hit, Blocked, Whiffed, Broken }

    [Header("게이지")]
    [Tooltip("내 일반 공격을 이만큼 맞히면 1회 사용할 수 있다. 막힌 공격과 파운장 자체의 적중은 세지 않는다.")]
    [SerializeField] private int hitsToCharge = 5;
    [Tooltip("켜면 내가 맞았을 때도(막은 타격은 제외) 1칸 찬다. 기본은 꺼짐: 공격이 들어갔을 때만 찬다.")]
    [SerializeField] private bool chargeWhenHit = false;

    [Header("피해량")]
    [Tooltip("기파 정타 피해. 끝까지 모으면 fullChargeBonus 만큼 늘어난다.")]
    [SerializeField] private int damage = 30;
    [Tooltip("방어로 막았을 때 들어가는 비율(기 관통).")]
    [Range(0f, 1f)] [SerializeField] private float guardDamageRatio = .25f;
    [Range(0f, 1f)] [SerializeField] private float fullChargeBonus = .2f;

    [Header("기 모으기 (초)")]
    [SerializeField] private float minCharge = .35f;
    [SerializeField] private float maxCharge = 2f;
    [SerializeField] private float fullChargeTime = 1.5f;
    [Tooltip("모으는 중에 맞아 끊기면 돌려받는 게이지 비율.")]
    [Range(0f, 1f)] [SerializeField] private float refundRatio = .5f;

    [Header("기파")]
    [SerializeField] private float waveSpeed = 12f;
    [SerializeField] private float waveRange = 6.5f;
    [SerializeField] private Vector2 knockback = new Vector2(6.5f, 1.4f);
    [Tooltip("방어로 막았을 때 밀려나는 처음 속도(m/s). 뜨지 않고 가로로만 미끄러진다. 2.1이면 약 0.2m 밀린다.")]
    [SerializeField] private float guardKnockback = 2.1f;
    [Tooltip("정타 시 맞은 쪽 경직에 더하는 시간.")]
    [SerializeField] private float hitExtraStun = .35f;
    [Tooltip("빗나갔을 때 쓴 쪽 무방비 시간.")]
    [SerializeField] private float whiffOpen = .4f;

    [Header("발동 연출")]
    [Range(0f, 1f)] [SerializeField] private float cutInSlowScale = .06f;
    [SerializeField] private float cutInDuration = .32f;
    [Range(.5f, 1f)] [SerializeField] private float chargeZoom = .86f;

    [Header("게이지 채우기 (테스트용)")]
    [Tooltip("맞지 않고도 게이지를 채우는 키. None 이면 꺼진다.")]
    [SerializeField] private KeyCode chargeKey = KeyCode.I;
    [SerializeField] private int chargeKeyAmount = 1;

    [Header("발동 키")]
    [Tooltip("누르고 있으면 기를 모으고, 떼면 발사한다. PlayerController 의 useKeyboard 가 꺼져 있으면 무시된다.")]
    [SerializeField] private KeyCode activateKey = KeyCode.O;

    /// <summary>(현재 게이지, 만충 수치)</summary>
    public event Action<int, int> OnChargeChanged;

    public int Charge => charge;
    public float VisualChargeLevel => level;
    public int HitsToCharge => hitsToCharge;
    public bool IsReady => charge >= hitsToCharge;
    public bool IsPlaying => phase != Phase.Idle;
    /// <summary>발동 컷인 또는 기를 모으는 중(아직 기파가 나가지 않았고, 맞으면 끊긴다).</summary>
    public bool IsCharging => phase == Phase.CutIn || phase == Phase.Charging;
    public Phase CurrentPhase => phase;
    public Result LastResult { get; private set; }
    public int LastDamage { get; private set; }
    public QiWave LastWave { get; private set; }
    public KeyCode ActivateKey => activateKey;
    public KeyCode ChargeKey => chargeKey;
    public int Damage => damage;
    public float MinCharge => minCharge;
    public float MaxCharge => maxCharge;

    private Health health;
    private Fighter fighter;
    private PlayerController controller;
    private Fighter3DView view;
    private QiPalmAssets assets;
    private AudioSource voice, loop;

    private int charge, spent;
    private Phase phase;
    private Coroutine routine;
    private bool releaseRequested, interrupted, waveLaunched, ownsTime;
    private float level;
    private QiChargeFx chargeFx;
    private GameObject readyLeft, readyRight;
    private float defaultFixedDelta = .02f;
    private const float MinFixedDelta = .001f;

    void Awake()
    {
        health = GetComponent<Health>();
        fighter = GetComponent<Fighter>();
        controller = GetComponent<PlayerController>();
        view = GetComponent<Fighter3DView>();
        assets = QiPalmAssets.Load();
        defaultFixedDelta = Time.fixedDeltaTime;
        voice = gameObject.AddComponent<AudioSource>(); voice.playOnAwake = false; voice.spatialBlend = 0f;
        loop = gameObject.AddComponent<AudioSource>(); loop.playOnAwake = false; loop.spatialBlend = 0f; loop.loop = true;
    }

    void Start()
    {
        if (RedboolEffectPreparation.Enabled)
        {
            RedboolEffectPreparation.Measure("special-font", QiCutIn.Prewarm);
            RedboolEffectPreparation.Measure("special-volume", QiPostFx.Prewarm);
            if (assets != null)
                foreach (var clip in new[] { assets.chargeStart, assets.chargeLoop, assets.release, assets.impactClip, assets.readyClip, assets.fizzle })
                    if (clip != null && clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
        }
    }

    void OnEnable()
    {
        health.OnDamaged += OnDamaged;
        fighter.AttackLanded += OnAttackLanded;
        OnChargeChanged?.Invoke(charge, hitsToCharge);
    }

    void OnDisable()
    {
        health.OnDamaged -= OnDamaged;
        fighter.AttackLanded -= OnAttackLanded;
        if (routine != null) { StopCoroutine(routine); routine = null; Cleanup(!fighter.InputLocked); }
        ClearReadyFx();
    }

    void Update()
    {
        // 게이지 채우기는 연출 확인용 단축키. 웹캠 테스트 중에도 쓰므로 useKeyboard 와 상관없이 받는다.
        if (RedboolDemoSettings.OperatorMode && !RedboolPauseMenu.BlocksStoryInput && !RedboolPvp.Active && chargeKey != KeyCode.None && Input.GetKeyDown(chargeKey)) AddCharge(chargeKeyAmount);

        if (controller == null || controller.useKeyboard)
        {
            // 방어 중에 누르면 공격처럼 방어를 풀고 기를 모으기 시작한다(Fighter.BeginSpecial).
            if (Input.GetKeyDown(activateKey)) Activate();
        }
    }

    void LateUpdate()
    {
        bool wantReady = IsReady && !IsPlaying && !health.IsDead && view != null && view.modelAnimator != null && view.modelAnimator.isHuman && assets != null && assets.ready != null;
        if (wantReady && readyLeft == null)
        {
            readyLeft = Instantiate(assets.ready, view.modelAnimator.GetBoneTransform(HumanBodyBones.LeftHand), false);
            readyRight = Instantiate(assets.ready, view.modelAnimator.GetBoneTransform(HumanBodyBones.RightHand), false);
        }
        else if (!wantReady) ClearReadyFx();
    }

    void ClearReadyFx()
    {
        if (readyLeft) Destroy(readyLeft);
        if (readyRight) Destroy(readyRight);
        readyLeft = readyRight = null;
    }

    /// <summary>게이지를 채운다. 가득 차는 순간 알림음이 난다.</summary>
    public void AddCharge(int amount = 1)
    {
        if (amount <= 0 || charge >= hitsToCharge) return;
        charge = Mathf.Min(hitsToCharge, charge + amount);
        OnChargeChanged?.Invoke(charge, hitsToCharge);
        if (IsReady)
        {
            PlayVoice(assets ? assets.readyClip : null, .7f);
            Debug.Log($"[SpecialMove] {name} 파운장 준비 완료 ({activateKey} 키)");
        }
    }

    /// <summary>라운드가 다시 시작될 때 게이지를 비운다.</summary>
    public void ResetCharge()
    {
        charge = 0;
        OnChargeChanged?.Invoke(charge, hitsToCharge);
    }

    /// <summary>기 모으기 시작. 게이지가 차 있고 지금 다른 동작을 하고 있지 않을 때만.</summary>
    public bool BeginCharge()
    {
        if (!IsReady || IsPlaying) return false;
        if (health.IsDead || health.IsHitStunned || fighter.InputLocked || fighter.VisionPreparationLocked) return false;
        // Character techniques own shared presentation, so a palm cannot overlap
        // one. Two PvP palms do not own global cut-in/time and may start together.
        // Consult the authoritative participants, never an arbitrary NPC.
        if (RedboolPvp.Active && RedboolPvp.Instance != null)
        {
            foreach (var other in RedboolPvp.Instance.Players)
            {
                if (other == null || other.GetComponent<Fighter>() == fighter) continue;
                var technique = other.GetComponent<EnemySpecial>();
                if (technique != null && technique.isActiveAndEnabled && technique.IsPlaying) return false;
            }
            if (!fighter.CanAttack || fighter.IsAttackSealed) return false;
            foreach (var participant in RedboolPvp.Instance.Players)
                if (participant != null) participant.GetComponent<EnemySpecial>()?.ClearIdlePresentation();
        }
        if (!fighter.BeginSpecial()) return false;

        spent = charge;
        charge = 0;
        OnChargeChanged?.Invoke(charge, hitsToCharge);
        releaseRequested = interrupted = waveLaunched = false;
        level = 0f;
        LastResult = Result.None; LastWave = null; LastDamage = 0;
        routine = StartCoroutine(Routine());
        return true;
    }

    /// <summary>모은 기를 발사한다. 최소 시간 전이면 그 시간이 되는 순간 나간다.</summary>
    public bool Release()
    {
        if (!IsCharging) return false;
        releaseRequested = true;
        return true;
    }

    /// <summary>즉시 발사(최소 시간만 모은다). 웹캠의 한 번짜리 SPECIAL 입력과 예전 호출부가 쓴다.</summary>
    public bool Activate()
    {
        if (!BeginCharge()) return false;
        releaseRequested = true;
        return true;
    }

    void OnDamaged(bool guarded)
    {
        if (IsCharging || (phase == Phase.Releasing && !waveLaunched)) interrupted = true;
        else if (chargeWhenHit && !guarded && !IsPlaying) AddCharge();
    }

    // Only successful unguarded normal attacks feed the meter; QiWave does not emit this event.
    void OnAttackLanded(Fighter target)
    {
        if (!IsPlaying) AddCharge();
    }

    // ─────────────────────── 진행 ───────────────────────

    IEnumerator Routine()
    {
        phase = Phase.CutIn;
        if (!RedboolPvp.Active) HitImpact.ReleaseTimeControl();
        if (!RedboolPvp.Active) QiPostFx.Set(1f);
        if (!RedboolPvp.Active) QiCameraFx.Zoom(chargeZoom);
        PlayVoice(assets ? assets.chargeStart : null, .9f);
        if (view != null) view.PlaySpecial(QiPalmTiming.ChargeState);
        if (assets != null && assets.charge != null)
        {
            chargeFx = Instantiate(assets.charge).GetComponent<QiChargeFx>();
            FollowFx(0f);
        }
        // PvP 에서도 컷인은 띄운다(2026-11-16): 다른 인물 11명의 필살기도 PvP 에서 컷인이 나온다. 손님 화면은 RedboolViewMirror 가 띄운다.
        if (assets != null && assets.brush != null) QiCutIn.Play(assets.brush, fighter.Facing > 0 ? -1 : 1, assets.cutInArt);

        ownsTime = !RedboolPvp.Active && !fighter.InputLocked;
        if (ownsTime) SetTimeScale(cutInSlowScale);
        float t = 0f;
        while (t < cutInDuration)
        {
            if (KoTookOver()) { Cleanup(false); yield break; }
            if (interrupted) { Break(); yield break; }
            t += Time.unscaledDeltaTime;
            FollowFx(0f);
            yield return null;
        }
        if (ownsTime) { RestoreTime(); ownsTime = false; }

        phase = Phase.Charging;
        if (assets != null && assets.chargeLoop != null) { loop.clip = assets.chargeLoop; loop.volume = .55f; loop.pitch = 1f; loop.Play(); }
        float held = 0f;
        while (true)
        {
            if (KoTookOver()) { Cleanup(false); yield break; }
            if (interrupted) { Break(); yield break; }
            held += Time.deltaTime;
            level = Mathf.Clamp01(held / Mathf.Max(.01f, fullChargeTime));
            FollowFx(level);
            loop.pitch = 1f + .3f * level; loop.volume = .55f + .3f * level;
            if (held >= maxCharge || (releaseRequested && held >= minCharge)) break;
            yield return null;
        }
        loop.Stop();

        phase = Phase.Releasing;
        if (view != null) view.PlaySpecial(QiPalmTiming.ReleaseState);
        if (!RedboolPvp.Active) QiCameraFx.Zoom(1f);
        float r = 0f;
        while (r < QiPalmTiming.ReleaseImpact)
        {
            if (KoTookOver()) { Cleanup(false); yield break; }
            if (interrupted) { Break(); yield break; }
            r += Time.deltaTime;
            FollowFx(level);
            yield return null;
        }
        LaunchWave();

        phase = Phase.Recovering;
        float rec = 0f, recover = QiPalmTiming.ReleaseLength - QiPalmTiming.ReleaseImpact - .1f;
        while ((rec < recover || (LastWave != null && !LastWave.Finished && rec < 1.5f)) && !KoTookOver()) { rec += Time.deltaTime; yield return null; }
        if (LastResult == Result.Whiffed && !KoTookOver())
        {
            float w = 0f;
            while (w < whiffOpen && !KoTookOver()) { w += Time.deltaTime; yield return null; }
        }
        fighter.EndSpecial();
        if (!RedboolPvp.Active) QiPostFx.Set(0f);
        phase = Phase.Idle;
        routine = null;
    }

    bool KoTookOver() => fighter.InputLocked || health.IsDead;

    void LaunchWave()
    {
        int dir = fighter.Facing >= 0 ? 1 : -1;
        Vector3 hands = HandsPoint();
        if (chargeFx != null) { chargeFx.End(); chargeFx = null; }
        var rot = Quaternion.LookRotation(dir > 0 ? Vector3.right : Vector3.left, Vector3.up);
        if (assets != null) QiFxLife.Spawn(assets.muzzle, hands, rot);
        PlayVoice(assets ? assets.release : null, 1f);
        if (!RedboolPvp.Active) QiCameraFx.Shake(.05f, 7f);

        int dmg = Mathf.RoundToInt(damage * (1f + fullChargeBonus * level));
        int chip = Mathf.Max(1, Mathf.RoundToInt(dmg * guardDamageRatio));
        LastDamage = dmg;
        var go = assets != null && assets.wave != null ? Instantiate(assets.wave, hands + Vector3.right * (dir * .12f), rot) : new GameObject("QiWave");
        go.transform.position = hands + Vector3.right * (dir * .12f);
        var wave = go.GetComponent<QiWave>();
        if (wave == null) wave = go.AddComponent<QiWave>();
        wave.speed = waveSpeed; wave.range = waveRange;
        wave.Launch(health, dir, dmg, chip, knockback, guardKnockback, hitExtraStun, OnWaveHit, OnWaveExpire);
        LastWave = wave;
        waveLaunched = true;
        Debug.Log($"[SpecialMove] {name} 파운장 발사 (모은 정도 {level:0.00}, 피해 {dmg} / 방어 시 {chip})");
    }

    void OnWaveHit(QiWave wave, Health target, bool guarded, Vector3 point)
    {
        RedboolViewLink.RecordEffect(point,guarded?3:2);
        LastResult = guarded ? Result.Blocked : Result.Hit;
        // 유지형 방어는 막아도 풀리지 않는다. 파운장은 기가 방어를 뚫고 들어가 방어를 풀어 버린다.
        if (guarded) target.GetComponent<Fighter>()?.ReleaseGuard();
        bool finisher = target.IsDead;
        if (assets != null) QiFxLife.Spawn(assets.impact, point, Quaternion.identity, guarded ? .65f : 1.1f, guarded ? .5f : 1f);
        PlayVoice(assets ? assets.impactClip : null, guarded ? .65f : 1f);
        if (!RedboolPvp.Active) QiCameraFx.Shake(guarded ? .05f : .13f, 5f);
        if (finisher) CombatSfx.PlayHit(false, true);
        HitImpact.Play(point, guarded, finisher, !finisher);
        Debug.Log($"[SpecialMove] 파운장 {(guarded ? "방어됨(기 관통)" : "적중")} → {target.DisplayName}");
    }

    void OnWaveExpire(QiWave wave, Vector3 point)
    {
        LastResult = Result.Whiffed;
        if (assets != null) QiFxLife.Spawn(assets.impact, point, Quaternion.identity, .4f, 0f);
    }

    /// <summary>모으는 중에 맞아 끊겼다. 게이지 일부를 돌려받는다.</summary>
    void Break()
    {
        loop.Stop();
        PlayVoice(assets ? assets.fizzle : null, .8f);
        if (assets != null) QiFxLife.Spawn(assets.impact, HandsPoint(), Quaternion.identity, .35f, 0f);
        int refund = Mathf.FloorToInt(spent * refundRatio);
        LastResult = Result.Broken;
        Cleanup(ownsTime && !fighter.InputLocked);
        if (refund > 0) { charge = Mathf.Min(hitsToCharge, charge + refund); OnChargeChanged?.Invoke(charge, hitsToCharge); }
        Debug.Log($"[SpecialMove] {name} 파운장 끊김 → 게이지 {refund} 반환");
    }

    public void CancelForPause()
    {
        if (routine!=null) StopCoroutine(routine);
        if (IsPlaying && !waveLaunched) { charge=Mathf.Min(hitsToCharge,charge+spent); OnChargeChanged?.Invoke(charge,hitsToCharge); }
        Cleanup(false);
    }

    void Cleanup(bool restoreTime)
    {
        if (loop) loop.Stop();
        if (chargeFx != null) { chargeFx.End(); chargeFx = null; }
        if (!RedboolPvp.Active) QiPostFx.Set(0f);
        if (!RedboolPvp.Active) QiCameraFx.Zoom(1f);
        if (restoreTime) RestoreTime();
        ownsTime = false;
        fighter.EndSpecial();
        phase = Phase.Idle;
        routine = null;
    }

    // ─────────────────────── 보조 ───────────────────────

    Vector3 HandsPoint()
    {
        var anim = view != null ? view.modelAnimator : null;
        if (anim != null && anim.isHuman)
        {
            var l = anim.GetBoneTransform(HumanBodyBones.LeftHand); var r = anim.GetBoneTransform(HumanBodyBones.RightHand);
            if (l && r) return (l.position + r.position) * .5f;
        }
        return transform.position + Vector3.up * 1.15f + Vector3.right * (fighter.Facing * .4f);
    }
    Vector3 FeetPoint() => view != null && view.visualRoot != null ? view.visualRoot.position : transform.position;
    void FollowFx(float lv) { if (chargeFx == null) return; chargeFx.Follow(HandsPoint(), FeetPoint()); chargeFx.SetLevel(lv); }
    void PlayVoice(AudioClip clip, float volume) { if (clip != null && voice != null) voice.PlayOneShot(clip, volume); }

    void SetTimeScale(float scale)
    {
        Time.timeScale = scale;
        // 물리 간격도 같이 줄여야 느려지는 동안 움직임이 끊기지 않는다(0 은 Unity 가 받지 않는다).
        Time.fixedDeltaTime = Mathf.Max(defaultFixedDelta * scale, MinFixedDelta);
    }
    void RestoreTime()
    {
        Time.timeScale = 1f;
        Time.fixedDeltaTime = defaultFixedDelta > 0f ? defaultFixedDelta : .02f;
    }
}
