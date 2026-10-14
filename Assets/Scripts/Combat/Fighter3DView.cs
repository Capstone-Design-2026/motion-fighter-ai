using UnityEngine;

/// <summary>Humanoid presentation on the existing single-plane combat simulation.</summary>
[DefaultExecutionOrder(100)]
public sealed class Fighter3DView : MonoBehaviour
{
    public Animator modelAnimator;
    public Transform visualRoot;
    public Color fighterTint = Color.white;
    public AnimationClip[] strikeClips;
    public AnimationClip[] guardClips;
    Fighter fighter;
    Health health;
    Rigidbody2D body;
    Animator flags;
    Renderer[] renderers;
    MaterialPropertyBlock properties;
    string currentState;
    bool scoutReactions;
    bool holdingScoutGuard;
    bool scoutWasAirborne;
    float scoutRiseSpeed;
    float scoutLandUntil;
    const float ScoutTakeoffPhase = 5f / 34f;
    const float ScoutLandingPhase = 22f / 34f;
    float flashUntil;
    bool guardFlash;
    Vector3 origin;
    float guardHitUntil;
    string guardState = "GUARD_JAB";
    public string CurrentState => currentState;

    /// <summary>Replace presentation only; the actor's AI, health and hitboxes stay in place.</summary>
    public bool ApplyVisual(GameObject prefab)
    {
        if (prefab == null) return false;
        var templateAnimator = prefab.GetComponentInChildren<Animator>(true);
        if (!prefab.activeSelf || templateAnimator == null || templateAnimator.avatar == null
            || !templateAnimator.avatar.isValid || !templateAnimator.avatar.isHuman)
        {
            Debug.LogError("[Fighter3DView] A valid humanoid visual prefab is required: " + prefab.name);
            return false;
        }
        var oldRoot = visualRoot;
        var controller = modelAnimator != null ? modelAnimator.runtimeAnimatorController : null;
        var replacement = Instantiate(prefab, transform, false);
        replacement.name = prefab.name;
        visualRoot = replacement.transform;
        visualRoot.localPosition = oldRoot != null ? oldRoot.localPosition : Vector3.zero;
        visualRoot.localRotation = oldRoot != null ? oldRoot.localRotation : Quaternion.identity;
        modelAnimator = replacement.GetComponentInChildren<Animator>(true);
        if (modelAnimator.runtimeAnimatorController == null) modelAnimator.runtimeAnimatorController = controller;
        modelAnimator.applyRootMotion = false;
        modelAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        renderers = replacement.GetComponentsInChildren<Renderer>();
        properties = new MaterialPropertyBlock();
        origin = visualRoot.localPosition;
        currentState = null;
        CacheReactionStyle();
        modelAnimator.Play("IDLE", 0, 0f);
        modelAnimator.Update(0f);
        if (oldRoot != null) { oldRoot.gameObject.SetActive(false); Destroy(oldRoot.gameObject); }
        return true;
    }

    void Awake()
    {
        fighter = GetComponent<Fighter>(); health = GetComponent<Health>();
        body = GetComponent<Rigidbody2D>(); flags = GetComponent<Animator>();
        renderers = visualRoot.GetComponentsInChildren<Renderer>();
        properties = new MaterialPropertyBlock(); origin = visualRoot.localPosition;
        modelAnimator.applyRootMotion = false;
        CacheReactionStyle();
    }
    void OnEnable() { health.OnDamaged += OnDamaged; }
    void OnDisable() { health.OnDamaged -= OnDamaged; }

    public static float Duration(CombatMove move)
    {
        switch(move) { case CombatMove.Jab: return .5f; case CombatMove.HighKick: return .8f;
            case CombatMove.LowKick: return .72f; default: return .62f; }
    }
    public static float Impact(CombatMove move)
    {
        return Duration(move) * (move == CombatMove.Jab ? .27f : move == CombatMove.HighKick ? .31f : move == CombatMove.LowKick ? .33f : .32f);
    }
    public void BeginAttack(CombatMove move)
    {
        string[] names = { "JAB", "CROSS", "HIGHKICK", "LOWKICK" };
        var clip = strikeClips[(int)move];
        if (modelAnimator.runtimeAnimatorController is AnimatorOverrideController overrides)
            clip = overrides[clip] ?? clip;
        float speed = clip.length / Duration(move);
        Play(names[(int)move], speed, true);
    }
    public void GuardAgainst(CombatMove move)
    {
        string[] names = { "GUARD_JAB", "GUARD_CROSS", "GUARD_HIGHKICK", "GUARD_LOWKICK" };
        guardState = names[(int)move]; guardHitUntil = Time.time + .2f;
    }
    // Only the Scout override opts into these short authored reaction clips.
    void CacheReactionStyle()
    {
        scoutReactions = false; holdingScoutGuard = false; scoutWasAirborne = false; scoutLandUntil = 0f;
        if (!(modelAnimator.runtimeAnimatorController is AnimatorOverrideController)) return;
        foreach (var clip in modelAnimator.runtimeAnimatorController.animationClips)
            if (clip.name == "BoxingV3_GUARD_JAB") { scoutReactions = true; break; }
    }
    void HoldScoutGuard(string state)
    {
        if (currentState != state || !holdingScoutGuard)
        {
            // Start at the recovered pose and let the non-looping clip hold its end.
            // Keeping animator speed nonzero allows the transition to finish.
            Play(state, 1f, true, .19f);
            holdingScoutGuard = true;
        }
    }
    void OnDamaged(bool guarded)
    {
        flashUntil = Time.time + .12f; guardFlash = guarded;
        if (scoutReactions && guarded) guardHitUntil = Time.time + .2f;
        if (!health.IsDead) Play(guarded ? guardState : "HURT", 1f, true);
    }
    void Play(string state, float speed = 1f, bool restart = false, float offsetSeconds = 0f)
    {
        if (!restart && state == currentState) return;
        currentState = state; modelAnimator.speed = speed; holdingScoutGuard = false;
        bool impactReaction = scoutReactions && restart && offsetSeconds == 0f && speed > 0f
            && (state == "HURT" || state.StartsWith("GUARD_"));
        bool locomotion = scoutReactions && (state == "IDLE" || state == "RUN" || state.StartsWith("WALK_"));
        float blendSeconds = state == "KO" ? .12f : impactReaction ? .025f : locomotion ? .12f : .06f;
        modelAnimator.CrossFadeInFixedTime(state, blendSeconds, 0, offsetSeconds);
    }
    // Phase follows physics, so the avatar never lands while its collider is still airborne.
    // The capture's ballistic translation is removed from the clip to avoid double jumping.
    void SampleScoutJump(float phase)
    {
        currentState = "JUMP"; holdingScoutGuard = false;
        modelAnimator.speed = 0f;
        modelAnimator.Play("JUMP", 0, Mathf.Clamp01(phase));
        modelAnimator.Update(0f);
    }
    void LateUpdate()
    {
        // Rotate the presentation only. Rigidbody2D and hit boxes stay on the combat plane.
        visualRoot.localRotation = Quaternion.Euler(0, fighter.Facing * 90f, 0);
        visualRoot.localPosition = origin;
        bool grounded = flags.GetBool("IsGrounded");
        bool capturedMovement = scoutReactions && modelAnimator.HasState(0, Animator.StringToHash("RUN"));
        if (capturedMovement)
        {
            if (!grounded)
            {
                if (!scoutWasAirborne) scoutRiseSpeed = Mathf.Max(.1f, body.linearVelocity.y);
                scoutRiseSpeed = Mathf.Max(scoutRiseSpeed, body.linearVelocity.y);
                scoutLandUntil = 0f;
            }
            else if (scoutWasAirborne && currentState == "JUMP") scoutLandUntil = Time.time + .20f;
            scoutWasAirborne = !grounded;
            if (health.IsDead || health.IsHitStunned || fighter.IsAttacking || fighter.IsBlocking)
                scoutLandUntil = 0f;
        }
        if (health.IsDead) Play("KO");
        else if (health.IsHitStunned) { if (!guardFlash) Play("HURT"); }
        else if (scoutReactions && guardFlash && Time.time < guardHitUntil)
        {
            // Finish the brief block recoil before returning to the held guard.
        }
        else if (!fighter.IsAttacking)
        {
            if (fighter.IsBlocking)
            {
                string pose = Time.time < guardHitUntil ? guardState : fighter.IsCrouching ? "GUARD_LOWKICK" : "GUARD_JAB";
                if (scoutReactions) HoldScoutGuard(pose);
                else Play(pose);
                // Keep the guard pose held rather than allowing the source clip to return to idle.
                if (!scoutReactions && modelAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime > .32f)
                    modelAnimator.speed = 0f;
            }
            else if (!grounded)
            {
                if (capturedMovement)
                    SampleScoutJump(Mathf.Lerp(ScoutTakeoffPhase, ScoutLandingPhase,
                        Mathf.InverseLerp(scoutRiseSpeed, -scoutRiseSpeed, body.linearVelocity.y)));
                else Play("JUMP");
            }
            else if (capturedMovement && Time.time < scoutLandUntil)
                SampleScoutJump(Mathf.Lerp(ScoutLandingPhase, 1f, 1f - (scoutLandUntil - Time.time) / .20f));
            else if (fighter.IsCrouching) Play("CROUCH");
            else if (Mathf.Abs(body.linearVelocity.x) > .12f)
            {
                float moveSpeed = Mathf.Abs(body.linearVelocity.x);
                bool backwards = body.linearVelocity.x * fighter.Facing < 0;
                // Only the Scout's private controller has RUN. Other fighters keep their presentation.
                string state = backwards ? "WALK_BACK" : "WALK_FORWARD";
                if (capturedMovement && !backwards && moveSpeed > (currentState == "RUN" ? 1.1f : 1.35f)) state = "RUN";
                Play(state);
                if (capturedMovement)
                    modelAnimator.speed = Mathf.Clamp(moveSpeed / (state == "RUN" ? 1.65f : backwards ? .65f : .62f), .65f, 2.2f);
            }
            else Play("IDLE");
        }
        Color tint = fighterTint;
        if (health.IsDead) tint *= .65f;
        else if (Time.time < flashUntil) tint = guardFlash ? new Color(.5f,.8f,1f) : new Color(1f,.45f,.4f);
        properties.SetColor("_BaseColor", tint); properties.SetColor("_Color", tint);
        foreach (var renderer in renderers) renderer.SetPropertyBlock(properties);
    }
}
