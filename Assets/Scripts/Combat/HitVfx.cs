using UnityEngine;

/// <summary>
/// 타격 이펙트. 공격이 실제로 대미지를 입힌 순간 맞은 지점에 파티클 프리팹을 하나 띄운다.
///
/// 씬에 이 컴포넌트가 있고 프리팹이 꽂혀 있을 때만 동작한다. 없으면 조용히 아무 일도 하지 않는다.
/// (HitImpact 와 달리 프리팹 참조가 필요하므로 자동 생성하지 않는다)
///
/// 파티클은 기본적으로 sortingOrder 0 에 그려진다. 캐릭터가 1 이라 그대로 두면
/// 이펙트가 캐릭터 뒤에 깔려 거의 안 보인다. 그래서 띄울 때마다 자식 렌더러의
/// sortingOrder 를 끌어올린다.
///
/// 프리팹의 stopAction 이 None 이라 저절로 사라지지 않는다. lifetime 이 지나면 직접 지운다.
/// </summary>
[DefaultExecutionOrder(-400)]
[DisallowMultipleComponent]
public class HitVfx : MonoBehaviour
{
    [Header("전체 On/Off")]
    [SerializeField] private bool enableVfx = true;

    [Header("프리팹")]
    [Tooltip("직격했을 때 띄울 이펙트.")]
    [SerializeField] private GameObject hitPrefab;
    [Tooltip("가드로 막혔을 때 띄울 이펙트. 비워두면 직격과 같은 것을 조금 작게 띄운다.")]
    [SerializeField] private GameObject guardPrefab;
    [Tooltip("가드 타격에는 이펙트를 넣지 않으려면 끈다.")]
    [SerializeField] private bool playOnGuard = true;

    [Header("크기")]
    [SerializeField] private float scale = 1f;
    [Tooltip("가드 타격일 때 위 크기에 곱할 배율.")]
    [Range(0.1f, 1f)]
    [SerializeField] private float guardScaleMultiplier = 0.6f;

    [Header("그리는 순서 (캐릭터보다 높아야 앞에 보인다)")]
    [SerializeField] private int sortingOrder = 30;
    [Tooltip("비워두면 프리팹에 설정된 레이어를 그대로 쓴다.")]
    [SerializeField] private string sortingLayerName = "";

    [Header("정리")]
    [Tooltip("이 시간(초)이 지나면 지운다. 파티클이 1초짜리라 넉넉하게 잡아 둔다.")]
    [SerializeField] private float lifetime = 2f;

    private static HitVfx instance;

    private GameObject preparedHit, preparedGuard;

    private bool warnedMissingMaterial;   // 경고는 한 번만 띄운다

    /// <summary>타격 이펙트 재생. 씬에 HitVfx 가 없으면 아무 일도 일어나지 않는다.</summary>
    /// <param name="point">맞은 지점(월드 좌표).</param>
    /// <param name="guarded">가드에 막힌 타격이면 true.</param>
    public static void Play(Vector3 point, bool guarded)
    {
        if (instance != null) instance.Spawn(point, guarded);
    }

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);
            return;
        }
        instance = this;
    }

    void Start()
    {
        if (!RedboolEffectPreparation.Enabled || !enableVfx) return;
        RedboolEffectPreparation.Measure("hit-prefabs", () => {
            preparedHit = PrepareTemplate(hitPrefab);
            preparedGuard = guardPrefab == hitPrefab ? preparedHit : PrepareTemplate(guardPrefab);
        });
    }

    GameObject PrepareTemplate(GameObject prefab)
    {
        if (prefab == null) return null;
        // Inactive parent prevents play-on-awake particles/audio during preparation.
        var holder = new GameObject("Prepared hit VFX"); holder.SetActive(false);
        holder.transform.SetParent(transform, false);
        var copy = Instantiate(prefab, holder.transform, false);
        PrepareRenderers(copy);
        return copy;
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    public static GameObject WarmVisual(Vector3 point, bool guarded) => instance != null ? instance.Spawn(point, guarded) : null;

    private GameObject Spawn(Vector3 point, bool guarded)
    {
        if (!enableVfx) return null;
        if (guarded && !playOnGuard) return null;

        GameObject prefab = guarded && guardPrefab != null ? guardPrefab : hitPrefab;
        if (prefab == null) return null;

        // Preserve the animated contact depth for rotating 3D cameras.
        var template = guarded && guardPrefab != null ? preparedGuard : preparedHit;
        var go = Instantiate(template != null ? template : prefab, point, prefab.transform.rotation);

        float s = scale;
        // 가드 전용 프리팹을 따로 꽂았다면 그 프리팹이 이미 약한 연출이므로 더 줄이지 않는다.
        if (guarded && guardPrefab == null) s *= guardScaleMultiplier;
        go.transform.localScale = prefab.transform.localScale * s;

        if (template == null) PrepareRenderers(go);

        if (lifetime > 0f) Destroy(go, lifetime);
        return go;
    }

    /// <summary>
    /// 띄우기 전에 프리팹 안의 렌더러를 손본다.
    ///   - 캐릭터(sortingOrder 1) 앞으로 끌어올린다
    ///   - 머티리얼이 비어 있는 렌더러는 꺼 버린다
    ///
    /// 머티리얼이 null 인 파티클은 Unity 가 마젠타 에러 머티리얼로 그린다. 그래서 타격할 때마다
    /// 분홍 사각형이 잠깐 튄다. Basic Hit 8 (NEW) 의 루트 파티클이 딱 이 상태다.
    /// (같은 팩의 다른 프리팹에는 없는, 이 프리팹만의 결함이다)
    /// 프리팹을 직접 고치지 않고 여기서 끄는 이유는 에셋을 다시 임포트해도 고침이 유지되고,
    /// 나중에 다른 프리팹으로 갈아끼워도 같은 사고를 막아 주기 때문이다.
    /// </summary>
    private void PrepareRenderers(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];

            if (r.sharedMaterial == null)
            {
                r.enabled = false;

                if (!warnedMissingMaterial)
                {
                    warnedMissingMaterial = true;
                    Debug.LogWarning($"[HitVfx] '{r.name}' 에 머티리얼이 없어 렌더러를 껐습니다. " +
                                     "그대로 두면 분홍색 사각형으로 그려집니다.");
                }
                continue;
            }

            r.sortingOrder = sortingOrder;
            if (!string.IsNullOrEmpty(sortingLayerName))
                r.sortingLayerName = sortingLayerName;
        }
    }
}
