using UnityEngine;

/// <summary>
/// 기 모으기 이펙트. 두 손 사이의 기운 구슬, 빨려 드는 빛 입자와 먹구름, 손을 감는 소용돌이 리본,
/// 발밑의 빛 고리와 피어오르는 기운. 모은 정도(0~1)에 따라 구슬·조명·발밑 고리가 커진다.
/// </summary>
public sealed class QiChargeFx : MonoBehaviour
{
    public Transform orb;
    public Light glow;
    public Transform ground;
    public ParticleSystem[] emitters;
    public float orbMin = .09f, orbMax = .2f;
    bool ending;

    /// <summary>구슬은 두 손 사이, 발밑 고리는 바닥에 둔다.</summary>
    public void Follow(Vector3 hands, Vector3 feet)
    {
        transform.position = hands;
        if (ground) { ground.position = feet + Vector3.up * .025f; ground.rotation = Quaternion.identity; }
    }

    public void SetLevel(float level)
    {
        level = Mathf.Clamp01(level);
        float pulse = 1f + .09f * Mathf.Sin(Time.unscaledTime * 17f);
        if (orb) orb.localScale = Vector3.one * Mathf.Lerp(orbMin, orbMax, level) * pulse;
        if (glow) glow.intensity = Mathf.Lerp(.5f, 1.6f, level) * pulse;
        if (ground) ground.localScale = Vector3.one * Mathf.Lerp(.75f, 1.3f, level);
    }

    /// <summary>발사(정상 종료) 또는 끊김(fizzle). 입자는 남은 것만 흩어지게 두고 잠시 뒤 지운다.</summary>
    public void End()
    {
        if (ending) return;
        ending = true;
        if (emitters != null) foreach (var ps in emitters) if (ps) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        if (orb) orb.gameObject.SetActive(false);
        if (glow) glow.enabled = false;
        if (ground) ground.gameObject.SetActive(false);
        Destroy(gameObject, 1.2f);
    }
}
