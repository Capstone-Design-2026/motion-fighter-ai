using UnityEngine;

/// <summary>One unscaled, non-spatial voice per presenter. Envelope follows the audible clip.</summary>
public class StoryVoice : MonoBehaviour
{
    AudioSource source;
    StoryDialogueLine line;
    float level;
    public bool IsPlaying => source != null && source.isPlaying;
    public float Level => IsPlaying ? level : 0f;
    public void Play(StoryDialogueLine value)
    {
        Stop();
        line = value;
        if (value == null || value.voice == null) return;
        if (source == null)
        {
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0;
            source.loop = false;
            source.ignoreListenerPause = true;
            source.volume = .9f;
        }
        source.clip = value.voice;
        source.Play();
    }
    void Update()
    {
        float target = 0;
        if (IsPlaying && line != null && line.voiceEnvelope != null && line.voiceEnvelope.Length > 0)
        {
            float cursor = source.time * 30f;
            int a = Mathf.Clamp((int)cursor, 0, line.voiceEnvelope.Length - 1);
            int b = Mathf.Min(a + 1, line.voiceEnvelope.Length - 1);
            target = Mathf.Lerp(line.voiceEnvelope[a], line.voiceEnvelope[b], cursor - (int)cursor);
        }
        level = Mathf.MoveTowards(level, target, Time.unscaledDeltaTime * (target > level ? 16 : 10));
    }
    public void Stop()
    {
        if (source != null) { source.Stop(); source.clip = null; }
        line = null; level = 0;
    }
    void OnDisable() { Stop(); }
    void OnDestroy() { Stop(); }
}
