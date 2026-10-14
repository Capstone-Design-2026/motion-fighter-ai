using UnityEngine;
using UnityEngine.UI;

/// <summary>Separate, non-interactive water layer aligned to the original painting.</summary>
[RequireComponent(typeof(RawImage))]
public sealed class StoryMapWater : MonoBehaviour
{
    Material ownedMaterial;
    RawImage image;
    public static void Create(RectTransform artwork, Texture texture)
    {
        var layer = UIFactory.CreateRect("LivingWater", artwork);
        UIFactory.Stretch(layer);
        var image = layer.gameObject.AddComponent<RawImage>();
        image.texture = texture; image.raycastTarget = false;
        layer.gameObject.AddComponent<StoryMapWater>();
    }
    void OnEnable()
    {
        image = GetComponent<RawImage>();
        var shader = Resources.Load<Shader>("UI/Wuxia/LivingMapWater");
        if (shader == null) { image.enabled = false; return; }
        ownedMaterial = new Material(shader); image.material = ownedMaterial;
    }
    void Update()
    {
        if (ownedMaterial != null) image.materialForRendering.SetFloat("_FlowTime", Time.unscaledTime);
    }
    void OnDisable()
    {
        if (image != null) image.material = null;
        if (ownedMaterial != null) { Destroy(ownedMaterial); ownedMaterial = null; }
    }
}
