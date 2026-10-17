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
    public const float HurtDuration = .70f;
    public const float GuardReactionDuration = .225f;
    const float GuardEntry = .27f, GuardRelease = .252f;
    static readonly string[] Attacks = { "JAB", "CROSS", "HIGHKICK", "LOWKICK" };
    Fighter fighter;
    Health health;
    Renderer[] renderers;
    MaterialPropertyBlock properties;
    Vector3 origin;
    string currentState;
    float stateStarted, reactionUntil, flashUntil;
    bool guardFlash;
    GroundedBoxingWalk walk;
    public GroundedBoxingWalk Walk => walk;
    public string CurrentState => currentState;

    void CacheVisual()
    {
        renderers = visualRoot.GetComponentsInChildren<Renderer>();
        properties = new MaterialPropertyBlock();
        origin = visualRoot.localPosition;
        modelAnimator.applyRootMotion = false;
        modelAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        walk = new GroundedBoxingWalk(visualRoot, modelAnimator);
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
        switch (move) { case CombatMove.Jab: return .20f; case CombatMove.HighKick: return .48f;
            case CombatMove.LowKick: return .42f; default: return .40f; }
    }
    public void BeginAttack(CombatMove move)
    {
        reactionUntil = 0f;
        Play(Attacks[(int)move], true);
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
        stateStarted = Time.time;
        walk?.Reset();
        if (modelAnimator == null) return;
        modelAnimator.speed = 1f;
        modelAnimator.Play("IDLE", 0, 0f);
        modelAnimator.Update(0f);
    }
    void Play(string state, bool restart = false, float offsetSeconds = 0f)
    {
        if (!restart && state == currentState) return;
        float blend = state == "HURT" || state.EndsWith("_HIT") ? .035f
            : state == "KO" ? .07f : state == "IDLE" || state.StartsWith("WALK_") ? .10f : .06f;
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
        else Play(pose);
    }
    void LateUpdate()
    {
        if (modelAnimator == null || visualRoot == null) return;
        visualRoot.localRotation = Quaternion.Euler(0, fighter.Facing * 90f, 0);
        visualRoot.localPosition = origin;
        walk?.Measure(Time.deltaTime, fighter.Facing);
        if (health.IsDead) Play("KO");
        else if (Time.time < reactionUntil || health.IsHitStunned) { }
        else if (!fighter.IsAttacking)
        {
            if (fighter.IsBlocking && !fighter.InputLocked) Guard(fighter.IsCrouching);
            else if (currentState != null && currentState.StartsWith("GUARD"))
            {
                if (!currentState.EndsWith("_RELEASE"))
                    Play(currentState.StartsWith("GUARD_LOWKICK") ? "GUARD_LOWKICK_RELEASE" : "GUARD_RELEASE", false, .648f);
                else if (Time.time - stateStarted >= GuardRelease) Play("IDLE");
            }
            else if (!fighter.InputLocked && walk != null && walk.IsMoving)
                Play(walk.Direction * fighter.Facing < 0 ? "WALK_BACK" : "WALK_FORWARD");
            else Play("IDLE");
        }
        bool walkingState = currentState == "WALK_FORWARD" || currentState == "WALK_BACK";
        modelAnimator.speed = walkingState ? Mathf.Clamp((currentState == "WALK_BACK" ? 1.30f : 1.20f) * walk.Speed / Mathf.Max(.1f, 2 * walk.StepLength), .7f, 3.5f) : 1f;
        walk?.Apply(Time.deltaTime, (walkingState || currentState == "IDLE") && !fighter.IsAttacking && !fighter.IsBlocking && !fighter.InputLocked && !health.IsDead && !health.IsHitStunned);
        Color tint = fighterTint;
        if (health.IsDead) tint *= .65f;
        else if (Time.time < flashUntil) tint = guardFlash ? new Color(.5f, .8f, 1f) : new Color(1f, .45f, .4f);
        properties.SetColor("_BaseColor", tint); properties.SetColor("_Color", tint);
        foreach (var renderer in renderers) if (renderer != null) renderer.SetPropertyBlock(properties);
    }
}
