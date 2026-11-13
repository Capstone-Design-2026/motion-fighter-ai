using UnityEngine;

/// <summary>
/// 전투 효과음. 공격이 나갈 때 휘두르는 소리를, 공격이 실제로 대미지를 입힌 순간 타격음을 낸다.
///   공격: 공격이 시작될 때마다(맞든 빗나가든) attackClip 재생
///   타격: 대미지가 들어갈 때마다 hitClips 중 하나를 무작위로 재생
///   마무리 일격: 상대를 쓰러뜨린 타격에는 hitClips 대신 finisherClip 재생
///
/// 플레이어와 적이 함께 쓰는 Fighter 가 호출하므로 키 입력, 웹캠 인식, AI 공격 모두 같은 소리가 난다.
///
/// 씬에 이 컴포넌트가 있고 클립이 꽂혀 있을 때만 동작한다. 없으면 조용히 아무 일도 하지 않는다.
/// (HitVfx 와 마찬가지로 에셋 참조가 필요하므로 자동 생성하지 않는다)
///
/// K.O.·필살기 슬로우가 timeScale 을 크게 낮추지만 오디오 재생 속도는 timeScale 의 영향을 받지 않으므로
/// 타격음이 늘어지지 않는다.
/// </summary>
[DefaultExecutionOrder(-400)]
[DisallowMultipleComponent]
public class CombatSfx : MonoBehaviour
{
    [Header("전체 On/Off")]
    [SerializeField] private bool enableSfx = true;

    [Header("공격 (휘두르기)")]
    [Tooltip("공격이 나갈 때마다 재생한다. 빗나가도 소리가 난다.")]
    [SerializeField] private AudioClip attackClip;
    [Range(0f, 1f)]
    [SerializeField] private float attackVolume = 1f;
    [Tooltip("클립 앞부분을 이만큼(초) 건너뛰고 재생한다. 지금 woosh 파일은 앞 0.34초가 작은 바람 소리이고 "
           + "휙 하는 소리가 약 0.37초부터 커져서, 처음부터 틀면 휘두르는 동작보다 소리가 늦게 들린다.")]
    [Range(0f, 1f)]
    [SerializeField] private float attackStartOffset = 0.3f;

    [Header("타격")]
    [Tooltip("공격이 대미지를 입힐 때마다 이 중 하나를 무작위로 재생한다.")]
    [SerializeField] private AudioClip[] hitClips;
    [Range(0f, 1f)]
    [SerializeField] private float hitVolume = 1f;
    [Tooltip("가드로 막힌 타격에도 타격음을 넣을지")]
    [SerializeField] private bool playOnGuard = true;
    [Tooltip("가드 타격일 때 위 볼륨에 곱할 배율.")]
    [Range(0f, 1f)]
    [SerializeField] private float guardVolumeMultiplier = 0.5f;

    [Header("마무리 일격")]
    [Tooltip("상대를 쓰러뜨린 타격에는 hitClips 대신 이 소리를 낸다. 비워두면 일반 타격음을 쓴다.")]
    [SerializeField] private AudioClip finisherClip;
    [Range(0f, 1f)]
    [SerializeField] private float finisherVolume = 1f;

    // 플레이어와 적이 동시에 휘둘러도 서로의 소리를 끊지 않을 만큼.
    private const int AttackVoiceCount = 4;

    private static CombatSfx instance;

    private AudioSource source;
    private AudioSource[] attackVoices;
    private int nextAttackVoice;

    private static bool prefabMissing;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatic() { instance = null; prefabMissing = false; }

    /// <summary>
    /// 씬에 CombatSfx 가 없으면 Resources/Audio/CombatSfx 프리팹으로 하나 만든다.
    /// 예전에는 GameScene 에만 놓여 있어서 스토리 전투 무대에서는 공격·타격·K.O. 소리가 나지 않았다.
    /// </summary>
    private static CombatSfx Ensure()
    {
        if (instance != null || prefabMissing) return instance;
        var prefab = Resources.Load<GameObject>("Audio/CombatSfx");
        if (prefab == null) { prefabMissing = true; return null; }
        Instantiate(prefab).name = "CombatSfx (auto)";   // Awake 가 instance 를 채운다
        return instance;
    }

    /// <summary>공격 시작 효과음.</summary>
    public static void PlayAttack()
    {
        var sfx = Ensure();
        if (sfx != null) sfx.PlayAttackClip();
    }

    /// <summary>타격 효과음.</summary>
    /// <param name="guarded">가드에 막힌 타격이면 true.</param>
    /// <param name="finisher">이 타격으로 상대가 쓰러졌으면 true.</param>
    public static void PlayHit(bool guarded, bool finisher = false)
    {
        var sfx = Ensure();
        if (sfx != null) sfx.PlayHitClip(guarded, finisher);
    }

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);
            return;
        }
        instance = this;

        source = GetComponent<AudioSource>();
        if (source == null) source = gameObject.AddComponent<AudioSource>();
        Configure(source);

        attackVoices = new AudioSource[AttackVoiceCount];
        for (int i = 0; i < attackVoices.Length; i++)
            attackVoices[i] = Configure(gameObject.AddComponent<AudioSource>());
    }

    private static AudioSource Configure(AudioSource s)
    {
        s.playOnAwake = false;
        s.spatialBlend = 0f;   // 2D. 카메라 위치와 상관없이 같은 크기로 들린다.
        return s;
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private void PlayAttackClip()
    {
        if (!enableSfx || attackClip == null) return;

        // 앞부분을 건너뛰려면 재생 위치를 정해야 하는데 PlayOneShot 은 그걸 못 한다.
        // 그래서 전용 AudioSource 몇 개를 번갈아 쓴다. 겹쳐 휘둘러도 앞 소리가 끊기지 않는다.
        AudioSource voice = attackVoices[nextAttackVoice];
        nextAttackVoice = (nextAttackVoice + 1) % attackVoices.Length;

        float offset = attackStartOffset < attackClip.length ? attackStartOffset : 0f;
        voice.clip = attackClip;
        voice.volume = attackVolume;
        voice.timeSamples = Mathf.FloorToInt(offset * attackClip.frequency);
        voice.Play();
    }

    private void PlayHitClip(bool guarded, bool finisher)
    {
        if (!enableSfx) return;

        // 마무리 일격은 가드 여부와 상관없이 제 볼륨으로 낸다. 가드 깎기로 끝나도 K.O. 는 K.O. 다.
        if (finisher && finisherClip != null)
        {
            source.PlayOneShot(finisherClip, finisherVolume);
            return;
        }

        if (hitClips == null || hitClips.Length == 0) return;
        if (guarded && !playOnGuard) return;

        AudioClip clip = hitClips[Random.Range(0, hitClips.Length)];
        if (clip == null) return;

        float volume = guarded ? hitVolume * guardVolumeMultiplier : hitVolume;
        source.PlayOneShot(clip, volume);
    }
}
