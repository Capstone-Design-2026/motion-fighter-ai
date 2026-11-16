using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Time/event policy only: never grants gameplay permission or confirms a hit.</summary>
public sealed class RedboolAnticipationWindow
{
    public const double AdvanceSeconds = .150, ReturnSeconds = .060, RejectSeconds = .035;
    public const int MaxRememberedEvents = 2048;
    readonly HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
    string eventId;
    double began, returnAt, returnFor, returnWeight;
    int baselineSerial;
    bool accepted, returning;
    public string State { get; private set; }
    public bool Active => eventId != null;
    public bool Returning => Active && returning;
    public double RecognizedAt => began;
    public int Started { get; private set; }
    public int Duplicates { get; private set; }
    public int Rejected { get; private set; }
    public int Handoffs { get; private set; }
    public int Timeouts { get; private set; }
    public bool DedupCapacityReached => seen.Count >= MaxRememberedEvents;

    public static string StateFor(string gesture)
    {
        switch(gesture)
        {
            case "LEFT_PUNCH": return "JAB";
            case "RIGHT_PUNCH": return "CROSS";
            case "LEFT_RAISE": return "HIGHKICK";
            case "RIGHT_RAISE": return "LOWKICK";
            default: return null;
        }
    }
    public bool Begin(string id,string gesture,double now,int serial)
    {
        string state=StateFor(gesture);
        if(string.IsNullOrEmpty(id)||id.Length>200||state==null||double.IsNaN(now)||double.IsInfinity(now))return false;
        if(seen.Contains(id)) {Duplicates++;return false;}
        // Do not evict an old ID and allow it to replay later in this connection.
        // At capacity, only this optional presentation feature stops accepting work.
        if(DedupCapacityReached)return false;
        seen.Add(id);
        if(Active)return false;
        eventId=id;State=state;began=now;baselineSerial=serial;accepted=returning=false;
        Started++;return true;
    }
    public void Result(string id,string status,double now)
    {
        if(!Active||id!=eventId||returning)return;
        if(status=="APPLIED")accepted=true;
        else {Rejected++;Return(now,RejectSeconds);}
    }
    public double Weight(double now,int serial)
    {
        if(!Active)return 0;
        // Authority wins even when its snapshot overtakes the corresponding ACK.
        if(!returning&&serial!=baselineSerial)
        {
            if(accepted)Handoffs++;
            Return(now,ReturnSeconds);
        }
        if(!returning&&now-began>=AdvanceSeconds)
        {
            Timeouts++;Return(began+AdvanceSeconds,ReturnSeconds);
        }
        if(returning)
        {
            double fraction=(now-returnAt)/returnFor;
            if(fraction>=1) {Cancel();return 0;}
            return returnWeight*Math.Max(0,1-Math.Max(0,fraction));
        }
        return EntryWeight(now);
    }
    double EntryWeight(double now) => Math.Min(1,Math.Max(0,(now-began)/.035));
    void Return(double now,double seconds)
    {
        returnWeight=EntryWeight(now);returnAt=now;returnFor=seconds;returning=true;
    }
    public void Cancel() {eventId=null;State=null;accepted=returning=false;}
}

/// <summary>Read-only gate: newest authority can cancel an older interpolated pose.</summary>
public static class RedboolAnticipationAuthority
{
    public static bool Allows(bool fresh,RedboolViewActor shown,RedboolViewWorld shownWorld,RedboolViewFrame newest)
    {
        return fresh&&newest!=null&&newest.Complete&&Safe(shown,shownWorld)&&Safe(newest.actors[1],newest.world)
            &&shownWorld.round==newest.world.round&&shownWorld.selectionRun==newest.world.selectionRun
            &&shown.character==newest.actors[1].character&&newest.actors[1].hp>=shown.hp;
    }
    static bool Safe(RedboolViewActor actor,RedboolViewWorld world)
    {
        return actor!=null&&world!=null&&world.fighting&&!world.paused&&!world.resolved
            &&actor.hp>0&&actor.live&&actor.phase==0&&(actor.specialFlags&1)==0;
    }
}

/// <summary>
/// Online guest's four normal attack poses only. Runs after authoritative bone
/// writes. A separate transform-only Animator samples rotations: the real rig,
/// gameplay scripts, renderer properties, events, sound and damage are never evaluated.
/// </summary>
public sealed class RedboolOnlineAnticipation : MonoBehaviour
{
    public const double SampleBudgetMs=2;
    static readonly string[] AttackStates={"JAB","CROSS","HIGHKICK","LOWKICK"};
    readonly RedboolAnticipationWindow window = new RedboolAnticipationWindow();
    Animator rig,sampler;
    GameObject samplerRoot;
    Avatar avatar;
    RuntimeAnimatorController controller;
    Transform[] sampleHierarchy, sampleBones, rigBones, authoritativeTargets;
    Vector3[] positions, scales;
    Quaternion[] rotations, forecast, authoritativePose;
    readonly int[] stateHashes = new int[4];
    bool safeRig, contextReady, forecastReady, visualCounted, authoritativeSaved;
    int contextRound, contextCharacter, contextHp, contextSerial, contextEpoch;
    double lastApply=-10, reportAt;
    public int RecognizedEvents { get; private set; }
    public int VisualStarts { get; private set; }
    public int SampleCount { get; private set; }
    public int SampleFailures { get; private set; }
    public int SampleBudgetStops { get; private set; }
    // Starts at LocalAction's arrival in Unity, not camera exposure or Python's
    // recognition timestamp; ends after the bone write, not physical display scanout.
    public double LastRecognizedToVisualMs { get; private set; } = -1;
    public double MaximumRecognizedToVisualMs { get; private set; }
    public double LastSampleCpuMs { get; private set; }
    public double LastBindCpuMs { get; private set; }
    public int LastVisualFrame { get; private set; } = -1;
    public RedboolAnticipationWindow Policy => window;

    void OnEnable()
    {
        RedboolOnlineInputBridge.LocalAction+=LocalAction;
        RedboolOnlineInputBridge.ActionResult+=ActionResult;
    }
    void OnDisable()
    {
        RedboolOnlineInputBridge.LocalAction-=LocalAction;
        RedboolOnlineInputBridge.ActionResult-=ActionResult;
        Cancel();ReleaseSampler();rig=null;controller=null;avatar=null;
    }
    void OnDestroy() {ReleaseSampler();}
    bool OnlineGuest
    {
        get
        {
            var session=RedboolPhotonSession.Instance;
            return RedboolPvp.Online&&RedboolPvp.IsClient&&session!=null&&session.InRoom&&!session.IsHost&&session.PeerPresent;
        }
    }
    void LocalAction(string id,string gesture)
    {
        RecognizedEvents++;
        double now=Time.realtimeSinceStartupAsDouble;
        if(!OnlineGuest||!contextReady||!safeRig||rig==null||rig.enabled||now-lastApply>=.15)return;
        if(window.Begin(id,gesture,now,contextSerial)) {forecastReady=false;visualCounted=false;}
    }
    void ActionResult(string id,string status) {window.Result(id,status,Time.realtimeSinceStartupAsDouble);}

    public void Cancel()
    {
        // A stale-link cancel may happen without another Apply. Remove the last
        // optional overlay immediately rather than freezing a predicted pose.
        if(authoritativeSaved)
            for(int i=0;i<authoritativeTargets.Length;i++)if(authoritativeTargets[i]!=null)
                authoritativeTargets[i].localRotation=authoritativePose[i];
        StopAfterAuthoritativeWrite();
    }
    void StopAfterAuthoritativeWrite()
    {
        // Apply has already received the newest authority; never replace it with
        // a cached older pose when cancelling for damage, pause or a new round.
        window.Cancel();contextReady=false;forecastReady=false;visualCounted=false;lastApply=-10;
        authoritativeSaved=false;
    }

    public void Apply(Animator animator,Transform[] bones,RedboolViewActor actor,RedboolViewWorld world)
    {
        double now=Time.realtimeSinceStartupAsDouble;
        var link=RedboolPvp.Instance!=null?RedboolPvp.Instance.ViewLink:null;
        // The displayed snapshot may trail Latest by the interpolation window.
        // New damage/pause/round authority cancels immediately, before it is drawn.
        if(!OnlineGuest||animator==null||bones==null||animator.enabled||link==null
            ||!RedboolAnticipationAuthority.Allows(link.Fresh,actor,world,link.Latest))
        {StopAfterAuthoritativeWrite();return;}
        var session=RedboolPhotonSession.Instance;
        if(rig!=animator||controller!=animator.runtimeAnimatorController||avatar!=animator.avatar||rigBones!=bones)
        {
            StopAfterAuthoritativeWrite();Bind(animator,bones);
        }
        if(!safeRig) {StopAfterAuthoritativeWrite();return;}
        if(contextReady&&(contextRound!=world.round||contextCharacter!=actor.character||actor.hp<contextHp||contextEpoch!=session.ConnectionEpoch))
            StopAfterAuthoritativeWrite();
        contextReady=true;contextRound=world.round;contextCharacter=actor.character;contextHp=actor.hp;
        contextEpoch=session.ConnectionEpoch;contextSerial=actor.attackSerial;lastApply=now;
        for(int i=0;i<bones.Length;i++)
        {
            authoritativeTargets[i]=bones[i];
            authoritativePose[i]=bones[i]!=null?bones[i].localRotation:Quaternion.identity;
        }
        authoritativeSaved=true;
        if(!window.Active)return;
        double weight=window.Weight(now,actor.attackSerial);
        if(weight<=0)return;
        // Returning freezes only the last predicted rotation sample. The target
        // remains this frame's fresh authoritative pose, never a replayed attack.
        if(!window.Returning)
        {
            if(!Sample(Mathf.Clamp((float)(now-window.RecognizedAt),0,(float)RedboolAnticipationWindow.AdvanceSeconds)))
            {SampleFailures++;StopAfterAuthoritativeWrite();return;}
            forecastReady=true;
        }
        if(!forecastReady)return;
        bool changed=false;
        for(int i=0;i<bones.Length;i++)if(bones[i]!=null)
        {
            Quaternion authoritative=bones[i].localRotation;
            Quaternion result=Quaternion.Slerp(authoritative,forecast[i],(float)weight);
            if(Quaternion.Angle(authoritative,result)>.05f)changed=true;
            bones[i].localRotation=result;
        }
        if(changed&&!visualCounted)
        {
            visualCounted=true;VisualStarts++;LastVisualFrame=Time.frameCount;
            LastRecognizedToVisualMs=Math.Max(0,(Time.realtimeSinceStartupAsDouble-window.RecognizedAt)*1000);
            MaximumRecognizedToVisualMs=Math.Max(MaximumRecognizedToVisualMs,LastRecognizedToVisualMs);
        }
        if(now>=reportAt&&visualCounted)
        {
            reportAt=now+5;
            Debug.Log(FormattableString.Invariant($"[REDBOOL ONLINE ANTICIPATION] visual_starts={VisualStarts} local_action_to_bone_ms={LastRecognizedToVisualMs:F2} max_ms={MaximumRecognizedToVisualMs:F2} sample_cpu_ms={LastSampleCpuMs:F3} budget_stops={SampleBudgetStops} handoffs={window.Handoffs} timeouts={window.Timeouts} rejected={window.Rejected} failures={SampleFailures} frame={LastVisualFrame} presentation_only=True"));
        }
    }

    void Bind(Animator animator,Transform[] bones)
    {
        ReleaseSampler();
        rig=animator;controller=animator.runtimeAnimatorController;avatar=animator.avatar;rigBones=bones;safeRig=false;
        if(controller==null||!rig.isHuman||rig.avatar==null||!rig.avatar.isValid||rig.layerCount!=1)return;
        // fireEvents does not suppress StateMachineBehaviour. Refuse these
        // controllers before assigning one to the isolated sampling Animator.
        if(rig.GetBehaviours<StateMachineBehaviour>().Length!=0)return;
        for(int i=0;i<AttackStates.Length;i++)
        {
            stateHashes[i]=Animator.StringToHash(rig.GetLayerName(0)+"."+AttackStates[i]);
            if(!rig.HasState(0,stateHashes[i]))return;
        }
        var view=rig.GetComponentInParent<Fighter3DView>();
        var root=view!=null&&view.visualRoot!=null?view.visualRoot:rig.transform;
        long began=System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            // Do not Instantiate a prefab then disable its scripts: Awake would
            // already have run. Copy transforms only, then add this sole Animator.
            // Hero punches also animate renderer blendshapes; there is deliberately
            // no renderer here for those curves to write, and no gameplay component.
            var map=new Dictionary<Transform,Transform>();
            samplerRoot=CloneTransforms(root,null,map);
            samplerRoot.name="REDBOOL isolated anticipation rig";
            sampler=map[rig.transform].gameObject.AddComponent<Animator>();
            sampler.enabled=false;sampler.fireEvents=false;sampler.applyRootMotion=false;
            sampler.avatar=avatar;sampler.runtimeAnimatorController=controller;
            sampler.cullingMode=AnimatorCullingMode.AlwaysAnimate;sampler.speed=1;
            samplerRoot.SetActive(true);sampler.enabled=true;
            sampler.Rebind();sampler.Update(0);sampler.enabled=false;
            if(!sampler.isInitialized||!sampler.isHuman) {ReleaseSampler();return;}
            sampleBones=new Transform[bones.Length];
            for(int i=0;i<bones.Length;i++)if(bones[i]!=null)
            {
                if(!map.TryGetValue(bones[i],out sampleBones[i])) {ReleaseSampler();return;}
            }
            sampleHierarchy=samplerRoot.GetComponentsInChildren<Transform>(true);
            positions=new Vector3[sampleHierarchy.Length];scales=new Vector3[sampleHierarchy.Length];rotations=new Quaternion[sampleHierarchy.Length];
            for(int i=0;i<sampleHierarchy.Length;i++)
            {positions[i]=sampleHierarchy[i].localPosition;scales[i]=sampleHierarchy[i].localScale;rotations[i]=sampleHierarchy[i].localRotation;}
            forecast=new Quaternion[bones.Length];authoritativePose=new Quaternion[bones.Length];authoritativeTargets=new Transform[bones.Length];safeRig=true;
        }
        catch(Exception e)
        {
            ReleaseSampler();Debug.LogWarning("[REDBOOL ONLINE ANTICIPATION] Isolated rig unavailable: "+e.GetType().Name);
        }
        finally {LastBindCpuMs=(System.Diagnostics.Stopwatch.GetTimestamp()-began)*1000d/System.Diagnostics.Stopwatch.Frequency;}
    }

    static GameObject CloneTransforms(Transform source,Transform parent,Dictionary<Transform,Transform> map)
    {
        var copy=new GameObject(source.name);copy.SetActive(false);copy.hideFlags=HideFlags.HideAndDontSave;
        copy.transform.SetParent(parent,false);copy.transform.localPosition=source.localPosition;
        copy.transform.localRotation=source.localRotation;copy.transform.localScale=source.localScale;
        map[source]=copy.transform;
        for(int i=0;i<source.childCount;i++)CloneTransforms(source.GetChild(i),copy.transform,map);
        if(parent!=null)copy.SetActive(source.gameObject.activeSelf);
        return copy;
    }
    void ReleaseSampler()
    {
        safeRig=false;
        if(samplerRoot!=null)
        {
            samplerRoot.SetActive(false);
            if(Application.isPlaying)Destroy(samplerRoot);else DestroyImmediate(samplerRoot);
        }
        sampler=null;samplerRoot=null;sampleBones=null;sampleHierarchy=null;
    }

    bool Sample(float seconds)
    {
        if(rig==null||rig.enabled||!safeRig||sampler==null||sampler.enabled||!sampler.gameObject.activeInHierarchy)return false;
        int state=Array.IndexOf(AttackStates,window.State);
        if(state<0)return false;
        long began=System.Diagnostics.Stopwatch.GetTimestamp();
        bool sampled=false;
        try
        {
            for(int i=0;i<sampleHierarchy.Length;i++)
            {sampleHierarchy[i].localPosition=positions[i];sampleHierarchy[i].localScale=scales[i];sampleHierarchy[i].localRotation=rotations[i];}
            sampler.fireEvents=false;sampler.applyRootMotion=false;sampler.speed=1;sampler.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            // Fixed animation seconds track recognition time; an arriving ACK
            // never restarts this clock or jumps it to a server-normalized time.
            sampler.PlayInFixedTime(stateHashes[state],0,Mathf.Clamp(seconds,0,(float)RedboolAnticipationWindow.AdvanceSeconds));sampler.Update(0);
            for(int i=0;i<sampleBones.Length;i++)forecast[i]=sampleBones[i]!=null?sampleBones[i].localRotation:Quaternion.identity;
            sampled=true;SampleCount++;
        }
        catch(Exception e)
        {
            safeRig=false;Debug.LogWarning("[REDBOOL ONLINE ANTICIPATION] Sampling disabled: "+e.GetType().Name);
        }
        finally
        {
            if(sampler!=null) {sampler.enabled=false;sampler.fireEvents=false;sampler.applyRootMotion=false;}
            LastSampleCpuMs=(System.Diagnostics.Stopwatch.GetTimestamp()-began)*1000d/System.Diagnostics.Stopwatch.Frequency;
            // A slow optional overlay must not repeatedly consume the frame
            // budget. Discard it and keep using authoritative poses for this rig.
            if(LastSampleCpuMs>SampleBudgetMs)
            {
                SampleBudgetStops++;sampled=safeRig=false;
                Debug.LogWarning(FormattableString.Invariant($"[REDBOOL ONLINE ANTICIPATION] Optional sampling stopped: {LastSampleCpuMs:F3}ms exceeds {SampleBudgetMs:F1}ms budget."));
            }
        }
        return sampled;
    }
}
