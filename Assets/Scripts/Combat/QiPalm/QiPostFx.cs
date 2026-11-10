using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 파운장 동안만 켜는 화면 효과: 블룸(기운이 번져 보이게), 약한 색수차, 푸른 비네팅.
/// 평소 화면은 후처리가 꺼져 있으므로, 필살기 중에만 카메라 후처리를 켜고 끝나면 원래대로 돌린다.
/// 블룸 임계값을 1 이상으로 두어 HDR 로 빛나는 이펙트만 번지고 배경은 거의 그대로다.
/// </summary>
public static class QiPostFx
{
    static Volume volume;
    static QiPostFxDriver driver;

    /// <summary>목표 세기(0~1). 0 이 되면 잠시 뒤 카메라 후처리도 원래 상태로 돌아간다.</summary>
    public static void Set(float weight)
    {
        Ensure();
        driver.target = Mathf.Clamp01(weight);
    }

    static void Ensure()
    {
        if (volume) return;
        var go = new GameObject("QiPalm PostFx");
        volume = go.AddComponent<Volume>();
        volume.isGlobal = true; volume.priority = 50; volume.weight = 0f;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        var bloom = profile.Add<Bloom>(true);
        bloom.intensity.Override(1.1f); bloom.threshold.Override(1.05f); bloom.scatter.Override(.72f); bloom.tint.Override(new Color(.82f, .94f, 1f));
        var ca = profile.Add<ChromaticAberration>(true); ca.intensity.Override(.16f);
        var vig = profile.Add<Vignette>(true); vig.intensity.Override(.26f); vig.smoothness.Override(.5f); vig.color.Override(new Color(.02f, .05f, .1f));
        volume.sharedProfile = profile;
        driver = go.AddComponent<QiPostFxDriver>();
        driver.volume = volume;
    }
}

/// <summary>볼륨 세기를 부드럽게 바꾸고, 필요할 때만 카메라 후처리를 켠다.</summary>
public sealed class QiPostFxDriver : MonoBehaviour
{
    public Volume volume;
    public float target;
    bool enabledByUs;
    UniversalAdditionalCameraData data;

    void Update()
    {
        if (!volume) return;
        volume.weight = Mathf.MoveTowards(volume.weight, target, Time.unscaledDeltaTime * 4f);
        var cam = Camera.main;
        if (cam != null && (data == null || data.gameObject != cam.gameObject)) data = cam.GetUniversalAdditionalCameraData();
        if (data == null) return;
        if (volume.weight > .001f && !data.renderPostProcessing) { data.renderPostProcessing = true; enabledByUs = true; }
        else if (volume.weight <= .001f && target <= 0f && enabledByUs) { data.renderPostProcessing = false; enabledByUs = false; }
    }
    void OnDestroy() { if (enabledByUs && data != null) data.renderPostProcessing = false; }
}
