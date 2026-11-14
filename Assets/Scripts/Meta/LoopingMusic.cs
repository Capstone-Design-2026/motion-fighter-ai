using UnityEngine;

/// <summary>
/// 한 씬 동안 반복 재생하는 배경음악. 처음 fadeIn 초 동안 서서히 커진다.
/// 씬이 바뀌면 오브젝트와 함께 사라지므로 따로 멈출 필요가 없다.
/// </summary>
public class LoopingMusic : MonoBehaviour
{
    private AudioSource source;
    private float targetVolume, fadeIn;

    /// <summary>host 에 붙여 clip 을 반복 재생한다. clip 이 없으면 아무것도 하지 않는다.</summary>
    public static LoopingMusic Play(GameObject host, AudioClip clip, float volume = 0.4f, float fadeIn = 2f)
    {
        if (host == null || clip == null) return null;
        var music = host.AddComponent<LoopingMusic>();
        music.targetVolume = Mathf.Clamp01(volume);
        music.fadeIn = Mathf.Max(0f, fadeIn);
        music.source = host.AddComponent<AudioSource>();
        music.source.clip = clip;
        music.source.loop = true;
        music.source.playOnAwake = false;
        music.source.spatialBlend = 0f;
        music.source.volume = music.fadeIn > 0f ? 0f : music.targetVolume;
        music.source.Play();
        return music;
    }

    void Update()
    {
        if (source == null || source.volume >= targetVolume) return;
        source.volume = fadeIn > 0f
            ? Mathf.MoveTowards(source.volume, targetVolume, targetVolume / fadeIn * Time.unscaledDeltaTime)
            : targetVolume;
    }
}
