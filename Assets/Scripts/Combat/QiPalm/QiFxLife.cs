using UnityEngine;

/// <summary>한 번 터지고 사라지는 이펙트. 지정 시간 뒤 스스로 지우고, 붙은 조명은 짧게 번쩍인 뒤 꺼진다.</summary>
public sealed class QiFxLife : MonoBehaviour
{
    public float lifetime = 2f;
    public Light flash;
    public float flashIntensity = 10f;
    public float flashTime = .3f;
    float born;

    void OnEnable() { born = Time.time; if (flash) flash.intensity = flashIntensity; }
    void Update()
    {
        float age = Time.time - born;
        if (flash) flash.intensity = flashIntensity * Mathf.Clamp01(1f - age / Mathf.Max(.01f, flashTime));
        if (age >= lifetime) Destroy(gameObject);
    }

    /// <summary>프리팹을 위치·방향·크기로 생성한다. 파티클은 계층 크기를 따르도록 만들어 두었다.</summary>
    public static GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, float scale = 1f, float lightScale = 1f)
    {
        if (prefab == null) return null;
        var go = Instantiate(prefab, position, rotation);
        go.transform.localScale = Vector3.one * scale;
        var life = go.GetComponent<QiFxLife>();
        if (life != null) life.flashIntensity *= lightScale;
        return go;
    }
}
