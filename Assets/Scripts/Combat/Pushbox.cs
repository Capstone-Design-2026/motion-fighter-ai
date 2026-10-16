using UnityEngine;

/// <summary>
/// 격투게임식 밀어내기(pushbox).
///
/// 두 파이터는 서로 물리적으로 충돌하지 않는다(Physics2D.IgnoreCollision).
/// 대신 이 스크립트가 매 FixedUpdate 마다 겹침을 검사해서 좌우로만 밀어낸다.
/// 그래서 상대 머리 위에 올라타거나, 착지하면서 상대를 밀치는 일이 없다.
///
/// blockCrossing 이 켜져 있으면 공중에서도 밀어내기가 유지되므로
/// 점프로 상대를 넘어가 반대편에 설 수 없다. 좌우 위치가 절대 바뀌지 않는다.
///
/// Player 와 Enemy 양쪽에 모두 붙인다.
/// </summary>
[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(Rigidbody2D))]
public class Pushbox : MonoBehaviour
{
    [Header("상대")]
    [Tooltip("비워두면 씬에서 다른 Pushbox 를 자동으로 찾는다.")]
    [SerializeField] private Pushbox opponent;

    [Header("넘어가기 방지")]
    [Tooltip("켜면 점프해도 상대를 넘어갈 수 없다. 좌우 위치가 절대 바뀌지 않는다.\n" +
             "끄면 상대 위를 지나 반대편으로 갈 수 있다(격투게임 표준).")]
    [SerializeField] private bool blockCrossing = true;

    [Tooltip("두 캐릭터가 최소한 이만큼은 떨어져 있게 한다. 0 이면 콜라이더가 딱 닿는 선까지.")]
    [SerializeField] private float minSeparation = 0f;

    [Header("밀어내기")]
    [Tooltip("초당 최대 밀어내는 거리. 너무 크면 튕기듯 보인다.")]
    [SerializeField] private float pushSpeed = 6f;

    [Tooltip("blockCrossing 이 꺼져 있을 때만 쓰인다. 세로로 이만큼 겹쳐야 밀어낸다.")]
    [SerializeField] private float verticalSlack = 0.25f;

    private Collider2D myCollider;
    private Rigidbody2D rb;
    private Health health;

    /// <summary>내가 상대의 어느 쪽에 있는지. +1 = 오른쪽, -1 = 왼쪽.</summary>
    private float side = 1f;

    // BattleSceneBridge가 Start 전에 교체된 상대를 연결한다.
    public void SetOpponent(Pushbox value) { opponent = value; }

    void Awake()
    {
        myCollider = GetComponent<Collider2D>();
        rb = GetComponent<Rigidbody2D>();
        health = GetComponent<Health>();
    }

    void Start()
    {
        if (opponent == null)
        {
            foreach (var p in FindObjectsByType<Pushbox>())
            {
                if (p != this) { opponent = p; break; }
            }
        }

        if (opponent == null) return;

        if (opponent.myCollider == null)
            opponent.myCollider = opponent.GetComponent<Collider2D>();

        // 서로 물리적으로 부딪히지 않게 한다. 바닥과의 충돌은 그대로 유지된다.
        if (myCollider != null && opponent.myCollider != null)
            Physics2D.IgnoreCollision(myCollider, opponent.myCollider, true);

        side = transform.position.x >= opponent.transform.position.x ? 1f : -1f;
    }

    void FixedUpdate()
    {
        if (opponent == null || myCollider == null || rb == null) return;
        if (health != null && health.IsDead) return;
        if (opponent.myCollider == null) return;

        Bounds a = myCollider.bounds;
        Bounds b = opponent.myCollider.bounds;

        // 내가 어느 쪽에 있는지 갱신. 겹쳐 있는 순간에는 직전 값을 유지한다.
        float dx = a.center.x - b.center.x;
        if (Mathf.Abs(dx) > 0.01f) side = Mathf.Sign(dx);

        float overlapX = Mathf.Min(a.max.x, b.max.x) - Mathf.Max(a.min.x, b.min.x) + minSeparation;
        if (overlapX <= 0f) return;

        if (!blockCrossing)
        {
            // 넘어가기를 허용하는 모드에서는 세로로 충분히 겹칠 때만 밀어낸다.
            // (상대 위를 지나가는 중이면 간섭하지 않는다)
            float overlapY = Mathf.Min(a.max.y, b.max.y) - Mathf.Max(a.min.y, b.min.y);
            if (overlapY <= verticalSlack) return;
        }

        // 양쪽 모두 이 스크립트를 갖고 있으므로 각자 절반씩만 물러난다.
        float step = Mathf.Min(overlapX * 0.5f, pushSpeed * Time.fixedDeltaTime);
        rb.position += new Vector2(side * step, 0f);

        // 상대 쪽으로 밀고 들어오는 속도를 없앤다.
        // 이게 없으면 이동 입력과 밀어내기가 매 프레임 싸워서 캐릭터가 떨린다.
        if (blockCrossing)
        {
            Vector2 v = rb.linearVelocity;
            if (side > 0f && v.x < 0f) v.x = 0f;
            else if (side < 0f && v.x > 0f) v.x = 0f;
            rb.linearVelocity = v;
        }
    }
}
