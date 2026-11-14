// Target: feature/unity 55844cd (Unity 6000.4.5f1), pose-held camera guard.
// Calls the game's PlayerController, never bypasses Fighter/Health/animation.
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

[DefaultExecutionOrder(-100)]
public sealed class RedboolDemoReceiver : MonoBehaviour
{
    public int port = 5065;
    public string bindAddress = "0.0.0.0";
    public string expectedActorId = "DEV01";
    [Tooltip("Empty: pair on SYNC; reconnect to a new run only after 0.5 s silence.")]
    public string expectedStreamId = "";
    public PlayerController player;
    public RedboolBattlePreparation Preparation { get; set; }
    public bool PvpOwned;
    public Func<bool> ExternalGate;
    public Func<RedboolPracticeContext> PracticeContext;
    public event Action<string,float,string,float> PracticeAction;
    public event Action<bool,int> CalibrationObserved;
    public Fighter Opponent;
    public bool LiveReady => controlling && tutorialInputValid && NowNs()<deadline;
    // Match presence is separate from the input lease: a late camera packet must
    // release held controls immediately without cancelling an already-started attack.
    // Only PvP gets 100 ms beyond the original source deadline (at most 250 ms old).
    public bool PresenceReady => LiveReady || PvpOwned && NowNs()<presenceDeadline;
    public bool NeutralReady => LiveReady && !BlockActive && targetMove==0 && lastPhase=="READY";
    public int CalibrationGeneration { get; private set; }
    public string PersonalStatus { get; private set; } = "BASELINE V26";
    public string InputPhase => lastPhase??"NONE";
    public double InputAgeMs => lastLiveNs==0?-1:(NowNs()-lastLiveNs)/1000000.0;
    public double SourceAgeMs => sourceCaptureNs<=0?-1:(NowNs()-sourceCaptureNs)/1000000.0;
    public double LeaseRemainingMs => !hasState?-1:Math.Max(0,(deadline-NowNs())/1000000.0);
    public double ClockUncertaintyMs => !hasState?-1:lastUncertaintyNs/1000000.0;
    public string InputFreshnessReason => LiveReady ? "VALID" : !hasState ? "NO_STATE"
        : !lastCalibrated ? "CALIBRATION" : lastTracking!="VALID" ? "TRACKING" : "LEASE_EXPIRED";
    private string lastPhase;
    bool AllowsInput => (Preparation==null || Preparation.AllowsPlayer) && (ExternalGate==null || ExternalGate());
    bool Practicing => PracticeAction!=null || TutorialAction!=null;
    void NotifyPractice(string action,float confidence,string movement,float strength) {
        if(PvpOwned) PracticeAction?.Invoke(action,confidence,movement,strength);
        else TutorialAction?.Invoke(action,confidence,movement,strength);
    }
    private static readonly Dictionary<int, RedboolDemoReceiver> listeners = new Dictionary<int, RedboolDemoReceiver>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetListeners() { listeners.Clear(); TutorialAction = null; }
    public bool showStatus = true;
    public string LastGesture { get; private set; } = "NONE";
    public static event Action<string,float,string,float> TutorialAction;
    [Range(0f, .15f)] public float movementSmoothingSeconds = .06f;
    // Map a recognised light lean to a useful walking pace before smoothing.
    // Zero still means stop; timeout, preparation and collision rules are unchanged.
    public static float WalkStrength(float strength) {
        if (float.IsNaN(strength) || float.IsInfinity(strength) || strength <= 0) return 0;
        return Mathf.Lerp(.65f, 1f, Mathf.InverseLerp(.35f, 1f, strength));
    }
    public string Status { get; private set; } = "WAITING";
    public bool Bound => isActiveAndEnabled && running && udp!=null;
    public string BindError { get; private set; }
    public bool PortConflict { get; private set; }
    bool quietBindRetry;
    public bool RetryBinding()
    {
        if(Bound || !gameObject.activeInHierarchy || string.IsNullOrEmpty(BindError))return Bound;
        // The owner calls this at most once per second, never on a socket worker.
        quietBindRetry=true;
        try { enabled=true; } finally { quietBindRetry=false; }
        return Bound;
    }
    public int AttacksApplied { get; private set; }
    public int AttacksBusy { get; private set; }
    public int GuardsStarted { get; private set; }
    public int QueueDrops { get; private set; }
    public bool BlockActive { get; private set; }
    public string StreamId { get; private set; }
    private UdpClient udp;
    private Thread worker;
    private volatile bool running;
    private readonly ConcurrentQueue<Received> inbox = new ConcurrentQueue<Received>();
    private readonly Dictionary<string,long> syncs = new Dictionary<string,long>();
    private readonly Dictionary<string,string> outcomes = new Dictionary<string,string>();
    private readonly List<UdpActionReceiver> oldReceivers = new List<UdpActionReceiver>();
    private IPEndPoint pairedPeer;
    private long sequence, deadline, presenceDeadline, lastLiveNs;
    private float targetMove, currentMove;
    private bool controlling, tutorialInputValid, hasState, lastCalibrated;
    private string lastTracking;
    private long sourceCaptureNs, lastUncertaintyNs;
    private bool oldKeyboard;
    private PlayerController configuredPlayer;
    private EnemyAI diagnosticsEnemy;
    private string inputFeedback = "", feedbackEvent;
    private float feedbackUntil;
    private struct Received { public string json; public long time; public IPEndPoint peer; }
    [Serializable] private class Packet {
        public int version, ttl_ms, calibration_version, calibration_generation;
        public string personal_status;
        public string kind, stream_id, actor_id, capture_epoch, sync_id, event_id, side, movement, gesture, fsm_state, tracking;
        public long sequence, sent_ns, capture_ns, receiver_offset_ns, clock_uncertainty_ns, event_expires_ns;
        public bool block_active, calibrated;
        public float movement_strength;
    }
    [Serializable] private class Ack {
        public int version = 2;
        public string stream_id, status, event_id, sync_id, applied_action;
        public string gesture_mapping = "raised-kicks-v3";
        public string receiver_version = "unity-55844cd-v5";
        public string guard_mode = "held_pose_immediate_release";
        public string receiver_clock = "unity_stopwatch_ns";
        public long sequence, sender_ns, receiver_ns, applied_ns, ack_sent_ns;
        public bool game_attack_started;
        public bool game_can_act, game_guard_active, battle_preparing, special_ready;
        // Additive diagnostics only: never bypass or queue a busy action.
        public int diagnostics_version = 1;
        public bool game_attacking, game_hit_stunned, game_input_locked, game_preparation_locked,
            game_guard_recovering, game_dead, application_focused, game_special_active;
        public int game_health;
        public int movement_diagnostics_version = 1;
        public int game_player_facing, game_opponent_facing;
        public float game_player_x, game_opponent_x, game_velocity_x, game_move_input, game_walk_stride;
        public string game_walk_state;
        public float game_frame_ms, game_time_scale;
        public int calibration_contract = 1;
        public int guards_started;
        public string tutorial_run, tutorial_action, tutorial_phase;
        public int tutorial_hits;
        public string player_id;
    }
    private static long NowNs() {
        long t=Stopwatch.GetTimestamp(),f=Stopwatch.Frequency;
        return (t/f)*1000000000L + (long)((t%f)*(1000000000.0/f));
    }
    private void OnEnable() {
        RedboolDemoReceiver owner;
        if (listeners.TryGetValue(port, out owner) && owner != null && owner != this && owner.isActiveAndEnabled) {
            Status="DUPLICATE RECEIVER :"+port;enabled=false;return;
        }
        try {
            BindError=null;PortConflict=false;
            StreamId=expectedStreamId; sequence=deadline=presenceDeadline=lastLiveNs=0; pairedPeer=null;
            hasState=false;sourceCaptureNs=lastUncertaintyNs=0;
            syncs.Clear();outcomes.Clear();
            udp=new UdpClient(new IPEndPoint(IPAddress.Parse(bindAddress),port));
            RedboolSocketOwnership.PreventInheritance(udp.Client,"UDP "+bindAddress+":"+port);
            udp.Client.ReceiveTimeout=100;
            listeners[port]=this;
            running=true;var boundSocket=udp;worker=new Thread(()=>Receive(boundSocket)) { IsBackground=true };worker.Start();
            DisableLegacy();
            Status="WAITING FOR PYTHON :"+port;
            if(quietBindRetry)UnityEngine.Debug.Log("[REDBOOL RECEIVER] local port recovered :"+port);
        } catch(Exception e) {
            var native=e as System.ComponentModel.Win32Exception;var socketError=e as SocketException;
            string code=native==null?"":" (native "+native.NativeErrorCode+(socketError==null?"":", "+socketError.SocketErrorCode)+")";
            Status="SOCKET ERROR: "+bindAddress+":"+port+code+" "+e.Message;
            BindError=Status;PortConflict=socketError!=null&&socketError.SocketErrorCode==SocketError.AddressAlreadyInUse;
            running=false;udp?.Close();udp=null;
            if(!quietBindRetry)UnityEngine.Debug.LogError(Status);
            enabled=false;
        }
    }
    private void Start() { DisableLegacy(); }
    private void DisableLegacy() {
        // The old 5055 receiver otherwise releases/overwrites the same player.
        foreach(var old in FindObjectsByType<UdpActionReceiver>(FindObjectsSortMode.None))
            if(old.enabled) { oldReceivers.Add(old);old.enabled=false; }
    }
    private void Receive(UdpClient boundSocket) {
        while(running) {
            try {
                var peer=new IPEndPoint(IPAddress.Any,0);var bytes=boundSocket.Receive(ref peer);
                if(bytes.Length>8192) continue;
                if(inbox.Count>=128) { Received dropped;inbox.TryDequeue(out dropped);QueueDrops++; }
                inbox.Enqueue(new Received { json=Encoding.UTF8.GetString(bytes),time=NowNs(),peer=peer });
            } catch(SocketException) { if(!running) break; }
              catch(ObjectDisposedException) { break; }
        }
    }
    private void Update() {
        if(player==null && !PvpOwned) player=FindAnyObjectByType<PlayerController>();
        if(player!=null && configuredPlayer!=player) {
            if(configuredPlayer!=null) configuredPlayer.useKeyboard=oldKeyboard;
            configuredPlayer=player;oldKeyboard=player.useKeyboard;player.useKeyboard=false;
            diagnosticsEnemy=FindAnyObjectByType<EnemyAI>();
        }
        // Keyboard test mode (F8) gives the keys back without a camera; camera mode keeps them off.
        if(player!=null && player==configuredPlayer) player.useKeyboard=!PvpOwned && RedboolBattlePreparation.KeyboardTest;
        if(NowNs()>=deadline) Safe(preservePresence:true);
        Received item;
        for(int i=0;i<32 && inbox.TryDequeue(out item);i++) {
            try { Process(JsonUtility.FromJson<Packet>(item.json),item); }
            catch(Exception e) { Status="PACKET ERROR: "+e.GetType().Name;Safe(); }
        }
        if(controlling && player!=null) {
            float alpha=movementSmoothingSeconds<=0 ? 1 : 1-Mathf.Exp(-Time.unscaledDeltaTime/movementSmoothingSeconds);
            currentMove=Mathf.Lerp(currentMove,targetMove,alpha);
            if(Mathf.Abs(currentMove)<.01f && targetMove==0) currentMove=0;
            player.ExternalMovement(AllowsInput && !Practicing && (!PvpOwned || LiveReady) ? currentMove : 0);
            ApplyVisionGuard();
        }
    }
    private void ApplyVisionGuard()
    {
        var fighter=player!=null ? player.GetComponent<Fighter>() : null;
        if(fighter==null) return;
        bool wasHeld=fighter.IsVisionGuardHeld;
        bool held=controlling && BlockActive && AllowsInput && !Practicing && (!PvpOwned || LiveReady);
        if(fighter.SetVisionGuard(held) && !wasHeld) GuardsStarted++;
    }
    private void Process(Packet p,Received item) {
        if(p==null || p.version!=2 || p.actor_id!=expectedActorId || String.IsNullOrEmpty(p.stream_id)
            || p.stream_id.Length>100 || p.capture_epoch!=p.stream_id) return;
        long now=NowNs();
        bool oneShot=p.kind=="ATTACK" || p.kind=="GESTURE";
        if(p.kind=="SYNC" && !String.IsNullOrEmpty(p.sync_id) && p.sync_id.Length<=100) {
            bool newRun=p.stream_id!=StreamId;
            if(newRun && (String.IsNullOrEmpty(expectedStreamId) && now-lastLiveNs>500000000L)) {
                Safe();Preparation?.ResetPreparation();StreamId=p.stream_id;sequence=0;outcomes.Clear();syncs.Clear();pairedPeer=item.peer;
            }
            if(p.stream_id!=StreamId) return;
            if(pairedPeer==null) pairedPeer=item.peer;
            if(!pairedPeer.Equals(item.peer)) return;
            var expired=new List<string>();foreach(var kv in syncs) if(now-kv.Value>5000000000L) expired.Add(kv.Key);
            foreach(var key in expired) syncs.Remove(key);
            syncs[p.sync_id]=item.time;
            Reply(p,item,"SYNC",false);return;
        }
        if(p.stream_id!=StreamId || pairedPeer==null || !pairedPeer.Equals(item.peer)) return;
        string status;
        if(p.kind!="STATE" && !oneShot) status="BAD_KIND";
        else if(p.sync_id==null || !syncs.ContainsKey(p.sync_id) || now-syncs[p.sync_id]>5000000000L) status="UNSYNCHRONIZED";
        else if(p.ttl_ms<=0 || p.ttl_ms>150 || p.clock_uncertainty_ns<0 || p.clock_uncertainty_ns>25000000L) status="INVALID_TIMING";
        else if(now-(p.sent_ns+p.receiver_offset_ns)<-p.clock_uncertainty_ns
            || now-(p.sent_ns+p.receiver_offset_ns)+p.clock_uncertainty_ns>=p.ttl_ms*1000000L) status="EXPIRED";
        else if(oneShot && (p.event_expires_ns<=0 || now+p.clock_uncertainty_ns>=p.event_expires_ns+p.receiver_offset_ns)) status="EXPIRED";
        else if(oneShot && p.event_id!=null && outcomes.ContainsKey(p.event_id)) status=outcomes[p.event_id];
        else if(p.sequence<=sequence) status="OLD_SEQUENCE";
        else if(oneShot) {
            if(String.IsNullOrEmpty(p.event_id) || p.event_id.Length>200 || (p.side!="LEFT" && p.side!="RIGHT" && !(p.kind=="GESTURE" && p.gesture=="SPECIAL" && p.side=="BOTH"))) status="BAD_EVENT";
            else if(outcomes.Count>=10000) status="RESTART_REQUIRED";
            else {
                sequence=p.sequence;lastLiveNs=now;
                string action=ResolveAction(p);
                LastGesture=p.kind=="GESTURE" ? p.gesture : p.side+"_PUNCH";
                Fighter fighter=player!=null ? player.GetComponent<Fighter>() : null;
                if(action==null) status="BAD_GESTURE";
                else if(action=="UNASSIGNED") status="UNASSIGNED";
                else if(Practicing && (!tutorialInputValid || now>=deadline)) status="INPUT_LOCKED";
                else if(Practicing) {
                    NotifyPractice(action,1f,"NEUTRAL",0f);status="TUTORIAL_APPLIED";
                }
                else if(player==null || fighter==null) status="NO_PLAYER";
                else if(!AllowsInput || (PvpOwned && !LiveReady)) status="INPUT_LOCKED";
                else if(!fighter.CanAttack || BlockActive) { status="GAME_BUSY";AttacksBusy++; }
                else if(action=="SPECIAL") {
                    var special=player.GetComponent<SpecialMove>();
                    status=special==null ? "NO_SPECIAL" : !special.IsReady ? "NOT_CHARGED"
                        : special.Activate() ? "APPLIED" : "GAME_BUSY";
                    if(status=="APPLIED") AttacksApplied++;
                    else if(status=="GAME_BUSY") AttacksBusy++;
                }
                else {
                    player.ExternalAction(action);
                    status=fighter.IsAttacking ? "APPLIED" : "GAME_BUSY";
                    if(status=="APPLIED") { AttacksApplied++; } else AttacksBusy++;
                }
                outcomes[p.event_id]=status;
            }
        } else if((p.movement!="LEFT" && p.movement!="RIGHT" && p.movement!="NEUTRAL")
            || Single.IsNaN(p.movement_strength) || Single.IsInfinity(p.movement_strength)
            || p.movement_strength<0 || p.movement_strength>1) status="BAD_STATE";
        else {
            sequence=p.sequence;lastLiveNs=now;
            deadline=p.sent_ns+p.receiver_offset_ns+p.ttl_ms*1000000L-p.clock_uncertainty_ns;
            hasState=true;lastCalibrated=p.calibration_version==1 && p.calibrated;lastTracking=p.tracking;
            sourceCaptureNs=p.capture_ns>0?p.capture_ns+p.receiver_offset_ns:0;
            lastUncertaintyNs=p.clock_uncertainty_ns;
            tutorialInputValid=lastCalibrated && p.tracking=="VALID";
            presenceDeadline=tutorialInputValid ? deadline+100000000L : 0;
            Preparation?.Observe(p.calibration_version==1 && p.calibrated,p.tracking,p.fsm_state,p.block_active,p.movement);
            CalibrationGeneration=p.calibration_generation;
            CalibrationObserved?.Invoke(p.calibration_version==1 && p.calibrated,p.calibration_generation);
            lastPhase=p.fsm_state;
            PersonalStatus=(p.personal_status??"BASELINE V26");
            if(PersonalStatus.Length>100) PersonalStatus=PersonalStatus.Substring(0,100);
            BlockActive=p.block_active;targetMove=(p.movement=="LEFT"?-1:p.movement=="RIGHT"?1:0)*WalkStrength(p.movement_strength);
            controlling=true;
            if(p.calibration_version==1 && p.calibrated && (p.tracking=="VALID" || p.tracking=="SYNTHETIC"))
                RedboolFrameMetrics.NoteLiveInput();
            // Release before processing any following one-shot in the same inbox batch.
            ApplyVisionGuard();
            // An attack candidate/return phase is still live camera input. It must
            // not clear tutorial readiness before the following one-shot event.
            string tutorialState=!tutorialInputValid ? "UNAVAILABLE"
                : p.block_active ? "BLOCK" : p.fsm_state=="READY" ? "IDLE" : "BUSY";
            NotifyPractice(tutorialState,1f,p.movement,p.movement_strength);
            status=player!=null?"APPLIED":Practicing?"TUTORIAL_APPLIED":"NO_PLAYER";
        }
        if(oneShot || status!="APPLIED") Status=status;
        Reply(p,item,status,oneShot && status=="APPLIED");
    }
    private string ResolveAction(Packet p) {
        return RedboolGestureMap.Resolve(p.kind,p.gesture,p.side);
    }
    private void Reply(Packet p,Received item,string status,bool attack) {
        Fighter fighter=player!=null ? player.GetComponent<Fighter>() : null;
        var body=player!=null ? player.GetComponent<Rigidbody2D>() : null;
        var view=player!=null ? player.GetComponent<Fighter3DView>() : null;
        var opponent=Opponent!=null ? Opponent : diagnosticsEnemy!=null ? diagnosticsEnemy.GetComponent<Fighter>() : null;
        var teaching=PracticeContext!=null ? PracticeContext() : CaveTutorial.TeachingContext;
        if(!String.IsNullOrEmpty(p.event_id) && p.event_id!=feedbackEvent) {
            feedbackEvent=p.event_id;feedbackUntil=Time.unscaledTime+1.5f;
            inputFeedback=status=="APPLIED" ? "동작 인식 · 기술 실행"
                : status=="NOT_CHARGED" ? "동작 인식 · 필살기 충전 필요"
                : status=="GAME_BUSY" && fighter!=null && fighter.Health.IsDead ? "동작 인식 · 전투 종료"
                : status=="GAME_BUSY" && fighter!=null && fighter.Health.IsHitStunned ? "동작 인식 · 피격 중에는 공격 불가"
                : status=="GAME_BUSY" ? "동작 인식 · 현재 기술 또는 전투 잠금 중"
                : "입력 처리: "+status;
        }
        var ack=new Ack { player_id=expectedActorId,
            tutorial_run=teaching?.run, tutorial_action=teaching?.action, tutorial_phase=teaching?.phase, tutorial_hits=teaching!=null?teaching.hits:0,
            stream_id=StreamId,status=status,event_id=p.event_id,sequence=p.sequence,
            sync_id=p.kind=="SYNC"?p.sync_id:null,sender_ns=p.sent_ns,receiver_ns=item.time,
            applied_ns=NowNs(),game_attack_started=attack,applied_action=attack?ResolveAction(p):null,ack_sent_ns=NowNs(),
            battle_preparing=!AllowsInput || Preparation!=null && Preparation.HoldRound,
            special_ready=player!=null && player.GetComponent<SpecialMove>()!=null && player.GetComponent<SpecialMove>().IsReady,
            // Wire contract: ready to start another attack, not merely free to move.
            game_can_act=AllowsInput && !Practicing && fighter!=null && fighter.CanAttack,game_guard_active=fighter!=null && fighter.IsGuarding,
            guards_started=GuardsStarted,
            game_attacking=fighter!=null && fighter.IsAttacking,
            game_special_active=fighter!=null && fighter.IsSpecialActive,
            game_hit_stunned=fighter!=null && fighter.Health!=null && fighter.Health.IsHitStunned,
            game_input_locked=fighter!=null && fighter.InputLocked,
            game_preparation_locked=fighter!=null && fighter.VisionPreparationLocked,
            game_guard_recovering=false, // hold-based guard has no failure delay
            game_dead=fighter!=null && fighter.Health!=null && fighter.Health.IsDead,
            game_health=fighter!=null && fighter.Health!=null ? fighter.Health.CurrentHealth : -1,
            application_focused=Application.isFocused, game_frame_ms=Time.unscaledDeltaTime*1000f,
            game_time_scale=Time.timeScale,
            game_player_facing=fighter!=null ? fighter.Facing : 0,
            game_opponent_facing=opponent!=null ? opponent.Facing : 0,
            game_player_x=player!=null ? player.transform.position.x : 0,
            game_opponent_x=opponent!=null ? opponent.transform.position.x : 0,
            game_velocity_x=body!=null ? body.linearVelocity.x : 0,
            game_move_input=currentMove,
            game_walk_stride=view!=null && view.Walk!=null ? view.Walk.StepLength : 0,
            game_walk_state=view!=null ? view.CurrentState : "" };
        byte[] bytes=Encoding.UTF8.GetBytes(JsonUtility.ToJson(ack));udp.Send(bytes,bytes.Length,item.peer);
    }
    public void ClearInput() { Safe(); }
    private void Safe(bool preservePresence=false) {
        if(!preservePresence) {presenceDeadline=0;hasState=false;sourceCaptureNs=0;}
        if(player!=null) player.GetComponent<Fighter>()?.SetVisionGuard(false);
        if(controlling && player!=null) player.ClearExternalInput();
        if(controlling) NotifyPractice("UNAVAILABLE",0f,"NEUTRAL",0f);
        controlling=false;tutorialInputValid=false;BlockActive=false;targetMove=currentMove=0;
    }
    private void OnGUI() {
        // Packaged play uses the camera panel and game HUD. Keep this technical
        // overlay available only in the existing manual development workflow.
        if(!showStatus || RedboolCameraRuntime.Instance!=null) return;
        GUI.Box(new Rect(12,12,470,130),"REDBOOL VISION :"+port);
        GUI.Label(new Rect(22,35,450,25),Status+" | "+LastGesture+" | attacks "+AttacksApplied+" | busy "+AttacksBusy);
        GUI.Label(new Rect(22,60,450,25),"block "+BlockActive+" | move "+currentMove.ToString("F2")+" | drops "+QueueDrops);
        var special=player!=null ? player.GetComponent<SpecialMove>() : null;
        if(special!=null) GUI.Label(new Rect(22,85,450,25),special.IsReady ? "SPECIAL READY: cross arms at chest for 0.7s" : "SPECIAL: charging");
        if(Time.unscaledTime<feedbackUntil) GUI.Label(new Rect(22,110,450,25),inputFeedback);
    }
    private void OnDisable() {
        Preparation?.ResetPreparation();
        Safe();running=false;udp?.Close();udp=null;
        RedboolDemoReceiver owner;
        if(listeners.TryGetValue(port,out owner) && owner==this) listeners.Remove(port);
        if(worker!=null) worker.Join(300);worker=null;
        Received discarded;while(inbox.TryDequeue(out discarded)) { }
        if(configuredPlayer!=null) configuredPlayer.useKeyboard=oldKeyboard;
        foreach(var old in oldReceivers) if(old!=null) old.enabled=true;
        oldReceivers.Clear();configuredPlayer=null;
    }
}
