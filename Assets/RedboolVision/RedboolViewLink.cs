using System;
using System.Collections.Generic;
using System.Net;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using System.Net.Sockets;
using UnityEngine;

/// <summary>LAN display channel only. Camera commands still go directly to authoritative P1/P2 receivers.</summary>
[DefaultExecutionOrder(450)]
public sealed class RedboolViewLink : MonoBehaviour
{
    RedboolPvp game;
    UdpClient socket;
    IPEndPoint peer;
    Guid session=Guid.NewGuid(),nonce=Guid.NewGuid(),peerNonce;
    readonly HashSet<Guid> retiredSessions=new HashSet<Guid>();
    readonly RedboolViewAssembler assembler=new RedboolViewAssembler();
    readonly List<RedboolViewFrame> history=new List<RedboolViewFrame>();
    readonly List<RedboolViewEffect> effects=new List<RedboolViewEffect>();
    readonly Transform[][] bones={new Transform[(int)HumanBodyBones.LastBone],new Transform[(int)HumanBodyBones.LastBone]};
    readonly Fighter3DView[] views=new Fighter3DView[2];
    RedboolViewMirror mirror;
    double nextSend,lastHello=-10,lastHeartbeat=-10,lastFrame=-10,echo,lastReport;
    ulong sequence,ack;
    int effectId,received,invalid,sentBytes;
    bool client,initialized;
    string traceFile,captureDirectory;
    int captureCount;
    double lastCapture;
    StreamWriter trace;
    // Fixed-size guest samples: distinguish replica/UI work from a slow whole
    // frame without per-frame logging, allocations or changing render quality.
    const int TimingCapacity=512;
    readonly float[] replicaMs=new float[TimingCapacity],remoteUiMs=new float[TimingCapacity],receiveMs=new float[TimingCapacity],frameMs=new float[TimingCapacity],timingScratch=new float[TimingCapacity];
    int timingSamples,replicaBoneCount,maxSnapshotWaves,maxSnapshotEffects;
    long frameUiTicks,frameReceiveTicks;
    static long TimingNow => System.Diagnostics.Stopwatch.GetTimestamp();
    static float TicksMs(long ticks) => (float)(ticks*(1000.0/System.Diagnostics.Stopwatch.Frequency));
    void ShowRemoteMeasured(RedboolViewFrame frame,bool fresh)
    {
        long start=TimingNow;game.ShowRemote(frame,fresh);frameUiTicks+=TimingNow-start;
    }
    void SampleGuest(long replicaTicks)
    {
        int index=timingSamples%TimingCapacity;timingSamples++;
        replicaMs[index]=TicksMs(replicaTicks);remoteUiMs[index]=TicksMs(frameUiTicks);
        receiveMs[index]=TicksMs(frameReceiveTicks);frameMs[index]=Time.unscaledDeltaTime*1000f;
        if(assembler.Latest!=null) {maxSnapshotWaves=Math.Max(maxSnapshotWaves,assembler.Latest.world.waves.Count);maxSnapshotEffects=Math.Max(maxSnapshotEffects,assembler.Latest.world.effects.Count);}
    }
    string TimingStats(string label,float[] values)
    {
        int count=Math.Min(timingSamples,TimingCapacity);Array.Copy(values,timingScratch,count);Array.Sort(timingScratch,0,count);
        int p50=(count-1)/2,p95=Math.Max(0,(int)Math.Ceiling(count*.95)-1);
        return FormattableString.Invariant($" {label}_p50_ms={timingScratch[p50]:F3} {label}_p95_ms={timingScratch[p95]:F3} {label}_max_ms={timingScratch[count-1]:F3}");
    }
    void ReportGuestTiming()
    {
        if(!client ||timingSamples==0) return;
        Debug.Log($"[REDBOOL VIEW CPU] samples={Math.Min(timingSamples,TimingCapacity)}"+TimingStats("replica",replicaMs)+TimingStats("ui",remoteUiMs)+TimingStats("receive",receiveMs)+TimingStats("frame",frameMs)
            +$" bones={replicaBoneCount} snapshot_waves_max={maxSnapshotWaves} snapshot_effects_max={maxSnapshotEffects} history={history.Count} retired_sessions={retiredSessions.Count} target_fps={Application.targetFrameRate} vsync={QualitySettings.vSyncCount} focused={Application.isFocused} fresh={Fresh}");
        timingSamples=maxSnapshotWaves=maxSnapshotEffects=0;
    }
    [Serializable] sealed class TraceRow {public string seq;public double time,arrival;public RedboolViewWorld world;public RedboolViewActor[] actors;}
    void TraceFrame(RedboolViewFrame frame)
    {
        if(trace==null) return;
        trace.WriteLine(JsonUtility.ToJson(new TraceRow {seq=frame.seq.ToString(),time=frame.time,arrival=Time.realtimeSinceStartupAsDouble,world=frame.world,actors=frame.actors}));
    }
    public bool PeerReady => !client && peer!=null && Time.realtimeSinceStartupAsDouble-lastHeartbeat<.25 && ack>0 && sequence>=ack && sequence-ack<=8;
    public bool Fresh => client && assembler.Latest!=null && Time.realtimeSinceStartupAsDouble-lastFrame<.25;
    public double RttMs {get;private set;}
    public RedboolViewFrame Latest => assembler.Latest;
    public void Initialize(RedboolPvp owner)
    {
        game=owner;client=RedboolPvp.IsClient;
        traceFile=Environment.GetEnvironmentVariable("REDBOOL_VIEW_TRACE_FILE");captureDirectory=Environment.GetEnvironmentVariable("REDBOOL_VIEW_CAPTURE_DIR");
        if(!String.IsNullOrEmpty(traceFile)) trace=new StreamWriter(traceFile,false);
        if(!String.IsNullOrEmpty(captureDirectory)) Directory.CreateDirectory(captureDirectory);
        try
        {
            socket=new UdpClient(new IPEndPoint(IPAddress.Any,client?0:RedboolViewProtocol.Port));
            socket.Client.Blocking=false;socket.Client.ReceiveBufferSize=128*1024;
            if(client) {
                int port=RedboolViewProtocol.Port;var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"--redbool-view-port");
                if(index>=0 && index+1<args.Length &&int.TryParse(args[index+1],out var parsed) &&parsed>0 &&parsed<=65535) port=parsed;
                peer=new IPEndPoint(IPAddress.Parse(RedboolPvp.HostAddress),port);
            }
            for(int i=0;i<2;i++)
            {
                views[i]=game.Players[i].GetComponent<Fighter3DView>();
                for(int b=0;b<bones[i].Length;b++) {bones[i][b]=views[i].modelAnimator.GetBoneTransform((HumanBodyBones)b);if(bones[i][b]) replicaBoneCount++;}
            }
            if(client) {mirror=gameObject.AddComponent<RedboolViewMirror>();mirror.Initialize(game,views,bones);Time.timeScale=1;}
            initialized=true;Debug.Log("[REDBOOL VIEW] "+(client?"guest "+peer:"host :5067")+" protocol="+RedboolViewProtocol.Version);
        }
        catch(Exception e) when(e is SocketException || e is FormatException)
        {Debug.LogError("[REDBOOL VIEW] Cannot open display channel: "+e.Message);}
    }
    public static void RecordEffect(Vector3 point,int kind)
    {
        var link=RedboolPvp.Instance?.ViewLink;
        if(link==null || link.client) return;
        link.effects.Add(new RedboolViewEffect {id=++link.effectId,point=point,kind=kind,time=Time.realtimeSinceStartupAsDouble});
        if(link.effects.Count>12) link.effects.RemoveAt(0);
    }
    void Send(RedboolViewPacket packet)
    {
        if(peer==null || socket==null) return;
        var bytes=RedboolViewProtocol.Encode(packet);
        try {socket.Send(bytes,bytes.Length,peer);sentBytes+=bytes.Length;AuditPacket(packet,bytes);}
        catch(SocketException e) when(e.SocketErrorCode==SocketError.WouldBlock || e.SocketErrorCode==SocketError.ConnectionReset || e.SocketErrorCode==SocketError.NetworkUnreachable || e.SocketErrorCode==SocketError.HostUnreachable) { }
    }
    void AuditPacket(RedboolViewPacket packet,byte[] bytes)
    {
        // One sampled snapshot per second, not video or per-frame disk writes.
        // Compare encoded bytes so quaternion quantization cannot fake a mismatch.
        if(packet.kind<1||packet.kind>3||packet.seq%30!=0)return;
        using(var sha=SHA256.Create())
            Debug.Log($"[REDBOOL VIEW AUDIT] client={client} session={packet.session:N} seq={packet.seq} kind={packet.kind} sha256={BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","")}");
    }
    void Receive(double now)
    {
        // A bounded drain prevents a flooded UDP socket from starving rendering or input leases.
        for(int i=0;i<128;i++)
        {
            IPEndPoint from=new IPEndPoint(IPAddress.Any,0);byte[] data;
            try {data=socket.Receive(ref from);}
            catch(SocketException e) when(e.SocketErrorCode==SocketError.WouldBlock || e.SocketErrorCode==SocketError.ConnectionReset) {break;}
            // A camera-free LAN check must never pair a player, reset practice,
            // or steal the display peer. Echo only a small bounded nonce.
            if(!client && data.Length==48 && Encoding.ASCII.GetString(data,0,16)=="REDBOOL_PING_V1:") {
                string token=Encoding.ASCII.GetString(data,16,32);
                if(Guid.TryParseExact(token,"N",out _)) {
                    byte[] reply=Encoding.ASCII.GetBytes("REDBOOL_PONG_V1:"+token);
                    socket.Send(reply,reply.Length,from);
                }
                continue;
            }
            if(!client && Encoding.ASCII.GetString(data)=="REDBOOL_DISCOVER_V1") {
                byte[] reply=Encoding.UTF8.GetBytes("{\"service\":\"REDBOOL\",\"view_version\":"+RedboolViewProtocol.Version+",\"view_port\":5067,\"p2_port\":5066}");
                socket.Send(reply,reply.Length,from);continue;
            }
            if(!RedboolViewProtocol.TryDecode(data,out var p)) {invalid++;continue;}
            if(client)
            {
                if(!from.Equals(peer) || p.nonce!=nonce || p.kind<1 ||p.kind>3 ||p.session==Guid.Empty ||retiredSessions.Contains(p.session)) {invalid++;continue;}
                AuditPacket(p,data);
                if(session!=p.session)
                {
                    if(assembler.Latest!=null) retiredSessions.Add(session);
                    session=p.session;assembler.Reset();history.Clear();mirror.ClearTransient();
                }
                var complete=assembler.Push(p,now);
                if(complete!=null)
                {
                    received++;lastFrame=now;TraceFrame(complete);RedboolFrameMetrics.NoteLiveInput();history.Add(complete);if(history.Count>8) history.RemoveAt(0);
                    if(complete.seq%30==0) Debug.Log($"[REDBOOL VIEW APPLIED] session={complete.session:N} seq={complete.seq} round={complete.world.round} resolved={complete.world.resolved}");
                    if(p.echo>0 && now>=p.echo) RttMs=(now-p.echo)*1000;
                    ack=complete.seq;
                }
            }
            else if(p.kind==0)
            {
                if(peer==null || now-lastHello>2 || from.Equals(peer) && p.nonce==peerNonce)
                {
                    if(peer==null || !from.Equals(peer)||peerNonce!=p.nonce) {ack=0;lastHeartbeat=-10;}
                    peer=from;peerNonce=p.nonce;lastHello=now;echo=p.time;
                }
            }
            else if(p.kind==5 &&peer!=null &&from.Equals(peer) &&p.nonce==peerNonce &&p.session==session &&PeerReady &&p.seq<=sequence &&sequence-p.seq<=8)
                game.SkipPractice(1,p.practiceRun.ToString("N"),p.practiceStep);
            else if(p.kind==4 && peer!=null && from.Equals(peer) &&p.nonce==peerNonce &&p.session==session &&p.seq<=sequence &&p.seq>=ack)
            {ack=p.seq;lastHeartbeat=lastHello=now;echo=p.time;}
        }
    }
    public void RequestPracticeSkip()
    {
        if(!client ||!Fresh ||assembler.Latest==null) return;
        var a=assembler.Latest.actors[1];if(a.step>=8)return;
        Send(new RedboolViewPacket {kind=5,session=session,nonce=nonce,seq=ack,time=Time.realtimeSinceStartupAsDouble,practiceRun=a.practiceRun,practiceStep=(byte)a.step});
    }
    void Update()
    {
        if(!initialized) return;
        double now=Time.realtimeSinceStartupAsDouble;
        if(client) {frameUiTicks=0;long start=TimingNow;Receive(now);frameReceiveTicks=TimingNow-start;}
        else Receive(now);
        if(client && now>=nextSend)
        {
            nextSend=now+.05;
            Send(new RedboolViewPacket {kind=0,session=Guid.Empty,nonce=nonce,time=now});
            if(ack>0 && mirror.Ready) Send(new RedboolViewPacket {kind=4,session=session,nonce=nonce,seq=ack,time=now});
        }
        if(client) {Time.timeScale=Fresh && assembler.Latest!=null && (!assembler.Latest.world.paused ||assembler.Latest.world.resolved)?1:0;if(!Fresh) ShowRemoteMeasured(assembler.Latest,false);}
        if(now-lastReport>=5)
        {
            Debug.Log(FormattableString.Invariant($"[REDBOOL VIEW METRIC] client={client} seq={(client?ack:sequence)} complete={received} invalid={invalid} bytes={sentBytes} fresh={(client?Fresh:PeerReady)} rtt_ms={RttMs:F2}"));
            ReportGuestTiming();lastReport=now;sentBytes=0;
        }
    }
    void LateUpdate()
    {
        if(!initialized) return;
        double now=Time.realtimeSinceStartupAsDouble;
        if(!String.IsNullOrEmpty(captureDirectory) &&game.Fighting &&now-lastCapture>8 &&captureCount<3)
        {lastCapture=now;ScreenCapture.CaptureScreenshot(Path.Combine(captureDirectory,(client?"guest":"host")+(captureCount++)+".png"));}
        if(client)
        {
            if(!Fresh || history.Count==0) {SampleGuest(0);return;}
            var latest=history[history.Count-1];double target=latest.time+now-latest.arrival-.05;
            var a=history[0];var b=a;
            foreach(var frame in history) {b=frame;if(frame.time>=target) break;a=frame;}
            float t=a==b?1:Mathf.Clamp01((float)((target-a.time)/(b.time-a.time)));
            // Never blend across round resets or paused snapshots.
            if(a.world.round!=b.world.round || a.world.paused!=b.world.paused) {a=b;t=1;}
            long start=TimingNow;mirror.Apply(a,b,t);long replicaTicks=TimingNow-start;
            ShowRemoteMeasured(b,true);SampleGuest(replicaTicks);return;
        }
        if(now<nextSend) return;nextSend=Math.Max(nextSend+1.0/30,now-.01);
        sequence++;
        if(peer==null || now-lastHello>2) return;
        var world=CaptureWorld();
        Send(new RedboolViewPacket {kind=1,session=session,nonce=peerNonce,seq=sequence,time=now,echo=echo,world=world});
        var captured=new RedboolViewFrame {seq=sequence,time=now,world=world};
        for(byte i=0;i<2;i++) {captured.actors[i]=CaptureActor(i);Send(new RedboolViewPacket {kind=(byte)(2+i),session=session,nonce=peerNonce,seq=sequence,time=now,echo=echo,actor=captured.actors[i]});}
        TraceFrame(captured);
    }
    RedboolViewActor CaptureActor(int i)
    {
        var p=game.Players[i];var h=p.GetComponent<Health>();var s=p.GetComponent<SpecialMove>();var practice=game.Practices[i];
        var a=new RedboolViewActor {root=p.transform.position,visual=views[i].visualRoot.position,rotation=views[i].visualRoot.rotation,scale=views[i].visualRoot.localScale,hips=bones[i][0]?bones[i][0].localPosition:Vector3.zero,hp=h.CurrentHealth,maxHp=h.MaxHealth,charge=s.Charge,maxCharge=s.HitsToCharge,phase=(int)s.CurrentPhase,step=practice.Step,hits=practice.Hits,practiceRun=Guid.Parse(practice.Run),live=game.Receivers[i].LiveReady,attackSerial=p.GetComponent<Fighter>().AttackSerial};
        for(int b=0;b<a.bones.Length;b++) a.bones[b]=bones[i][b]?bones[i][b].localRotation:Quaternion.identity;
        a.chargeLevel=s.VisualChargeLevel;
        string status=game.Receivers[i].PersonalStatus;a.personal=(byte)(status.Contains("PERSONAL RANGE")?2:status.Contains("CYCLES")?1:0);
        var renderer=views[i].visualRoot.GetComponentInChildren<Renderer>();if(renderer) {var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);a.tint=block.GetColor("_BaseColor");}
        return a;
    }
    RedboolViewWorld CaptureWorld()
    {
        var cam=Camera.main;var world=new RedboolViewWorld {round=game.Round,gate=game.Gate,countdown=game.Countdown,remaining=game.Remaining,paused=game.Paused,resolved=game.Resolved,fighting=game.Fighting,winner=game.Result.StartsWith("1P")?1:game.Result.StartsWith("2P")?2:0,camera=cam.transform.position,rotation=cam.transform.rotation,fov=cam.fieldOfView};
        foreach(var wave in FindObjectsByType<QiWave>(FindObjectsSortMode.None)) if(wave.isActiveAndEnabled && !wave.Finished &&world.waves.Count<8) world.waves.Add(new RedboolViewWave {id=wave.GetInstanceID(),point=wave.transform.position,rotation=wave.transform.rotation});
        effects.RemoveAll(x=>Time.realtimeSinceStartupAsDouble-x.time>1);world.effects.AddRange(effects);return world;
    }
    void OnDestroy() {socket?.Close();socket=null;trace?.Dispose();trace=null;}
}
