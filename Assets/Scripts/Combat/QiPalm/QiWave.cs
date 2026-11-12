using System;
using UnityEngine;

/// <summary>
/// 파운장의 기파. 전투 평면(X축)을 따라 날아가다 상대 몸에 닿으면 터진다.
///   정타: 큰 피해 + 넉백 + 추가 경직.
///   방어: 방어 중이고 공격자를 바라보고 있으면 "기 관통" — 피해 일부만 들어가고 방어가 풀리며 약하게 밀려난다(뜨지 않는다).
///   빗나감: 사거리 끝에서 흩어진다.
/// 판정은 Fighter 의 일반 공격과 같은 Health.TakeDamage 를 쓴다(가드 반응·게이지·HUD 가 그대로 이어진다).
/// </summary>
public sealed class QiWave : MonoBehaviour
{
    public float speed = 12f;
    public float range = 6.5f;
    [Tooltip("기파 앞쪽 반지름. 상대 콜라이더 가장자리와 이 거리가 겹치면 맞는다.")]
    public float hitRadius = .3f;
    public Transform body;
    public Light glow;
    public ParticleSystem[] detachOnEnd;
    public TrailRenderer[] trails;

    Health ownerHealth;
    Transform ownerRoot;
    int dir, damage, chip;
    Vector2 knockback;
    float travelled, extraStun, guardPush;
    bool finished;
    Action<QiWave, Health, bool, Vector3> onHit;
    Action<QiWave, Vector3> onExpire;

    public bool Finished => finished;

    public void Launch(Health owner, int direction, int hitDamage, int guardDamage, Vector2 hitKnockback, float guardKnockback, float hitExtraStun,
        Action<QiWave, Health, bool, Vector3> hit, Action<QiWave, Vector3> expire)
    {
        ownerHealth = owner; ownerRoot = owner ? owner.transform : null;
        dir = direction >= 0 ? 1 : -1; damage = hitDamage; chip = guardDamage;
        knockback = hitKnockback; guardPush = guardKnockback; extraStun = hitExtraStun; onHit = hit; onExpire = expire;
        transform.rotation = Quaternion.LookRotation(dir > 0 ? Vector3.right : Vector3.left, Vector3.up);
    }

    void Update()
    {
        if (finished) return;
        float step = speed * Time.deltaTime;
        travelled += step;
        transform.position += Vector3.right * (dir * step);
        float front = transform.position.x + dir * hitRadius;

        foreach (var target in FindObjectsByType<Health>(FindObjectsSortMode.None))
        {
            if (target == ownerHealth || target.IsDead) continue;
            var col = target.GetComponent<Collider2D>();
            float cx = col ? col.bounds.center.x : target.transform.position.x;
            float half = col ? col.bounds.extents.x : .25f;
            // 출발점보다 뒤에 있는 상대는 무시한다(등 뒤로는 나가지 않는다).
            if (ownerRoot && (cx - ownerRoot.position.x) * dir < 0f) continue;
            bool reached = dir > 0 ? front >= cx - half : front <= cx + half;
            if (reached) { Hit(target); return; }
        }
        if (travelled >= range) Expire();
    }

    void Hit(Health target)
    {
        var fighter = target.GetComponent<Fighter>();
        // 방어가 성립하려면 시간제 방어 중이고 기파가 오는 쪽을 보고 있어야 한다(등 뒤는 뚫린다).
        bool facing = fighter != null && (transform.position.x - target.transform.position.x) * fighter.Facing > 0f;
        bool guarded = fighter != null && fighter.IsGuarding && facing;
        int amount = guarded ? chip : damage;
        // 정타는 크게 날린다. 막았을 때는 뜨지 않고 가로로만 살짝 밀린다.
        Vector2 kb = guarded ? new Vector2(guardPush * dir, 0f) : new Vector2(knockback.x * dir, knockback.y);
        Vector3 point = new Vector3(Mathf.Lerp(transform.position.x, target.transform.position.x, .35f), transform.position.y, transform.position.z);
        bool damaged = target.TakeDamage(amount, kb, guarded, true);
        if (damaged && !guarded && !target.IsDead) target.ExtendStun(extraStun);
        Finish();
        onHit?.Invoke(this, target, guarded, point);
    }

    void Expire()
    {
        Finish();
        onExpire?.Invoke(this, transform.position);
    }

    /// <summary>몸체는 바로 감추고, 꼬리·흩어지는 입자는 자연스럽게 사라지도록 떼어 둔다.</summary>
    void Finish()
    {
        if (finished) return;
        finished = true;
        if (body) body.gameObject.SetActive(false);
        if (glow) glow.enabled = false;
        if (detachOnEnd != null)
            foreach (var ps in detachOnEnd)
            {
                if (!ps) continue;
                ps.transform.SetParent(null, true);
                ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                Destroy(ps.gameObject, 2f);
            }
        if (trails != null) foreach (var t in trails) if (t) t.emitting = false;
        Destroy(gameObject, .6f);
    }
}
