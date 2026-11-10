using UnityEngine;

/// <summary>
/// 파운장(破雲掌) 이펙트·사운드 묶음. Resources/QiPalm/QiPalmAssets 에 하나 두고 런타임에 불러온다.
/// 전투 씬마다 참조를 꽂지 않아도 되도록 Resources 로 둔다. 에셋은 QiPalmVfxBuilder(에디터)가 만든다.
/// </summary>
public sealed class QiPalmAssets : ScriptableObject
{
    [Header("이펙트 프리팹")]
    public GameObject charge;
    public GameObject wave;
    public GameObject impact;
    public GameObject muzzle;
    public GameObject ready;

    [Header("컷인")]
    public Texture2D brush;
    public Texture2D glow;

    [Header("사운드")]
    public AudioClip chargeStart;
    public AudioClip chargeLoop;
    public AudioClip release;
    public AudioClip impactClip;
    public AudioClip readyClip;
    public AudioClip fizzle;

    static QiPalmAssets cached;
    static bool warned;

    public static QiPalmAssets Load()
    {
        if (cached == null) cached = Resources.Load<QiPalmAssets>("QiPalm/QiPalmAssets");
        if (cached == null && !warned) { warned = true; Debug.LogWarning("[QiPalm] Resources/QiPalm/QiPalmAssets 가 없습니다. 이펙트 없이 진행합니다."); }
        return cached;
    }
}
