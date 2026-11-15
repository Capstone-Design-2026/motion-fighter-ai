using System;
using System.Diagnostics;

public enum RedboolPracticeInputKind { State, Event, Pause, Reset }

/// <summary>Local tutorial metadata; never sent over the network or used for combat input.</summary>
public sealed class RedboolPracticeInput
{
    public readonly RedboolPracticeInputKind Kind;
    public readonly string Action, Movement, Stream;
    public readonly float Confidence, Strength;
    public readonly int Generation;
    // Raw capture time checks spacing independently of changing clock-sync offsets.
    public readonly long CaptureNs, SourceAtNs, NowNs, LeaseUntilNs;
    public bool HasSource => CaptureNs > 0 && !String.IsNullOrEmpty(Stream);
    public RedboolPracticeInput(RedboolPracticeInputKind kind,string action,float confidence,string movement,float strength,
        string stream,int generation,long captureNs,long sourceAtNs,long nowNs,long leaseUntilNs)
    {
        Kind=kind;Action=action;Confidence=confidence;Movement=movement;Strength=strength;
        Stream=stream;Generation=generation;CaptureNs=captureNs;SourceAtNs=sourceAtNs;NowNs=nowNs;LeaseUntilNs=leaseUntilNs;
    }
}

/// <summary>Only private tutorial memory may survive a bounded output-lease gap.</summary>
public sealed class RedboolPracticeContinuity
{
    public const long MaxGapNs=100000000L, LeaseNs=150000000L, PrivateNs=220000000L;
    public enum Result { Reset, Paused, Fresh, FreshReset, Event, Legacy }
    string stream;
    int generation;
    long capture,until,lease,accounted,restNs;
    bool rest,paused;
    public bool Active => stream!=null;
    public long RestNs => restNs;
    public long PrivateUntilNs => until;
    public bool Fresh(long now) => Active && !paused && now<lease && now<until;
    public static long ClockNs()
    {
        long t=Stopwatch.GetTimestamp(),f=Stopwatch.Frequency;
        return (t/f)*1000000000L+(long)((t%f)*(1000000000.0/f));
    }
    public void Reset() { stream=null;generation=0;capture=until=lease=accounted=restNs=0;rest=paused=false; }
    void Account(long now)
    {
        if(!Active)return;
        long end=Math.Min(now,lease);
        if(rest&&!paused&&end>accounted)restNs+=end-accounted;
        accounted=Math.Max(accounted,end);
    }
    public bool Tick(long now)
    {
        Account(now);
        if(Active&&now>=until){Reset();return true;}
        return false;
    }
    public Result Observe(RedboolPracticeInput input,bool isRest)
    {
        if(input==null||input.Kind==RedboolPracticeInputKind.Reset){Reset();return Result.Reset;}
        if(!input.HasSource){Reset();return Result.Legacy;}
        bool expired=Tick(input.NowNs);
        bool same=Active&&stream==input.Stream&&generation==input.Generation;
        if(input.Kind==RedboolPracticeInputKind.Pause)
        {
            if(!same||capture!=input.CaptureNs||input.NowNs>=until){Reset();return Result.Reset;}
            // Account only through the original valid lease, never the gap.
            lease=Math.Min(lease,input.NowNs);paused=true;
            return Result.Paused;
        }
        if(input.Kind==RedboolPracticeInputKind.Event)
        {
            // The receiver already validated/deduplicated this one-shot and
            // its current STATE lease. A just-advanced tutorial must not add
            // another IDLE/STATE prerequisite or reuse rest credit for it.
            long eventLease=Math.Min(input.LeaseUntilNs,input.SourceAtNs+LeaseNs);
            if(same&&capture==input.CaptureNs)eventLease=Math.Min(eventLease,lease);
            if(input.SourceAtNs<=input.NowNs&&input.NowNs<eventLease)
            {
                if(Active&&!same)Reset();
                return Result.Event;
            }
            Reset();return Result.Reset;
        }
        long freshUntil=Math.Min(input.LeaseUntilNs,input.SourceAtNs+LeaseNs);
        if(input.NowNs>=freshUntil||input.SourceAtNs>input.NowNs)
        {Reset();return Result.Reset;}
        if(same&&input.CaptureNs<capture){Reset();return Result.Reset;}
        if(same&&input.CaptureNs==capture&&input.NowNs>=Math.Min(lease,freshUntil))
        {paused=true;return Result.Paused;}
        bool discontinuity=expired||!same||input.CaptureNs-capture>MaxGapNs;
        if(discontinuity)Reset();
        if(!Active||input.CaptureNs!=capture)
        {
            stream=input.Stream;generation=input.Generation;capture=input.CaptureNs;
            until=input.SourceAtNs+PrivateNs;lease=freshUntil;
        }
        else lease=Math.Min(lease,freshUntil); // Repeated packets cannot renew old evidence.
        paused=false;accounted=input.NowNs;
        if(!isRest)restNs=0;
        rest=isRest;
        return discontinuity?Result.FreshReset:Result.Fresh;
    }

    // Match presence is not a control lease. Preserve its ORIGINAL deadline
    // only when the sender reports expiry of the exact previously-valid frame.
    public static long PreservePresence(long existing,long now,bool previousCalibrated,bool calibrated,
        long previousCapture,long capture,int previousGeneration,int generation,string tracking,string previousStream,string stream)
    {
        return existing>now&&previousCalibrated&&calibrated&&!String.IsNullOrEmpty(stream)&&stream==previousStream&&previousCapture>0&&capture==previousCapture
            &&previousGeneration==generation&&tracking=="STALE"?existing:0;
    }
}
