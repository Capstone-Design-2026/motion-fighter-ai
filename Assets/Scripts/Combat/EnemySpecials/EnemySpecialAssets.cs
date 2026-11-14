using UnityEngine;

/// <summary>
/// 적 필살기 이펙트 재료 묶음. Resources/EnemySpecials/EnemySpecialAssets 에 하나 두고 런타임에 불러온다.
/// 텍스처·재질은 에디터 도구(EnemySpecialPreview.BuildGameAssets)가 구워서 채운다. 전투 씬마다 참조를 꽂지 않아도 된다.
/// </summary>
public sealed class EnemySpecialAssets : ScriptableObject
{
    [Header("셰이더")]
    public Shader ribbon;
    public Shader smoke;
    public Shader orb;

    [Header("이펙트 텍스처")]
    public Texture2D ribbonTex;
    public Texture2D brushTex;
    public Texture2D noiseTex;
    public Texture2D smokeTex;
    public Texture2D talismanTex;
    public Texture2D shockTex;
    public Texture2D crackTex;
    public Texture2D bellTex;
    public Texture2D glowTex;

    [Header("소품 재질")]
    public Material wood;
    public Material bronze;
    public Material iron;
    public Material rope;

    [Header("소품")]
    public GameObject rock;

    static EnemySpecialAssets cached;
    static bool warned;

    public bool Complete => ribbon != null && smoke != null && orb != null && ribbonTex != null && brushTex != null && noiseTex != null && smokeTex != null
        && talismanTex != null && shockTex != null && crackTex != null && bellTex != null && glowTex != null && wood != null && bronze != null && iron != null && rope != null && rock != null;

    public static EnemySpecialAssets Load()
    {
        if (cached == null) cached = Resources.Load<EnemySpecialAssets>("EnemySpecials/EnemySpecialAssets");
        if ((cached == null || !cached.Complete) && !warned)
        {
            warned = true;
            Debug.LogWarning("[EnemySpecial] Resources/EnemySpecials/EnemySpecialAssets 가 없거나 비어 있습니다. 적 필살기를 끕니다.");
        }
        return cached != null && cached.Complete ? cached : null;
    }
}
