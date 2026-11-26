using UnityEngine;

/// <summary>One boxing vocabulary for every Humanoid on the 2D combat plane.</summary>
[DefaultExecutionOrder(100)]
public sealed class Fighter3DView : MonoBehaviour
{
    public Animator modelAnimator;
    public Transform visualRoot;
    public Color fighterTint = Color.white;
    public AnimationClip[] strikeClips;
    public AnimationClip[] guardClips;
    /// <summary>
    /// 걷기 방식(주인공 전용 스텝을 쓰지 않는 캐릭터, 곧 적에게 해당).
    /// true: 걷기 클립이 발을 직접 딛는다. 적의 걷기 클립은 주인공 스텝을 옮긴 것이라 주인공과 같은 걸음이 된다(2026-11-13).
    /// false: 예전 방식. 코드가 실제 이동에 맞춰 발을 IK로 딛게 한다(GroundedBoxingWalk).
    /// </summary>
    public bool authoredSteps = true;
    // The old run-speed travel forced tiny boxing steps to play several times too fast.
    // Scale only grounded 3D locomotion; stage-relative speeds and 2D mode stay intact.
    public const float BoxingTravelScale = .4f;
    public const float HurtDuration = .70f;
    public const float GuardReactionDuration = .225f;
    const float GuardEntry = .27f, GuardRelease = .252f, GuardSwitchBlend = .14f;
    // 방어 중 이동(높은 방어 상체 + 이동 하체). 이 상태가 있는 애니메이터(주인공)만 방어 중에 움직일 수 있다.
    //   GUARD_WALK_*: 공통 걷기(WALK_FORWARD/BACK) 하체. RunConfig.heroMovement = CommonWalk 일 때.
    //   GUARD_STEP_*: 승인 스텝(HERO_*_STEP) 하체. ApprovedWalk / DashAndStep 일 때.
    public const string GuardStepForward = "GUARD_STEP_FORWARD", GuardStepBackward = "GUARD_STEP_BACKWARD";
    public const string GuardWalkForward = "GUARD_WALK_FORWARD", GuardWalkBack = "GUARD_WALK_BACK";
    static readonly int GuardStepForwardHash = Animator.StringToHash(GuardStepForward), GuardWalkForwardHash = Animator.StringToHash(GuardWalkForward);
    bool HeroStepping => heroMovement != null && heroMovement.isActiveAndEnabled;
    static readonly string[] Attacks = { "JAB", "CROSS", "HIGHKICK", "LOWKICK" };
    Fighter fighter;
    bool cameraPlayer;
    Health health;
    Renderer[] renderers;
    MaterialPropertyBlock properties;
    Vector3 origin;
    string currentState;
    float stateStarted, reactionUntil, flashUntil;
    bool guardFlash;
    GroundedBoxingWalk walk;
    HeroAuthoredMovement heroMovement;
    int movementRevision = -1;
    // 걷기 접지: 걷는 동안 가장 낮은 발바닥이 바닥에 닿도록 겉모습만 위아래로 맞춘다.
    // 낮은 발바닥이 바닥 위 0~4mm(IK 걷기가 발을 놓는 높이) 안이면 그대로 두고, 벗어난 만큼만 옮긴다.
    const float SoleBand = .004f, MaxGroundShift = .04f;
    float groundWeight, groundShift;
    public GroundedBoxingWalk Walk => walk;
    public string CurrentState => currentState;
    /// <summary>방어한 채로 걸을 수 있는지. 지금 이동 방식에 맞는 방어 걷기 상태(GUARD_STEP_* 또는 GUARD_WALK_*)가 애니메이터에 있을 때만 true.</summary>
    public bool SupportsGuardStep => modelAnimator != null && modelAnimator.runtimeAnimatorController != null
        && modelAnimator.HasState(0, HeroStepping ? GuardStepForwardHash : GuardWalkForwardHash);
    /// <summary>이번 프레임에 걷기 접지가 겉모습을 옮긴 높이(m, +는 올림). 검사용.</summary>
    public float GroundShift { get; private set; }

    void CacheVisual()
    {
        cameraPlayer = GetComponent<PlayerController>() != null;
        renderers = visualRoot.GetComponentsInChildren<Renderer>();
        properties = new MaterialPropertyBlock();
        origin = visualRoot.localPosition;
        modelAnimator.applyRootMotion = false;
        modelAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        walk = new GroundedBoxingWalk(visualRoot, modelAnimator);
        heroMovement = GetComponent<HeroAuthoredMovement>();
    }
    void Awake()
    {
        fighter = GetComponent<Fighter>(); health = GetComponent<Health>();
        if (visualRoot != null && modelAnimator != null) CacheVisual();
    }
    void OnEnable() { if (health != null) health.OnDamaged += OnDamaged; }
    void OnDisable() { if (health != null) health.OnDamaged -= OnDamaged; }

    // BattleSceneBridge may replace the visual before this component's Awake.
    public bool ApplyVisual(GameObject prefab)
    {
        if (prefab == null) return false;
        var template = prefab.GetComponentInChildren<Animator>(true);
        if (!prefab.activeSelf || template == null || template.avatar == null
            || !template.avatar.isValid || !template.avatar.isHuman)
        {
            Debug.LogError("[Fighter3DView] Valid Humanoid required: " + prefab.name);
            return false;
        }
        var oldRoot = visualRoot;
        var fallback = modelAnimator != null ? modelAnimator.runtimeAnimatorController : null;
        var replacement = Instantiate(prefab, transform, false);
        replacement.name = prefab.name;
        visualRoot = replacement.transform;
        visualRoot.localPosition = oldRoot != null ? oldRoot.localPosition : Vector3.zero;
        visualRoot.localRotation = oldRoot != null ? oldRoot.localRotation : Quaternion.identity;
        modelAnimator = replacement.GetComponentInChildren<Animator>(true);
        if (modelAnimator.runtimeAnimatorController == null) modelAnimator.runtimeAnimatorController = fallback;
        CacheVisual();
        ResetPresentation();
        if (oldRoot != null) { oldRoot.gameObject.SetActive(false); Destroy(oldRoot.gameObject); }
        return true;
    }
    public static float Duration(CombatMove move)
    {
        switch (move) { case CombatMove.Jab: return .60f; case CombatMove.HighKick: return 1.24f;
            case CombatMove.LowKick: return 1.14f; default: return .96f; }
    }
    public static float Impact(CombatMove move)
    {
        // Straight: the fist is fully out at .167s in the installed clip (it was .40s in the previous one).
        switch (move) { case CombatMove.Jab: return .20f; case CombatMove.Straight: return .17f; case CombatMove.HighKick: return .48f;
            case CombatMove.LowKick: return .42f; default: return .40f; }
    }
    public void BeginAttack(CombatMove move)
    {
        reactionUntil = 0f;
        Play(Attacks[(int)move], true);
    }
    /// <summary>필살기 상태(SPECIAL_CHARGE / SPECIAL_RELEASE). SpecialMove 가 진행 단계에 맞춰 부른다.</summary>
    public void PlaySpecial(string state)
    {
        reactionUntil = 0f;
        Play(state, true);
    }
    // The defender's selected posture determines the guard; there are only two guards.
    public void GuardAgainst(CombatMove move) { }
    void OnDamaged(bool guarded)
    {
        flashUntil = Time.time + .12f; guardFlash = guarded;
        if (health.IsDead) { reactionUntil = 0f; Play("KO", true); return; }
        reactionUntil = Time.time + (guarded ? GuardReactionDuration : HurtDuration);
        string pose = fighter.IsCrouching ? "GUARD_LOWKICK" : "GUARD";
        Play(guarded ? pose + "_HIT" : "HURT", true, guarded ? .351f : 0f);
    }
    public void ResetPresentation()
    {
        reactionUntil = flashUntil = 0f; guardFlash = false; currentState = "IDLE";
        stateStarted = Time.time; groundWeight = groundShift = 0f; GroundShift = 0f;
        walk?.Reset();
        heroMovement?.Cancel(); movementRevision = -1;
        if (modelAnimator == null) return;
        modelAnimator.speed = 1f;
        modelAnimator.Play("IDLE", 0, 0f);
        modelAnimator.Update(0f);
    }
    void Play(string state, bool restart = false, float offsetSeconds = 0f, float blendOverride = -1f)
    {
        if (!restart && state == currentState) return;
        float blend = blendOverride >= 0f ? blendOverride
            : state == "HURT" || state.EndsWith("_HIT") ? .035f
            : state == "KO" ? .07f : state == "IDLE" || state.StartsWith("WALK_") ? .16f
            : state == QiPalmTiming.ChargeState ? .1f : .06f;
        // 기본자세에서 승인 걷기로 들어갈 때는 발이 튀지 않게 조금 더 길게 섞는다.
        if (currentState == "IDLE" && (state == "HERO_FORWARD_STEP" || state == "HERO_BACKWARD_STEP")) blend = .14f;
        currentState = state; stateStarted = Time.time;
        // Never freeze the animator: held guards have dedicated looping pose clips,
        // so fades can finish and a subsequent action cannot stick in a T-pose.
        modelAnimator.speed = 1f;
        modelAnimator.CrossFadeInFixedTime(state, blend, 0, offsetSeconds);
    }
    void Guard(bool low)
    {
        string pose = low ? "GUARD_LOWKICK" : "GUARD";
        if (currentState == pose + "_HOLD") return;
        if (currentState == pose)
        {
            if (Time.time - stateStarted >= GuardEntry) Play(pose + "_HOLD");
        }
        else if (currentState == pose + "_HIT") Play(pose + "_HOLD");
        // A fresh guard may return while its visual release is still blending.
        // Resume the held pose from the current pose; gameplay guard remains immediate.
        else if (currentState == pose + "_RELEASE" && Time.time >= stateStarted
            && Time.time - stateStarted < GuardRelease) Play(pose + "_HOLD", false, 0f, GuardSwitchBlend);
        // 막는 중에 높이만 바뀌면(상대 공격에 맞춘 자동 전환) 팔을 내렸다 다시 올리지 않고 그 자세로 바로 넘어간다.
        else if (currentState != null && currentState.StartsWith("GUARD") && !currentState.EndsWith("_RELEASE")) Play(pose + "_HOLD", false, 0f, GuardSwitchBlend);
        else Play(pose);
    }
    /// <summary>높은 방어 중에 걷고 있으면 방어 걷기를 튼다. 틀었으면 true.</summary>
    bool GuardStep()
    {
        if (fighter.IsCrouching || !SupportsGuardStep) return false;
        if (HeroStepping)
        {
            if (!heroMovement.IsMoving) return false;
            string step = heroMovement.StateName.Contains("BACKWARD") ? GuardStepBackward : GuardStepForward;
            Play(step, movementRevision != heroMovement.Revision, heroMovement.AnimationTime);
            movementRevision = heroMovement.Revision;
            return true;
        }
        if (walk == null || !walk.IsMoving) return false;
        Play(walk.Direction * fighter.Facing < 0 ? GuardWalkBack : GuardWalkForward);
        return true;
    }
    static bool GuardMoving(string state) => state == GuardWalkForward || state == GuardWalkBack || state == GuardStepForward || state == GuardStepBackward;
    static bool Groundable(string state) => state == "WALK_FORWARD" || state == "WALK_BACK"
        || state == "HERO_FORWARD_STEP" || state == "HERO_BACKWARD_STEP" || GuardMoving(state);
    /// <summary>
    /// 걷기 접지(겉모습 전용). 걷기·스텝 중에 두 발바닥 중 낮은 쪽이 바닥(2D 바닥 높이)에 닿도록 모델 전체를 위아래로 맞춘다.
    /// 적마다 옮겨 만든 클립의 작은 뜸·묻힘과, IK 걷기가 다 닿지 못한 발을 함께 잡는다. 최대 ±4cm.
    /// 기본자세로 돌아가면 0.1초, 공격·막기·피격·KO·필살기가 시작되면 0.05초 만에 마지막 값에서 풀어
    /// 그 동작의 높이(기본자세의 들썩임, 뛰기 포함)는 그대로 둔다. 판정·이동·속도는 건드리지 않는다.
    /// </summary>
    void GroundFeet(float dt)
    {
        GroundShift = 0f;
        if (walk == null || !walk.SoleReady) { groundWeight = 0f; return; }
        bool on = Groundable(currentState) && !health.IsDead && Time.time >= reactionUntil && !health.IsHitStunned
            && !fighter.IsAttacking && !fighter.IsSpecialActive && (!fighter.IsBlocking || GuardMoving(currentState));
        float fade = on ? .08f : currentState == "IDLE" ? .1f : .05f;
        groundWeight = Mathf.MoveTowards(groundWeight, on ? 1f : 0f, dt / fade);
        if (groundWeight <= 0f) { groundShift = 0f; return; }
        if (on)
        {
            float gap = Mathf.Min(walk.LowestSoleY(0), walk.LowestSoleY(1)) - walk.GroundY;
            float shift = gap < 0f ? -gap : gap > SoleBand ? SoleBand - gap : 0f;
            groundShift = Mathf.Clamp(shift, -MaxGroundShift, MaxGroundShift);
        }
        GroundShift = groundShift * groundWeight;
        visualRoot.position += Vector3.up * GroundShift;
    }
    void LateUpdate()
    {
        if (modelAnimator == null || visualRoot == null) return;
        visualRoot.localRotation = Quaternion.Euler(0, fighter.Facing * 90f, 0);
        visualRoot.localPosition = origin;
        walk?.Measure(Time.deltaTime, fighter.Facing);
        // Physics contact offsets can leave the collider a little above the floor.
        // Align only the presentation root; retain the intentional hop inside the clip.
        if (walk != null)
        {
            Vector3 grounded = visualRoot.position; grounded.y = walk.GroundY;
            visualRoot.position = grounded;
            // 발 치수(발바닥 표본)는 기본자세가 자리 잡은 프레임에 한 번 잰다. 모든 캐릭터의 기본자세는 발바닥이 바닥에 평평하다.
            if (!walk.Calibrated && currentState == "IDLE" && !modelAnimator.IsInTransition(0)) walk.TryCalibrate();
        }
        if (health.IsDead) Play("KO");
        else if (Time.time < reactionUntil || health.IsHitStunned) { }
        else if (fighter.IsSpecialActive) { }   // SpecialMove drives SPECIAL_* states through PlaySpecial
        else if (!fighter.IsAttacking)
        {
            if (fighter.IsBlocking && !fighter.InputLocked) { if (!GuardStep()) Guard(fighter.IsCrouching); }
            else if (currentState != null && currentState.StartsWith("GUARD"))
            {
                if (!currentState.EndsWith("_RELEASE"))
                    Play(currentState.StartsWith("GUARD_LOWKICK") ? "GUARD_LOWKICK_RELEASE" : "GUARD_RELEASE", false, .648f);
                else if (Time.time - stateStarted >= GuardRelease) Play("IDLE");
            }
            else if (heroMovement != null && heroMovement.isActiveAndEnabled)
            {
                Play(heroMovement.StateName, movementRevision != heroMovement.Revision, heroMovement.AnimationTime);
                movementRevision = heroMovement.Revision;
            }
            else if (!fighter.InputLocked && walk != null && walk.IsMoving)
                Play(walk.Direction * fighter.Facing < 0 ? "WALK_BACK" : "WALK_FORWARD");
            else Play("IDLE");
        }
        bool walkingState = currentState == "WALK_FORWARD" || currentState == "WALK_BACK" || currentState == GuardWalkForward || currentState == GuardWalkBack;
        // 승인 걷기(HERO_*_STEP)는 걷기 빠르기 배율만큼 빨리 재생한다. 그 밖의 상태는 1.
        bool heroStep = heroMovement != null && heroMovement.isActiveAndEnabled && (currentState == "HERO_FORWARD_STEP" || currentState == "HERO_BACKWARD_STEP"
            || currentState == GuardStepForward || currentState == GuardStepBackward);
        modelAnimator.speed = walkingState ? (authoredSteps && !cameraPlayer ? walk.AuthoredPlaybackSpeed : walk.PlaybackSpeed) : heroStep ? heroMovement.PlaybackSpeed : 1f;
        if ((!authoredSteps || cameraPlayer) && (heroMovement == null || !heroMovement.isActiveAndEnabled))
            walk?.Apply(Time.deltaTime, (walkingState || currentState == "IDLE") && !fighter.IsAttacking && (!fighter.IsBlocking || GuardMoving(currentState)) && !fighter.InputLocked && !health.IsDead && !health.IsHitStunned);
        GroundFeet(Time.deltaTime);
        Color tint = fighterTint;
        if (health.IsDead) tint *= .65f;
        else if (Time.time < flashUntil) tint = guardFlash ? new Color(.5f, .8f, 1f) : new Color(1f, .45f, .4f);
        properties.SetColor("_BaseColor", tint); properties.SetColor("_Color", tint);
        foreach (var renderer in renderers) if (renderer != null) renderer.SetPropertyBlock(properties);
    }
}
