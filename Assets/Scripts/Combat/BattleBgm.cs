using UnityEngine;

/// <summary>
/// 전투 씬 BGM. clips 중 하나를 무작위로 골라 재생하고, 곡이 끝나면 다음 곡을 무작위로 이어서 재생한다.
/// (같은 곡이 연속으로 나오지 않게 직전 곡은 피한다)
///
/// 오디오 재생 속도는 Time.timeScale 의 영향을 받지 않으므로 K.O.·필살기 슬로우 중에도 BGM 은 늘어지지 않는다.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class BattleBgm : MonoBehaviour
{
    [SerializeField] private AudioClip[] clips;
    [Range(0f, 1f)]
    [SerializeField] private float volume = 0.4f;

    private AudioSource source;
    private int lastIndex = -1;

    private void Awake()
    {
        source = GetComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0f;
        source.volume = volume;
    }

    private void Start()
    {
        PlayRandom();
    }

    private void Update()
    {
        // 일시정지(AudioListener.pause)나 포커스 이탈 중에는 isPlaying 이 false 일 수 있어 그때는 넘기지 않는다.
        if (!source.isPlaying && !AudioListener.pause && source.clip != null && source.time == 0f)
            PlayRandom();
    }

    private void PlayRandom()
    {
        if (clips == null || clips.Length == 0) return;

        int index = Random.Range(0, clips.Length);
        if (clips.Length > 1 && index == lastIndex)
            index = (index + 1 + Random.Range(0, clips.Length - 1)) % clips.Length;

        lastIndex = index;
        source.clip = clips[index];
        source.Play();
    }
}
