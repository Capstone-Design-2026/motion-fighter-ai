using System;
using System.Collections.Generic;
using System.Net;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using System.Net.Sockets;
using UnityEngine;

/// <summary>LAN UDP or authenticated Photon display transport; gameplay remains authoritative on P1.</summary>
[DefaultExecutionOrder(450)]
public sealed class RedboolViewLink : MonoBehaviour
{
    RedboolPvp game;
    UdpClient socket;
    IPEndPoint peer;
    bool online;
    RedboolPhotonSession onlineSession;
    int onlineEpoch;
    bool OnlineOwnerValid => !online || (RedboolPvp.Active && RedboolPvp.Online && RedboolPvp.Instance==game
        && onlineSession!=null && onlineSession.ConnectionEpoch==onlineEpoch);
    readonly Queue<byte[]> onlineInbox=new Queue<byte[]>();
    double onlineArrival=-1,onlineJitter;
    public double InterpolationMs => online?Math.Max(33,Math.Min(75,33+onlineJitter*2000)):50;
    Guid session=Guid.NewGuid(),nonce=Guid.NewGuid(),peerNonce;
    readonly HashSet<Guid> retiredSessions=new HashSet<Guid>();
    readonly RedboolViewAssembler assembler=new RedboolViewAssembler();
    readonly List<RedboolViewFrame> history=new List<RedboolViewFrame>();
    readonly List<RedboolViewEffect> effects=new List<RedboolViewEffect>();
    readonly Transform[][] bones={new Transform[(int)HumanBodyBones.LastBone],new Transform[(int)HumanBodyBones.LastBone]};
    readonly Fighter3DView[] views=new Fighter3DView[2];
    readonly HeroPunchSurfaceVisibility[] punchSurfaces=new HeroPunchSurfaceVisibility[2];
    RedboolViewMirror mirror;
    double nextSend,lastHello=-10,lastHeartbeat=-10,lastFrame=-10,echo,lastReport;
    ulong sequence,ack,minimumReadySequence;
    int effectId,received,invalid,sentBytes;
    bool client,initialized;
    RedboolViewPacket selectionRequest;
    double selectionRequestUntil,nextSelectionSend;
    public bool SelectionPending => selectionRequest!=null;
    double nextBindAttempt;
    public bool Bound => initialized && (online?onlineSession!=null&&onlineSession.InRoom:socket!=null);
    public string BindError { get; private set; }
    public bool PortConflict { get; private set; }
    public string ConnectionIssue => string.IsNullOrEmpty(BindError) ? null : PortConflict
        ? "화면 연결을 다른 게임이 사용 중이에요 · 이전 게임을 닫으면 자동 재연결됩니다"
        : "화면 연결을 열지 못했어요 · 자동 재시도 중 · 계속되면 메뉴에서 다시 참가해주세요";
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
    public bool PeerReady => Bound && !client && peer!=null && Time.realtimeSinceStartupAsDouble-lastHeartbeat<.25 && ack>0 && ack>=minimumReadySequence && sequence>=ack && sequence-ack<=8;
    public void RequireCurrentAvatarReady()
    {
        if (client) return;
        minimumReadySequence = sequence + 1;
        ack = 0; lastHeartbeat = -10;
    }
    public bool Fresh => Bound && client && assembler.Latest!=null && Time.realtimeSinceStartupAsDouble-lastFrame<.25;
    public double RttMs {get;private set;}
    public RedboolViewFrame Latest => assembler.Latest;
    public static int ResolvePeerPort(bool isOnline,string[] args)
    {
        if(!isOnline && args!=null) {
            int index=Array.IndexOf(args,"--redbool-view-port");
            if(index>=0 && index+1<args.Length && int.TryParse(args[index+1],out var value) && value>0 && value<=65535)return value;
        }
        return RedboolViewProtocol.Port;
    }
    public void Initialize(RedboolPvp owner)
    {
        game=owner;client=RedboolPvp.IsClient;online=RedboolPvp.Online;
        if(online) {onlineSession=RedboolPhotonSession.Instance;if(onlineSession==null)throw new InvalidOperationException("Online room must exist before arena initialization");onlineEpoch=onlineSession.ConnectionEpoch;onlineSession.Message+=ReceiveOnline;}
        traceFile=Environment.GetEnvironmentVariable("REDBOOL_VIEW_TRACE_FILE");captureDirectory=Environment.GetEnvironmentVariable("REDBOOL_VIEW_CAPTURE_DIR");
        if(!String.IsNullOrEmpty(traceFile)) trace=new StreamWriter(traceFile,false);
        if(!String.IsNullOrEmpty(captureDirectory)) Directory.CreateDirectory(captureDirectory);
        if(client) {
            int port=ResolvePeerPort(online,Environment.GetCommandLineArgs());
            peer=new IPEndPoint(IPAddress.Parse(RedboolPvp.HostAddress),port);
        }
        for(int i=0;i<2;i++)
        {
            views[i]=game.Players[i].GetComponent<Fighter3DView>();
            punchSurfaces[i]=views[i].visualRoot.GetComponentInChildren<HeroPunchSurfaceVisibility>(true);
            for(int b=0;b<bones[i].Length;b++) {bones[i][b]=views[i].modelAnimator.GetBoneTransform((HumanBodyBones)b);if(bones[i][b]) replicaBoneCount++;}
        }
        // Visual setup is one-time. Retrying a local socket must not clone mirrors,
        // reset practice, or reopen the game's camera-input receivers.
        if(client) {mirror=gameObject.AddComponent<RedboolViewMirror>();mirror.Initialize(game,views,bones);Time.timeScale=1;}
        initialized=true;
        TryOpenSocket(Time.realtimeSinceStartupAsDouble);
    }
    void TryOpenSocket(double now)
    {
        if(online)return;
        if(!initialized || socket!=null || now<nextBindAttempt)return;
        nextBindAttempt=now+1; // unscaled: the match is intentionally paused while disconnected
        UdpClient candidate=null;
        try
        {
            candidate=new UdpClient(AddressFamily.InterNetwork);
            candidate.ExclusiveAddressUse=true;
            candidate.Client.Bind(new IPEndPoint(IPAddress.Any,client?0:RedboolViewProtocol.Port));
            RedboolSocketOwnership.PreventInheritance(candidate.Client,"UDP display "+candidate.Client.LocalEndPoint);
            candidate.Client.Blocking=false;candidate.Client.ReceiveBufferSize=128*1024;
            bool recovered=!string.IsNullOrEmpty(BindError);
            socket=candidate;candidate=null;BindError=null;PortConflict=false;
            // Opening a socket is not evidence of a connected peer. No previous
            // lease, ACK or snapshot can authorize play after local recovery.
            ack=0;lastHeartbeat=lastFrame=-10;nextSend=now;
            assembler.Reset();history.Clear();mirror?.ClearTransient();
            selectionRequest=null;
            if(!client) {peer=null;peerNonce=Guid.Empty;lastHello=-10;}
            Debug.Log("[REDBOOL VIEW] "+(recovered?"local channel recovered ":"")+(client?"guest "+peer:"host :5067")+" protocol="+RedboolViewProtocol.Version);
        }
        catch(Exception e) when(e is SocketException || e is System.ComponentModel.Win32Exception || e is System.Security.SecurityException)
        {
            string error=e.GetType().Name+": "+e.Message;
            if(BindError!=error)Debug.LogWarning("[REDBOOL VIEW] Cannot open local display channel; retrying: "+error);
            BindError=error;PortConflict=!client &&e is SocketException se &&se.SocketErrorCode==SocketError.AddressAlreadyInUse;
        }
        finally {candidate?.Close();}
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
        if(peer==null || !Bound) return;
        var bytes=RedboolViewProtocol.Encode(packet);
        if(online) {if(!OnlineOwnerValid)return;onlineSession.Send(2,bytes);sentBytes+=bytes.Length;AuditPacket(packet,bytes);return;}
        try {socket.Send(bytes,bytes.Length,peer);sentBytes+=bytes.Length;AuditPacket(packet,bytes);}
        // macOS may report EHOSTDOWN while the selected peer cannot be resolved.
        // Keep normal retries; failed sends must never refresh a received lease.
        catch(SocketException e) when(e.SocketErrorCode==SocketError.WouldBlock || e.SocketErrorCode==SocketError.ConnectionReset || e.SocketErrorCode==SocketError.NetworkUnreachable || e.SocketErrorCode==SocketError.HostUnreachable || e.SocketErrorCode==SocketError.HostDown) { }
    }
    void ReceiveOnline(byte channel,byte[] data)
    {
        if(!online || !OnlineOwnerValid || channel!=2 || data==null || data.Length>RedboolViewProtocol.MaxBytes)return;
        if(onlineInbox.Count>=128)onlineInbox.Dequeue();
        onlineInbox.Enqueue(data);
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
            if(online)
            {
                if(onlineInbox.Count==0)break;
                data=onlineInbox.Dequeue();from=new IPEndPoint(IPAddress.Loopback,RedboolViewProtocol.Port);
            }
            else try {data=socket.Receive(ref from);}
            catch(SocketException e) when(e.SocketErrorCode==SocketError.WouldBlock || e.SocketErrorCode==SocketError.ConnectionReset) {break;}
            // A camera-free LAN check must never pair a player, reset practice,
            // or steal the display peer. Echo only a small bounded nonce.
            if(!online && !client && data.Length==48 && Encoding.ASCII.GetString(data,0,16)=="REDBOOL_PING_V1:") {
                string token=Encoding.ASCII.GetString(data,16,32);
                if(Guid.TryParseExact(token,"N",out _)) {
                    byte[] reply=Encoding.ASCII.GetBytes("REDBOOL_PONG_V1:"+token);
                    socket.Send(reply,reply.Length,from);
                }
                continue;
            }
            if(!online && !client && Encoding.ASCII.GetString(data)=="REDBOOL_DISCOVER_V1") {
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
                    selectionRequest=null;
                }
                var complete=assembler.Push(p,now);
                if(complete!=null)
                {
                    if(online) {if(onlineArrival>=0)onlineJitter=onlineJitter*.9+Math.Abs(now-onlineArrival-1.0/30)*.1;onlineArrival=now;}
                    received++;lastFrame=now;TraceFrame(complete);RedboolFrameMetrics.NoteLiveInput();history.Add(complete);if(history.Count>8) history.RemoveAt(0);
                    if(complete.seq%30==0) Debug.Log($"[REDBOOL VIEW APPLIED] session={complete.session:N} seq={complete.seq} round={complete.world.round} resolved={complete.world.resolved}");
                    if(p.echo>0 && now>=p.echo) RttMs=(now-p.echo)*1000;
                    ack=complete.seq;
                    ObserveSelectionReply(complete);
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
            else if(p.kind==6) ApplySelectionPacket(p,from);
            else if(p.kind==4 && peer!=null && from.Equals(peer) &&p.nonce==peerNonce &&p.session==session &&p.seq<=sequence &&p.seq>=ack &&p.seq>=minimumReadySequence)
            {ack=p.seq;lastHeartbeat=lastHello=now;echo=p.time;}
        }
    }
    public void RequestPracticeSkip()
    {
        if(!client ||!Fresh ||assembler.Latest==null) return;
        var a=assembler.Latest.actors[1];if(a.step>=8)return;
        Send(new RedboolViewPacket {kind=5,session=session,nonce=nonce,seq=ack,time=Time.realtimeSinceStartupAsDouble,practiceRun=a.practiceRun,practiceStep=(byte)a.step});
    }
    // The sender never provides a player slot: the connected guest owns only P2.
    bool ApplySelectionPacket(RedboolViewPacket packet,IPEndPoint from)
    {
        if(client||packet==null||packet.kind!=6||game==null||peer==null||!peer.Equals(from)
            ||packet.nonce!=peerNonce||packet.session!=session||!PeerReady
            ||packet.seq>sequence||sequence-packet.seq>8||packet.selectionRound!=game.Round)return false;
        return game.Lobby!=null&&game.Lobby.ApplySelection(1,packet.selectionRun,packet.selectionRevision,packet.selectionCharacter,packet.selectionLocked);
    }
    public bool RequestSelection(int character,bool confirmed)
    {
        var frame=assembler.Latest;
        if(!client||!Fresh||SelectionPending||frame==null||frame.world.stage!=1
            ||frame.world.selectionRun==Guid.Empty||character<0||character>=RedboolPvpRoster.Count
            ||RedboolPauseMenu.BlocksStoryInput)return false;
        int cursor=frame.world.cursor1;bool locked=(frame.world.locked&2)!=0;
        if((locked||confirmed)&&character!=cursor)return false;
        if(cursor==character&&locked==confirmed)return true;
        selectionRequest=new RedboolViewPacket {kind=6,session=session,nonce=nonce,
            selectionRun=frame.world.selectionRun,selectionRound=frame.world.round,
            selectionRevision=frame.world.selectionRevision1,selectionCharacter=(byte)character,selectionLocked=confirmed};
        double now=Time.realtimeSinceStartupAsDouble;selectionRequestUntil=now+1.2;nextSelectionSend=now;
        SendSelectionRequest(now);return true;
    }
    void ObserveSelectionReply(RedboolViewFrame frame)
    {
        if(selectionRequest==null)return;
        var request=selectionRequest;var world=frame.world;
        // A new person/round/selection invalidates the old click rather than
        // silently rebasing an earlier confirmation onto a new character.
        if(frame.session!=request.session||world.stage!=1||world.selectionRun!=request.selectionRun
            ||world.round!=request.selectionRound||world.selectionRevision1!=request.selectionRevision
            ||world.cursor1==request.selectionCharacter&&((world.locked&2)!=0)==request.selectionLocked)
            selectionRequest=null;
    }
    void SendSelectionRequest(double now)
    {
        if(selectionRequest==null)return;
        if(!Fresh||now>=selectionRequestUntil||RedboolPauseMenu.BlocksStoryInput)
        {selectionRequest=null;return;}
        if(now<nextSelectionSend)return;
        nextSelectionSend=now+.12;
        selectionRequest.seq=ack;selectionRequest.time=now;Send(selectionRequest);
    }
    void Update()
    {
        if(!initialized || !OnlineOwnerValid) {CancelOnlineAnticipation();return;}
        double now=Time.realtimeSinceStartupAsDouble;
        if(online && !Bound) {CancelOnlineAnticipation();lastFrame=lastHeartbeat=-10;if(client) {Time.timeScale=0;ShowRemoteMeasured(null,false);}return;}
        if(!online && socket==null) {
            TryOpenSocket(now);
            if(socket==null) {if(client) {Time.timeScale=0;ShowRemoteMeasured(null,false);}return;}
        }
        if(client) {frameUiTicks=0;long start=TimingNow;Receive(now);frameReceiveTicks=TimingNow-start;}
        else Receive(now);
        if(client && now>=nextSend)
        {
            nextSend=now+.05;
            Send(new RedboolViewPacket {kind=0,session=Guid.Empty,nonce=nonce,time=now});
            if(ack>0 && mirror.Ready && game.CanAcknowledgeView(assembler.Latest)) Send(new RedboolViewPacket {kind=4,session=session,nonce=nonce,seq=ack,time=now});
        }
        if(client)SendSelectionRequest(now);
        if(client) {Time.timeScale=Fresh && assembler.Latest!=null && (!assembler.Latest.world.paused ||assembler.Latest.world.resolved)?1:0;if(!Fresh) ShowRemoteMeasured(assembler.Latest,false);}
        if(now-lastReport>=5)
        {
            Debug.Log(FormattableString.Invariant($"[REDBOOL VIEW METRIC] client={client} seq={(client?ack:sequence)} complete={received} invalid={invalid} bytes={sentBytes} fresh={(client?Fresh:PeerReady)} rtt_ms={RttMs:F2} route={(online?"photon":"lan")} interpolation_ms={InterpolationMs:F2} photon_rtt_ms={(onlineSession!=null?onlineSession.RttMs:0)}"));
            ReportGuestTiming();lastReport=now;sentBytes=0;
        }
    }
    void LateUpdate()
    {
        if(!Bound || !OnlineOwnerValid) {CancelOnlineAnticipation();return;}
        double now=Time.realtimeSinceStartupAsDouble;
        if(!String.IsNullOrEmpty(captureDirectory) &&game.Fighting &&now-lastCapture>8 &&captureCount<3)
        {lastCapture=now;ScreenCapture.CaptureScreenshot(Path.Combine(captureDirectory,(client?"guest":"host")+(captureCount++)+".png"));}
        if(client)
        {
            if(!Fresh || history.Count==0) {if(online)mirror.GetComponent<RedboolOnlineAnticipation>()?.Cancel();SampleGuest(0);return;}
            var latest=history[history.Count-1];double target=latest.time+now-latest.arrival-InterpolationMs/1000.0;
            var a=history[0];var b=a;
            foreach(var frame in history) {b=frame;if(frame.time>=target) break;a=frame;}
            double span=b.time-a.time;
            float t=a==b||span<=0?1:Mathf.Clamp01((float)((target-a.time)/span));
            // A different avatar needs its own rig before applying the first pose.
            // Do not interpolate body/bone layouts across roster or round changes.
            if(!CanBlendSnapshots(a,b)) {a=b;t=1;}
            for(int i=0;i<2;i++) if(game.Characters[i]!=b.actors[i].character) game.ApplyCharacter(i,b.actors[i].character);
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
    void CancelOnlineAnticipation() {if(online && mirror!=null)mirror.GetComponent<RedboolOnlineAnticipation>()?.Cancel();}
    public static bool CanBlendSnapshots(RedboolViewFrame a,RedboolViewFrame b)
    {
        return a!=null && b!=null && a.Complete && b.Complete && a.session==b.session
            && a.world.selectionRun==b.world.selectionRun && a.world.round==b.world.round && a.world.paused==b.world.paused && a.world.stage==b.world.stage
            && a.actors[0].character==b.actors[0].character && a.actors[1].character==b.actors[1].character;
    }
    /// <summary>인물을 바꾼 뒤 그 겉모습의 뼈대를 다시 잡는다 (호스트는 보낼 뼈대, 손님은 움직일 뼈대).</summary>
    public void Rebind(int i)
    {
        if(!initialized ||i<0||i>=views.Length) return;
        views[i]=game.Players[i].GetComponent<Fighter3DView>();
        punchSurfaces[i]=views[i].visualRoot.GetComponentInChildren<HeroPunchSurfaceVisibility>(true);   // 적 인물의 겉모습에는 없을 수 있다(null)
        for(int b=0;b<bones[i].Length;b++) bones[i][b]=views[i].modelAnimator.GetBoneTransform((HumanBodyBones)b);
        replicaBoneCount=0;foreach(var rig in bones) foreach(var bone in rig) if(bone) replicaBoneCount++;
        if(mirror!=null) mirror.Rebind(i);
    }
    RedboolViewActor CaptureActor(int i)
    {
        var p=game.Players[i];var h=p.GetComponent<Health>();var s=p.GetComponent<SpecialMove>();var practice=game.Practices[i];
        var a=new RedboolViewActor {root=p.transform.position,visual=views[i].visualRoot.position,rotation=views[i].visualRoot.rotation,scale=views[i].visualRoot.localScale,hips=bones[i][0]?bones[i][0].localPosition:Vector3.zero,hp=h.CurrentHealth,maxHp=h.MaxHealth,charge=s.Charge,maxCharge=s.HitsToCharge,phase=(int)s.CurrentPhase,step=practice.Step,hits=practice.Hits,practiceRun=Guid.Parse(practice.Run),live=game.Receivers[i].LiveReady,attackSerial=p.GetComponent<Fighter>().AttackSerial};
        for(int b=0;b<a.bones.Length;b++) a.bones[b]=bones[i][b]?bones[i][b].localRotation:Quaternion.identity;
        a.chargeLevel=s.VisualChargeLevel;
        a.feedback=(byte)game.Receivers[i].FeedbackCode;
        a.punchSurface=punchSurfaces[i]!=null?punchSurfaces[i].CurrentSurface:(byte)0;
        // 적 인물을 골랐으면 게이지와 발동 신호는 그 인물의 기술에서 가져온다.
        var own=p.GetComponent<EnemySpecial>();
        if(own!=null && own.enabled && own.PlayerOwned) {a.charge=own.IsPlaying?own.HitsToCharge:own.Charge;a.maxCharge=own.HitsToCharge;a.phase=0;a.chargeLevel=0;a.specialSerial=(byte)(own.FireSerial&255);a.specialFlags=(byte)((own.IsPlaying?1:0)|(own.GuardDecided?2:0)|(own.GuardedSpecial?4:0));}
        string status=game.Receivers[i].PersonalStatus;a.personal=(byte)(status.Contains("PERSONAL RANGE")?2:status.Contains("CYCLES")?1:0);
        var renderer=views[i].visualRoot.GetComponentInChildren<Renderer>();if(renderer) {var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);a.tint=block.GetColor("_BaseColor");}
        a.character=(byte)game.Characters[i];
        return a;
    }
    RedboolViewWorld CaptureWorld()
    {
        var cam=Camera.main;
        // 필살기가 카메라를 뒤로 물린 만큼(QiCameraFx.Pull)도 보낸다. 그 효과는 그리는 순간에만 더해져 transform 에는 없다.
        QiCameraFx.EffectivePose(cam,out var cameraPosition,out var cameraFov);
        var world=new RedboolViewWorld {round=game.Round,gate=game.Gate,countdown=game.Countdown,remaining=game.Remaining,paused=game.Paused,resolved=game.Resolved,fighting=game.Fighting,winner=game.Result.StartsWith("1P")?1:game.Result.StartsWith("2P")?2:0,camera=cameraPosition,rotation=cam.transform.rotation,fov=cameraFov};
        foreach(var wave in FindObjectsByType<QiWave>(FindObjectsSortMode.None)) if(wave.isActiveAndEnabled && !wave.Finished &&world.waves.Count<8) world.waves.Add(new RedboolViewWave {id=wave.GetInstanceID(),point=wave.transform.position,rotation=wave.transform.rotation});
        effects.RemoveAll(x=>Time.realtimeSinceStartupAsDouble-x.time>1);world.effects.AddRange(effects);
        var lobby=game.Lobby;
        if(lobby!=null) {world.stage=(byte)lobby.Current;world.cursor0=(byte)lobby.Cursor[0];world.cursor1=(byte)lobby.Cursor[1];world.locked=(byte)((lobby.Locked[0]?1:0)|(lobby.Locked[1]?2:0));world.selectSeconds=(byte)Mathf.Clamp(Mathf.CeilToInt(lobby.SelectRemaining),0,60);
            world.selectionRun=lobby.SelectionRun;world.selectionRevision0=lobby.SelectionRevision[0];world.selectionRevision1=lobby.SelectionRevision[1];}
        return world;
    }
    void OnDestroy() {initialized=false;if(onlineSession!=null)onlineSession.Message-=ReceiveOnline;onlineInbox.Clear();socket?.Close();socket=null;trace?.Dispose();trace=null;}
}
