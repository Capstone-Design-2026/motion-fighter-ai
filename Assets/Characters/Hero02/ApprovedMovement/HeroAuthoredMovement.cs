using UnityEngine;

/// <summary>Approved hero clips drive X travel through Rigidbody2D, retaining collisions and knockback.</summary>
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
    public float AnimationTime => IsMoving ? sequence.Age + Mathf.Max(0, Time.time-lastPhysicsTime) : 0;
    public float WorldScale => view != null && view.visualRoot != null ? Mathf.Abs(view.visualRoot.lossyScale.y) : 1;
    void Awake() {
        body = GetComponent<Rigidbody2D>(); fighter = GetComponent<Fighter>(); health = GetComponent<Health>(); view = GetComponent<Fighter3DView>();
        contacts=GetComponents<Collider2D>();originalMaterials=new PhysicsMaterial2D[contacts.Length];movementMaterials=new PhysicsMaterial2D[contacts.Length];
        for(int i=0;i<contacts.Length;i++) {
            originalMaterials[i]=contacts[i].sharedMaterial;
            movementMaterials[i]=new PhysicsMaterial2D("Hero authored travel") {friction=0,bounciness=originalMaterials[i]!=null?originalMaterials[i].bounciness:0};
        }
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
        if (fighter == null || health == null) return;
        string presentation = view != null ? view.CurrentState : null;
        bool allowed = fighter.CanAct && !fighter.IsAttacking && !fighter.IsBlocking && (presentation == null || (!presentation.StartsWith("GUARD") && presentation != "HURT" && presentation != "KO"));
        float delta = sequence.Advance(Time.fixedDeltaTime, fighter.Facing, allowed);
        // Floor friction must not shorten the authored stride. Restore contact friction
        // immediately for idle, guards and hit reactions. Pushbox runs after this velocity.
        ContactMaterial(allowed && (IsMoving || Mathf.Abs(delta)>.000001f));
        // Hit reactions own horizontal knockback; movement must never erase it.
        if (health.IsHitStunned && !health.IsDead && !fighter.InputLocked) return;
        body.linearVelocity = new Vector2(delta * WorldScale / Time.fixedDeltaTime, body.linearVelocity.y);
    }
}
