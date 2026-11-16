using System;
using System.Linq;
using System.Reflection;
#if REDBOOL_PHOTON_CONTRACT_TEST
using Photon.Client;
using Photon.Realtime;
#endif

static class RedboolPhotonTests
{
    static int assertions;
    static void Check(bool ok, string name) { assertions++; if (!ok) throw new Exception(name); }
    static void Reject(Action action, string name)
    { bool threw = false; try { action(); } catch (ArgumentException) { threw = true; } Check(threw, name); }
    public static int Main()
    {
        Check(RedboolPhotonWire.TryNormalizeRoomCode(" kr-234abc ", out var code) && code == "KR-234ABC", "normalize");
        foreach (var bad in new[] { "", "KR-000000", "KR-ABCDEFx", "JP-ABCDEF", "KR-ABC DE", "KR-ＡBCDEF", "ABCDEF", null })
            Check(!RedboolPhotonWire.TryNormalizeRoomCode(bad, out _), "invalid code");
        for (int i = 0; i < 256; i++) Check(RedboolPhotonWire.TryNormalizeRoomCode(RedboolPhotonWire.NewRoomCode(), out _), "generated code");
        var epoch = RedboolPhotonWire.NewEpoch();
        Check(RedboolPhotonWire.TryReadEpoch(RedboolPhotonWire.EpochText(epoch), out var epochCopy) && epoch.SequenceEqual(epochCopy), "epoch roundtrip");
        Check(!RedboolPhotonWire.TryReadEpoch(new string('Z', 32), out _), "bad hex");
        foreach (int size in new[] { 1, 626, 960, 1500, 8192 })
        {
            var source = Enumerable.Range(0, size).Select(x => (byte)x).ToArray();
            var wire = RedboolPhotonWire.Encode(255, 17, epoch, source);
            Check(RedboolPhotonWire.TryDecode(wire, epoch, out var channel, out var seq, out var payload) && channel == 255 && seq == 17 && payload.SequenceEqual(source), "payload roundtrip");
            Check(!RedboolPhotonWire.TryDecode(wire, RedboolPhotonWire.NewEpoch(), out _, out _, out _), "old room epoch");
            wire[27] ^= 1;
            Check(!RedboolPhotonWire.TryDecode(wire, epoch, out _, out _, out _), "length corruption");
        }
        Reject(() => RedboolPhotonWire.Encode(1, 1, epoch, new byte[8193]), "oversize send");
        Reject(() => RedboolPhotonWire.Encode(1, 1, epoch, new byte[0]), "empty send");
        var zero = RedboolPhotonWire.Encode(1, 0, epoch, new byte[] { 1 });
        Check(!RedboolPhotonWire.TryDecode(zero, epoch, out _, out _, out _), "zero sequence");
        var good = RedboolPhotonWire.Encode(2, 1, epoch, new byte[] { 1 });
        for (int i = 0; i < good.Length; i++)
            Check(!RedboolPhotonWire.TryDecode(good.Take(i).ToArray(), epoch, out _, out _, out _), "truncation");
        var random = new Random(777);
        for (int i = 0; i < 1000; i++)
        { var bytes = new byte[random.Next(9000)]; random.NextBytes(bytes); Check(!RedboolPhotonWire.TryDecode(bytes, epoch, out _, out _, out _), "malformed fuzz"); }
        var gate = new RedboolPhotonSequenceGate();
        Check(gate.Accept(2, 8) && gate.Accept(2, 6) && gate.Accept(2, 7), "multipart reordering allowed");
        Check(!gate.Accept(2, 6) && gate.Accept(1, 6), "duplicate/channel separation");
        Check(gate.Accept(1, uint.MaxValue) && gate.Accept(1, 1), "wrap accepted");
        gate.Clear(); Check(gate.Accept(2, 6), "new room clears dedup");
        for (uint i = 1; i <= 513; i++) gate.Accept(9, i);
        Check(gate.Accept(9, 1), "bounded dedup permits evicted sequence");
#if REDBOOL_PHOTON_CONTRACT_TEST
        Lifecycle();
        AnonymousIdentity();
        CancelledJoinRetry();
#endif
        Console.WriteLine("PASS: " + assertions + " assertions. No Photon Cloud connection was attempted.");
        return 0;
    }
#if REDBOOL_PHOTON_CONTRACT_TEST
    static void Call(RedboolPhotonSession s, string method) => typeof(RedboolPhotonSession).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(s, null);
    static RedboolPhotonSession NewSession()
    { var session = new RedboolPhotonSession(); Call(session, "Awake"); return session; }
    static void AnonymousIdentity()
    {
        var s = NewSession();
        s.JoinRoom("KR-234ABC"); Call(s, "Update");
        string first = RealtimeClient.Last.AuthValues.UserId;
        Check(Guid.TryParseExact(first, "N", out var id) && id != Guid.Empty, "anonymous identity is a nonempty GUID");
        s.Leave(); Call(s, "Update");
        s.JoinRoom("KR-234ABC"); Call(s, "Update");
        Check(RealtimeClient.Last.AuthValues.UserId == first, "cancelled join retry retains anonymous identity");
        Call(s, "Update"); Check(s.InRoom, "retry can still join normally");
        s.Leave(); Call(s, "Update"); Call(s, "OnDestroy");
        s = NewSession(); s.JoinRoom("KR-234ABC"); Call(s, "Update");
        Check(RealtimeClient.Last.AuthValues.UserId != first, "distinct Session instance has a distinct anonymous identity");
        s.Leave(); Call(s, "Update"); Call(s, "OnDestroy");
    }
    static void StartBlockedJoin(RedboolPhotonSession s, string code = "KR-234ABC")
    {
        s.JoinRoom(code); Call(s, "Update");
        RealtimeClient.Last.Callbacks.Clear();
        s.OnConnectedToMaster(); RealtimeClient.Last.Callbacks.Clear();
    }
    static void CancelledJoinRetry()
    {
        UnityEngine.Time.realtimeSinceStartup = 100;
        var s = NewSession(); int entered = 0; s.RoomEntered += () => entered++;
        StartBlockedJoin(s); s.Leave(); Call(s, "Update");
        StartBlockedJoin(s); s.OnJoinRoomFailed(ErrorCode.GameFull, "full");
        Check(!s.InRoom && s.Error.Length == 0, "same-room cancelled reservation permits bounded retry");
        UnityEngine.Time.realtimeSinceStartup += .5f; Call(s, "Update");
        Check(!s.InRoom, "retry does not busy-loop before interval");
        UnityEngine.Time.realtimeSinceStartup += .6f; Call(s, "Update"); Call(s, "Update");
        Check(s.InRoom && entered == 1, "same-room capacity retry can enter exactly once");
        s.Leave(); Call(s, "Update"); Call(s, "OnDestroy");

        s = NewSession(); StartBlockedJoin(s);
        s.OnJoinRoomFailed(ErrorCode.GameFull, "full");
        Check(s.Error.Length != 0 && !s.InRoom, "ordinary full room fails without cancellation retry");
        Call(s, "Update"); Call(s, "OnDestroy");

        s = NewSession(); StartBlockedJoin(s); s.Leave(); Call(s, "Update");
        StartBlockedJoin(s, "KR-345BCD"); s.OnJoinRoomFailed(ErrorCode.GameFull, "full");
        Check(s.Error.Length != 0, "different full room gets no cancellation grace period");
        Call(s, "Update"); Call(s, "OnDestroy");

        s = NewSession(); StartBlockedJoin(s); s.Leave(); Call(s, "Update");
        StartBlockedJoin(s); s.OnJoinRoomFailed(ErrorCode.GameFull, "full");
        s.Leave(); s.OnJoinedRoom();
        UnityEngine.Time.realtimeSinceStartup += 2f; Call(s, "Update");
        Check(!s.InRoom && RealtimeClient.Last.Target == null, "cancel during retry wait suppresses late join and disconnects");
        Call(s, "OnDestroy");

        s = NewSession(); StartBlockedJoin(s); s.Leave(); Call(s, "Update");
        UnityEngine.Time.realtimeSinceStartup += 26f;
        StartBlockedJoin(s); s.OnJoinRoomFailed(ErrorCode.GameFull, "full");
        Check(s.Error.Length != 0, "expired cancellation window fails immediately");
        Call(s, "Update"); Call(s, "OnDestroy");

        s = NewSession(); StartBlockedJoin(s); s.Leave(); Call(s, "Update");
        StartBlockedJoin(s);
        for (int i = 0; i < 23; i++)
        {
            s.OnJoinRoomFailed(ErrorCode.GameFull, "full");
            Check(s.Error.Length == 0 && !s.InRoom, "retry budget remains bounded before final attempt");
            UnityEngine.Time.realtimeSinceStartup += 1.01f; Call(s, "Update");
            RealtimeClient.Last.Callbacks.Clear();
        }
        s.OnJoinRoomFailed(ErrorCode.GameFull, "full");
        Check(s.Error.Length != 0 && !s.InRoom, "retry count exhausted fails closed");
        Call(s, "Update"); Call(s, "OnDestroy");
    }
    static void Lifecycle()
    {
        Environment.SetEnvironmentVariable("REDBOOL_PHOTON_APP_ID", "11111111-1111-4111-8111-111111111111");
        var s = NewSession(); int entered = 0, left = 0, messages = 0;
        s.RoomEntered += () => entered++; s.RoomLeft += () => left++; s.Message += (c, p) => messages++;
        s.CreateRoom(); Call(s, "Update"); // starts connection only
        s.Leave(); s.OnJoinedRoom(); // deliberately late callback after cancel
        Call(s, "Update"); Check(!s.InRoom && entered == 0, "cancel rejects late join");
        s.CreateRoom(); Call(s, "Update"); Call(s, "Update");
        Check(s.InRoom && s.IsHost && !s.PeerPresent && entered == 1, "created room enters after host publication");
        var c = RealtimeClient.Last; Check(c.LastCreate.RoomOptions.IsVisible == false && c.LastCreate.RoomOptions.MaxPlayers == 2, "private two player room");
        Check(c.LastSettings.FixedRegion == "kr" && c.LastSettings.Protocol == ConnectionProtocol.Udp, "Seoul UDP");
        var peer = new Player { ActorNumber = 2 }; c.CurrentRoom.Players[2] = peer; s.OnPlayerEnteredRoom(peer);
        Check(s.PeerPresent && !c.CurrentRoom.IsOpen, "close room when peer joins");
        var epochText = (string)c.CurrentRoom.CustomProperties["roomEpoch"];
        RedboolPhotonWire.TryReadEpoch(epochText, out var epoch);
        Action<int, uint, byte[]> inbound = (actor, seq, ep) => s.OnEvent(new EventData { Code = 42, Sender = actor, CustomData = RedboolPhotonWire.Encode(2, seq, ep, new byte[] { 9 }) });
        inbound(3, 1, epoch); inbound(1, 1, epoch); inbound(2, 1, RedboolPhotonWire.NewEpoch());
        Check(messages == 0, "reject unknown actor, self and old epoch");
        inbound(2, 2, epoch); inbound(2, 1, epoch); inbound(2, 2, epoch);
        Check(messages == 2, "valid peer reordered messages, duplicate suppression");
        s.Send(2, new byte[] { 8 });
        Check(c.LastSend.DeliveryMode == DeliveryMode.UnreliableUnsequenced && c.LastSend.Encrypt && c.LastTargets[0] == 2, "unsequenced encrypted target actor");
        c.RealtimePeer.SentReliableCommandsCount = 9999;
        int raisedBefore = c.RaiseCount;
        s.Send(2, new byte[] { 9 });
        Check(c.RaiseCount == raisedBefore + 1 && s.RejectedOutgoingMessages == 0, "cumulative reliable count never blocks live sends");
        s.Send(1, new byte[8193]);
        Check(s.InRoom && s.RejectedOutgoingMessages == 1, "oversize session send is counted without throwing");
        c.RealtimePeer.QueuedOutgoingCommands = 64;
        s.Send(2, new byte[] { 8 });
        Check(s.InRoom && s.RejectedOutgoingMessages == 2, "live backpressure drops without queuing");
        c.RealtimePeer.QueuedOutgoingCommands = 0;
        s.OnMasterClientSwitched(peer); Check(!s.InRoom && left == 1 && !string.IsNullOrEmpty(s.Error), "no host takeover");
        Call(s, "Update"); s.Leave(); Call(s, "OnDestroy");

        s = NewSession(); entered = 0; s.RoomEntered += () => entered++;
        s.JoinRoom("KR-234ABC"); Call(s, "Update"); Call(s, "Update");
        Check(s.InRoom && !s.IsHost && s.PeerPresent && entered == 1 && RealtimeClient.Last.JoinOnly, "join existing host only");
        int previousEpoch = s.ConnectionEpoch;
        s.OnPlayerLeftRoom(new Player { ActorNumber = 1 });
        Check(!s.InRoom && s.ConnectionEpoch != previousEpoch, "host departure invalidates room");
        Call(s, "Update"); Call(s, "OnDestroy");

        s = NewSession(); s.CreateRoom(); Call(s, "Update"); Call(s, "Update");
        RealtimeClient.Last.CurrentRoom.CustomProperties["authoritativeHost"] = 99;
        s.OnRoomPropertiesUpdate(new PhotonHashtable());
        Check(!s.InRoom, "host property mutation fails closed");
        Call(s, "Update"); Call(s, "OnDestroy");

        s = NewSession(); s.JoinRoom("KR-234ABC"); Call(s, "Update"); Call(s, "Update");
        RealtimeClient.Last.RealtimePeer.ReliableCommandsInFlight = 64;
        s.Send(1, new byte[] { 1 }, true);
        Check(!s.InRoom && s.RejectedOutgoingMessages == 1, "reliable backpressure ends room");
        Call(s, "Update"); Call(s, "OnDestroy");

        s = NewSession(); s.JoinRoom("KR-234ABC"); Call(s, "Update"); Call(s, "Update");
        RealtimeClient.Last.RealtimePeer.QueuedIncomingCommands = 513;
        Call(s, "Update"); Check(!s.InRoom, "incoming overload ends room");
        Call(s, "Update"); Call(s, "OnDestroy");
    }
#endif
}
