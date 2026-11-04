using UnityEngine;

/// <summary>One sound on a confirmed contact. No audio on whiffs or rejected damage.</summary>
public sealed class CombatContactAudio:MonoBehaviour
{
    static CombatContactAudio instance;
    readonly AudioSource[] sources=new AudioSource[6];
    AudioClip punch,kick,guard;int next;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]static void ResetStatic(){instance=null;}
    void Awake(){
        punch=Resources.Load<AudioClip>("Audio/CombatContact/Punch");kick=Resources.Load<AudioClip>("Audio/CombatContact/Kick");guard=Resources.Load<AudioClip>("Audio/CombatContact/Guard");
        for(int i=0;i<sources.Length;i++){sources[i]=gameObject.AddComponent<AudioSource>();sources[i].playOnAwake=false;sources[i].spatialBlend=0;}
    }
    void OnDestroy(){if(instance==this)instance=null;}
    public static void Play(CombatMove move,bool guarded){
        if(!instance)instance=new GameObject("Combat Contact Audio").AddComponent<CombatContactAudio>();
        var clip=guarded?instance.guard:move==CombatMove.HighKick||move==CombatMove.LowKick?instance.kick:instance.punch;
        if(!clip)return;var source=instance.sources[instance.next++%instance.sources.Length];
        source.pitch=move==CombatMove.Jab?1.06f:move==CombatMove.LowKick?.96f:1f;
        source.clip=clip;source.volume=guarded?.43f:.55f;source.Play();
    }
}
