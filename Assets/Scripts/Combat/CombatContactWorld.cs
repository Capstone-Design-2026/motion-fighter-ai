using System.Collections.Generic;
using UnityEngine;

/// <summary>Evaluate all contacts after every fighter has applied its final animated pose.</summary>
[DefaultExecutionOrder(150)]
public sealed class CombatContactWorld:MonoBehaviour
{
    static CombatContactWorld instance;
    readonly List<Fighter> fighters=new List<Fighter>();
    readonly List<Contact> pending=new List<Contact>();
    struct Contact {public Fighter from,to;public int serial;public float t;public Vector3 point;public CombatBodyPart part;}
    float previousTime;bool hasTime;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]static void ResetStatic(){instance=null;}
    public static void Register(Fighter fighter){
        if(!Application.isPlaying)return;
        if(!instance)instance=new GameObject("Combat Contacts").AddComponent<CombatContactWorld>();
        if(!instance.fighters.Contains(fighter))instance.fighters.Add(fighter);
    }
    public static void Unregister(Fighter fighter){if(instance)instance.fighters.Remove(fighter);}
    void OnDestroy(){if(instance==this)instance=null;}
    // Windows derived from the installed clips: extension/impact only, never the recovery.
    // Straight: the installed clip (AuthoredPunches/Hero_CROSS and the enemy clips made from it) extends the arm
    // from .10s to .167s and then holds it out. The window covers that extension and the start of the hold,
    // the same length as before; it was .295~.43 for the previous clip, whose arm extended from .30s to .40s.
    public static Vector2 Window(CombatMove move){
        switch(move){case CombatMove.Jab:return new Vector2(.12f,.215f);
            case CombatMove.Straight:return new Vector2(.12f,.255f);
            case CombatMove.HighKick:return new Vector2(.295f,.49f);
            default:return new Vector2(.29f,.445f);}
    }
    void LateUpdate()
    {
        float now=Time.time,dt=hasTime?now-previousTime:0;
        for(int i=fighters.Count-1;i>=0;i--)if(!fighters[i])fighters.RemoveAt(i);
        foreach(var f in fighters)if(f.isActiveAndEnabled)f.Contacts.Capture();
        pending.Clear();
        if(dt>0&&Time.timeScale>0){
            foreach(var attacker in fighters){
                if(!attacker.PreciseAttackActive)continue;
                var window=Window(attacker.CurrentMove);
                float a=attacker.AttackStarted+window.x,b=attacker.AttackStarted+window.y;
                if(now<a||previousTime>b)continue;
                float begin=Mathf.Clamp01((a-previousTime)/dt),end=Mathf.Clamp01((b-previousTime)/dt);
                foreach(var target in fighters){
                    if(target==attacker||!target.isActiveAndEnabled||!target.Contacts.Ready||target.Health.IsDead||attacker.HasContacted(target.Health))continue;
                    bool guarded=attacker.IsGuardedBy(target);float earliest=2;Vector3 point=default;CombatBodyPart part=default;
                    int move=(int)attacker.CurrentMove;
                    for(int i=0;i<target.Contacts.hurt.Length;i++){
                        if(!CombatBodyPose.Allows(attacker.CurrentMove,target.Contacts.parts[i],guarded))continue;
                        if(CombatContactGeometry.Sweep(attacker.Contacts.previousStrikes[move],attacker.Contacts.strikes[move],target.Contacts.previousHurt[i],target.Contacts.hurt[i],begin,end,out float t,out var p)&&t<earliest){earliest=t;point=p;part=target.Contacts.parts[i];}
                    }
                    if(earliest<=1)pending.Add(new Contact{from=attacker,to=target,serial=attacker.AttackSerial,t=earliest,point=point,part=part});
                }
            }
            pending.Sort((a,b)=>{int c=a.t.CompareTo(b.t);return c!=0?c:a.from.GetInstanceID().CompareTo(b.from.GetInstanceID());});
            if(!RedboolPvp.Active) {
                foreach(var c in pending)if(c.from.PreciseAttackActive&&c.from.AttackSerial==c.serial&&!c.from.HasContacted(c.to.Health))c.from.ResolveContact(c.to,c.point,c.part);
            } else {
                // Contacts within one millisecond are a trade, not an instance-ID win.
                // Earlier contacts still interrupt later ones. Snapshot eligibility
                // before resolving the group; never revive an old queued attack.
                for(int begin=0;begin<pending.Count;) {
                    int end=begin+1;while(end<pending.Count&&(pending[end].t-pending[begin].t)*dt<=.001f)end++;
                    var eligible=new List<Contact>();
                    for(int i=begin;i<end;i++){var c=pending[i];if(c.from.PreciseAttackActive&&c.from.AttackSerial==c.serial&&!c.from.HasContacted(c.to.Health))eligible.Add(c);}
                    foreach(var c in eligible)if(!c.from.HasContacted(c.to.Health))c.from.ResolveContact(c.to,c.point,c.part);
                    begin=end;
                }
            }
        }
        foreach(var f in fighters)f.FinishPreciseAttack(now);
        previousTime=now;hasTime=true;
    }
}
