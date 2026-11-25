using UnityEngine;

/// <summary>
/// Approved hero clips drive X travel through Rigidbody2D, retaining collisions and knockback.
/// 2026-11-12: 주인공 이동은 승인 걷기(스텝)로 통일했다. 방식은 RunConfig.heroMovement 로 고른다.
///   ApprovedWalk(기본): 대시 없이 승인 걷기만 반복. 뒤로는 같은 클립을 거꾸로 재생한다.
///   DashAndStep: 대시 → 이어 달리기 → 스텝, 뒤로는 백대시(처음 들어온 방식).
///   CommonWalk: 이 컴포넌트가 스스로 꺼지고 적과 같은 걷기(WALK_FORWARD / WALK_BACK)로 움직인다.
/// </summary>
[DisallowMultipleComponent, RequireComponent(typeof(PlayerController), typeof(Rigidbody2D))]
[DefaultExecutionOrder(-50)]
public sealed class HeroAuthoredMovement : MonoBehaviour
{
    readonly HeroMovementSequence sequence = new HeroMovementSequence();
    Rigidbody2D body;
    Fighter fighter;
    Health health;
    Fighter3DView view;
    Collider2D[] contacts;
    PhysicsMaterial2D[] originalMaterials, movementMaterials;
    bool usingMovementMaterial;
    float lastPhysicsTime;
    public HeroMovementSequence Sequence => sequence;
    public bool IsMoving => sequence.Current != HeroMovementSequence.Phase.Idle;
    public string StateName => sequence.StateName;
    public int Revision => sequence.Revision;
    public float AnimationTime => IsMoving ? sequence.Age + Mathf.Max(0, Time.time-lastPhysicsTime) * sequence.PlaybackSpeed : 0;
    /// <summary>걷기 빠르기 배율에 맞춘 애니메이션 재생 빠르기. Fighter3DView 가 Animator 에 건다.</summary>
    public float PlaybackSpeed => sequence.PlaybackSpeed;
    RunConfig config;
    public float WorldScale => view != null && view.visualRoot != null ? Mathf.Abs(view.visualRoot.lossyScale.y) : 1;
    void Awake() {
        body = GetComponent<Rigidbody2D>(); fighter = GetComponent<Fighter>(); health = GetComponent<Health>(); view = GetComponent<Fighter3DView>();
        contacts=GetComponents<Collider2D>();originalMaterials=new PhysicsMaterial2D[contacts.Length];movementMaterials=new PhysicsMaterial2D[contacts.Length];
        for(int i=0;i<contacts.Length;i++) {
            originalMaterials[i]=contacts[i].sharedMaterial;
            movementMaterials[i]=new PhysicsMaterial2D("Hero authored travel") {friction=0,bounciness=originalMaterials[i]!=null?originalMaterials[i].bounciness:0};
        }
    }
    void OnEnable()
    {
        var run = RunState.Instance;
        var style = run != null && run.Config != null ? run.Config.heroMovement : HeroMovementStyle.CommonWalk;
        if (style == HeroMovementStyle.CommonWalk) { enabled = false; return; }
        sequence.WalkOnly = style == HeroMovementStyle.ApprovedWalk;
        config = run != null ? run.Config : null;
        sequence.WalkSpeed = config != null ? config.heroWalkSpeed : 1f;
    }
    public void SetInput(float worldAxis) { sequence.SetInput(worldAxis); }
    public void Cancel() { sequence.Cancel(); }
    void ContactMaterial(bool moving) {
        if(contacts==null || usingMovementMaterial==moving)return;
        usingMovementMaterial=moving;
        for(int i=0;i<contacts.Length;i++)if(contacts[i]!=null&&!contacts[i].isTrigger)contacts[i].sharedMaterial=moving?movementMaterials[i]:originalMaterials[i];
    }
    void OnDisable() { Cancel(); ContactMaterial(false); if (body != null && health != null && !health.IsHitStunned) body.linearVelocity = new Vector2(0, body.linearVelocity.y); }
    void OnDestroy() { if(movementMaterials!=null)foreach(var material in movementMaterials)if(material!=null)Destroy(material); }
    void FixedUpdate()
    {
        lastPhysicsTime = Time.time;
        if (config != null) sequence.WalkSpeed = config.heroWalkSpeed;   // Play 중에 RunConfig 에서 바꿔도 바로 반영
        if (fighter == null || health == null) return;
        string presentation = view != null ? view.CurrentState : null;
        // 높은 방어 중에는 방어 걷기(GUARD_STEP_*)로 움직일 수 있다. 낮은 방어·피격·KO 중에는 멈춘다.
        bool guardMove = fighter.IsBlocking && !fighter.IsCrouching && view != null && view.SupportsGuardStep;
        bool allowed = fighter.CanAct && !fighter.IsAttacking && (!fighter.IsBlocking || guardMove)
            && (presentation == null || (presentation != "HURT" && presentation != "KO" && (guardMove || !presentation.StartsWith("GUARD"))));
        float delta = sequence.Advance(Time.fixedDeltaTime, fighter.Facing, allowed);
        // Floor friction must not shorten the authored stride. Restore contact friction
        // immediately for idle, guards and hit reactions. Pushbox runs after this velocity.
        ContactMaterial(allowed && (IsMoving || Mathf.Abs(delta)>.000001f));
        // Hit reactions own horizontal knockback; movement must never erase it.
        if (health.IsHitStunned && !health.IsDead && !fighter.InputLocked) return;
        body.linearVelocity = new Vector2(delta * WorldScale / Time.fixedDeltaTime, body.linearVelocity.y);
    }
}
