using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

// Only online P2 uses this local gateway. LAN and P1 keep the original UDP route.
// SYNC and ACK always come from the real authoritative receiver, not this gateway.
[DefaultExecutionOrder(-400)]
public sealed class RedboolOnlineInputBridge : MonoBehaviour
{
    public const byte InputChannel = 1;
    public static RedboolOnlineInputBridge Instance { get; private set; }
    public static int LocalPort => Instance != null ? Instance.localPort : 0;
    public static event Action<string, string> LocalAction;
    public static event Action<string, string> ActionResult;
    [Serializable] public class Header
    {
        public int version, ttl_ms, calibration_generation;
        public string kind, stream_id, actor_id, capture_epoch, event_id, gesture, side, tracking, movement, fsm_state, personal_status;
        public long sequence, capture_ns, commit_ns, sent_ns, event_expires_ns;
        public bool block_active, calibrated;
        public float movement_strength;
    }
    [Serializable] public class Feedback
    {
        public string event_id, status, stream_id;
        public bool game_can_act, game_attacking, game_hit_stunned, game_input_locked, game_dead, battle_preparing, game_special_active, special_ready;
        public long sequence, sender_ns;
    }
    UdpClient local;
    IPEndPoint pythonPeer;
    readonly IPEndPoint logicalPeer = new IPEndPoint(IPAddress.Loopback, 5066);
    RedboolPhotonSession session;
    int localPort, epoch, lastRound = -1;
    string stream;
    Header lastForwarded, newestState;
    byte[] newestBytes, lastStateBytes;
    Feedback feedback;
    double feedbackAt = -10, stateAt = -10, packetAt = -10;
    long lastLocalSequence;
    double nextReport;
    public int Forwarded { get; private set; }
    public int Coalesced { get; private set; }
    public int Rejected { get; private set; }
    public long WireBytes { get; private set; }
    public static void Attach(RedboolPvp owner)
    {
        if (!RedboolPvp.Online || Instance != null) return;
        Instance = owner.gameObject.AddComponent<RedboolOnlineInputBridge>();
    }
    void Awake()
    {
        Instance = this; session = RedboolPhotonSession.Instance;
        if (session == null) { enabled = false; return; }
        epoch = session.ConnectionEpoch; session.Message += OnMessage;
        nextReport=Time.realtimeSinceStartupAsDouble+5;
        if (!session.IsHost)
        {
            local = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            RedboolSocketOwnership.PreventInheritance(local.Client, "Online camera local gateway");
            local.Client.Blocking = false;
            localPort = ((IPEndPoint)local.Client.LocalEndPoint).Port;
        }
    }
    bool Current => session != null && session.InRoom && session.ConnectionEpoch == epoch && RedboolPvp.Online;
    int Round => RedboolPvp.Instance != null ? RedboolPvp.Instance.Round : -1;
    static Header Read(byte[] data) { try { return JsonUtility.FromJson<Header>(Encoding.UTF8.GetString(data)); } catch { return null; } }
    void Update()
    {
        if (!Current) { newestBytes = null; feedback = null; return; }
        if(Time.realtimeSinceStartupAsDouble>=nextReport) {
            nextReport=Time.realtimeSinceStartupAsDouble+5;
            Debug.Log($"[REDBOOL ONLINE INPUT] host={session.IsHost} round={Round} forwarded={Forwarded} coalesced={Coalesced} rejected={Rejected} wire_bytes={WireBytes} photon_server_rtt_ms={session.RttMs}");
        }
        if (lastRound != Round)
        {
            lastRound = Round; newestBytes = lastStateBytes = null; newestState = lastForwarded = null; feedback = null;
            stateAt = feedbackAt = -10;
        }
        if (local == null) return;
        for (int i = 0; i < 128; i++)
        {
            var peer = new IPEndPoint(IPAddress.Any, 0); byte[] data;
            try { data = local.Receive(ref peer); }
            catch (SocketException e) when (e.SocketErrorCode == SocketError.WouldBlock || e.SocketErrorCode == SocketError.ConnectionReset) { break; }
            if (!IPAddress.IsLoopback(peer.Address) || data.Length > RedboolOnlineWire.MaxRaw) { Rejected++; continue; }
            var p = Read(data);
            if (p == null || p.version != 2 || p.actor_id != "P2" || string.IsNullOrEmpty(p.stream_id) || p.stream_id != p.capture_epoch) { Rejected++; continue; }
            if (p.kind == "SYNC" && (pythonPeer == null || peer.Equals(pythonPeer) || Time.realtimeSinceStartupAsDouble-packetAt>.5))
            {
                if (stream != p.stream_id) { stream = p.stream_id; lastLocalSequence = 0; lastForwarded = newestState = null; newestBytes = lastStateBytes = null; feedback = null; }
                pythonPeer = peer;
            }
            if (pythonPeer == null || !peer.Equals(pythonPeer) || p.stream_id != stream || p.sequence <= lastLocalSequence) { Rejected++; continue; }
            lastLocalSequence = p.sequence;packetAt=Time.realtimeSinceStartupAsDouble;
            if (p.kind == "STATE") { newestState = p; newestBytes = data; stateAt = Time.realtimeSinceStartupAsDouble; continue; }
            if (p.kind != "SYNC" && p.kind != "ATTACK" && p.kind != "GESTURE") { Rejected++; continue; }
            // Flush and bundle the exact latest source STATE with one-shots. One
            // datagram applies release before attack even when the relay reorders packets.
            FlushState();
            if (p.kind != "SYNC" && CanAnticipate(p)) LocalAction?.Invoke(p.event_id, p.kind == "GESTURE" ? p.gesture : p.side + "_PUNCH");
            Forward(data,p.kind=="SYNC"?null:lastStateBytes);
        }
        FlushState();
    }
    public static bool SameControl(Header a, Header b)
    {
        return a != null && b != null && a.stream_id == b.stream_id && a.capture_ns == b.capture_ns
            && a.calibration_generation == b.calibration_generation && a.calibrated == b.calibrated && a.tracking == b.tracking
            && a.block_active == b.block_active && a.movement == b.movement && a.movement_strength == b.movement_strength
            && a.fsm_state == b.fsm_state && a.personal_status == b.personal_status;
    }
    void FlushState()
    {
        if (newestBytes == null) return;
        if (SameControl(lastForwarded, newestState)) Coalesced++;
        else { Forward(newestBytes); lastForwarded = newestState; lastStateBytes=newestBytes; }
        newestBytes = null;
    }
    void Forward(byte[] data,byte[] state=null)
    {
        if (!Current || !session.PeerPresent || Round < 0) return;
        var packet = state==null?RedboolOnlineWire.Pack(data, Round):RedboolOnlineWire.PackInputs(state,data,Round);
        if (packet == null) { Rejected++; return; }
        session.Send(InputChannel, packet); Forwarded++;WireBytes+=packet.Length;
    }
    bool CanAnticipate(Header p)
    {
        double now = Time.realtimeSinceStartupAsDouble;
        var game = RedboolPvp.Instance; var frame = game != null && game.ViewLink != null ? game.ViewLink.Latest : null;
        return frame != null && game.ViewLink.Fresh && frame.world.fighting && !frame.world.paused && !frame.world.resolved
            && frame.world.round == Round && feedback != null && now - feedbackAt < .15 && feedback.game_can_act
            && feedback.sender_ns>0 && p.sent_ns>=feedback.sender_ns && p.sent_ns-feedback.sender_ns<=150000000L
            && !feedback.game_hit_stunned && !feedback.game_dead && !feedback.game_input_locked && !feedback.game_special_active && !feedback.battle_preparing
            && lastForwarded != null && now - stateAt < .12 && lastForwarded.calibrated && lastForwarded.tracking == "VALID"
            && !lastForwarded.block_active && p.calibration_generation == lastForwarded.calibration_generation
            && p.sent_ns >= p.commit_ns && p.sent_ns - p.commit_ns <= 50000000L && p.event_expires_ns > p.sent_ns
            && !string.IsNullOrEmpty(p.event_id);
    }
    void OnMessage(byte channel, byte[] packet)
    {
        if (channel != InputChannel || !Current || !RedboolOnlineWire.TryUnpackInputs(packet, Round, out var state,out var data)) return;
        if (session.IsHost)
        {
            var receiver = RedboolPvp.Instance != null ? RedboolPvp.Instance.Receivers[1] : null;
            if (receiver == null || !receiver.OnlineRemoteInput) return;
            var p = Read(data);
            if (p == null || p.actor_id != "P2") { Rejected++; return; }
            if(state!=null) {
                var s=Read(state);
                if(s==null || s.kind!="STATE" || s.actor_id!="P2" || s.stream_id!=p.stream_id
                    || s.calibration_generation!=p.calibration_generation || s.sequence>=p.sequence
                    || (p.kind!="ATTACK" && p.kind!="GESTURE")) {Rejected++;return;}
            }
            int receivedRound = Round, receivedEpoch = epoch;
            Func<bool> valid=()=>Current && Round==receivedRound && epoch==receivedEpoch;
            Action<byte[]> respond=reply =>
            {
                if (!Current || Round != receivedRound || epoch != receivedEpoch) return;
                var wrapped = RedboolOnlineWire.Pack(reply, Round);
                if (wrapped != null) session.Send(InputChannel, wrapped);
            };
            if(state!=null)receiver.InjectOnline(state,logicalPeer,valid,respond);
            receiver.InjectOnline(data, logicalPeer,valid,respond);
        }
        else if (local != null && pythonPeer != null)
        {
            if(state!=null) {Rejected++;return;}
            Feedback ack = null;
            try { ack = JsonUtility.FromJson<Feedback>(Encoding.UTF8.GetString(data)); } catch { }
            if (ack == null || ack.stream_id != stream) return;
            if (feedback == null || ack.sequence > feedback.sequence) { feedback = ack; feedbackAt = Time.realtimeSinceStartupAsDouble; }
            if (!string.IsNullOrEmpty(ack.event_id)) ActionResult?.Invoke(ack.event_id, ack.status);
            try { local.Send(data, data.Length, pythonPeer); } catch (SocketException) { Rejected++; }
        }
    }
    void OnDestroy()
    {
        if (session != null) session.Message -= OnMessage;
        local?.Close(); local = null;
        if (Instance == this) Instance = null;
    }
}
