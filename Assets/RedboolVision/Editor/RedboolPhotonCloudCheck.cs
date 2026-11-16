#if UNITY_EDITOR && PHOTON_REALTIME_5_OR_NEWER
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Photon.Client;
using Photon.Realtime;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Explicit edit-mode integration check against the real vendor SDK and Cloud.
/// No automatic startup, Play mode, camera, game scene or Photon test doubles.
/// Invoke with -executeMethod RedboolPhotonCloudCheck.Run (optionally -quit).
/// </summary>
public static class RedboolPhotonCloudCheck
{
    [Serializable] sealed class CheckResult { public string name; public bool passed; }
    [Serializable] sealed class CloudReport
    {
        public bool passed;
        public string scope = "Actual Session networking and Photon Cloud; two clients on one computer. Inactive edit-mode fixture initializes main-thread/singleton fields; Awake and native game lifecycle are excluded. No camera, inference, game loop or rendered frames.";
        public bool nativeAwakeExercised = false;
        public string sdkVersion, region = "kr", protocol = "Udp", error = "";
        public string timingScope = "One main-process stopwatch; manual 1 ms service loop. Relay delivery and echoed application RTT include local dispatch. Not two-device latency.";
        public double elapsedMs;
        public double cancelToRetryJoinedMs = -1;
        public List<string> cancellationStates = new List<string>();
        public int probesSent, probesReceivedByPeer, echoedWithin1000ms, echoedLate, missingEchoes;
        public double relayDeliveryMedianMs = -1, relayDeliveryP95Ms = -1, relayDeliveryMaxMs = -1;
        public double echoRttMedianMs = -1, echoRttP95Ms = -1, echoRttMaxMs = -1;
        public string serverRttScope = "SDK smoothed RTT to each client's Photon server, including server ACK behavior; distinct from relay delivery and echoed application RTT.";
        public double sessionServerRttMedianMs = -1, sessionServerRttP95Ms = -1, sessionServerRttMaxMs = -1;
        public double peerServerRttMedianMs = -1, peerServerRttP95Ms = -1, peerServerRttMaxMs = -1;
        public List<CheckResult> checks = new List<CheckResult>();
        public List<double> relayDeliverySamplesMs = new List<double>();
        public List<double> echoRttSamplesMs = new List<double>();
        public List<double> sessionServerRttSamplesMs = new List<double>();
        public List<double> peerServerRttSamplesMs = new List<double>();
    }

    // A blocking editor method must still dispatch captured async continuations.
    // Posts execute only in Pump(), on this same Unity main thread.
    sealed class PumpContext : SynchronizationContext
    {
        readonly ConcurrentQueue<Action> queue = new ConcurrentQueue<Action>();
        public override void Post(SendOrPostCallback callback, object state) => queue.Enqueue(() => callback(state));
        public override SynchronizationContext CreateCopy() => this;
        public void Drain() { for (int i = 0; i < 128 && queue.TryDequeue(out var action); i++) action(); }
    }

    sealed class RawPeer : IConnectionCallbacks, IMatchmakingCallbacks, IInRoomCallbacks, IOnEventCallback
    {
        public readonly RealtimeClient Client = new RealtimeClient();
        public bool Ready, Creating, ExpectedDisconnect;
        public string Error = "";
        public Action<byte, byte[]> Payload;
        public byte[] Epoch;
        uint sequence;
        public RawPeer()
        {
            WatchEditorCleanup(Client);
            Client.AddCallbackTarget(this);
            Client.AuthValues = new AuthenticationValues(Guid.NewGuid().ToString("N"));
        }
        public void Connect()
        {
            if (!Client.ConnectUsingSettings(new AppSettings {
                AppIdRealtime = RedboolPhotonSession.AppId, AppVersion = RedboolPhotonSession.ProtocolVersion,
                FixedRegion = "kr", UseNameServer = true, Protocol = ConnectionProtocol.Udp,
                AuthMode = AuthModeOption.AuthOnceWss, EnableProtocolFallback = false
            })) throw new InvalidOperationException("Raw peer ConnectUsingSettings returned false.");
        }
        public void Join(string code)
        {
            if (!Client.OpJoinRoom(new EnterRoomArgs { RoomName = code }))
                throw new InvalidOperationException("Raw peer join request failed.");
        }
        public string Create()
        {
            Creating = true; Epoch = RedboolPhotonWire.NewEpoch();
            var code = RedboolPhotonWire.NewRoomCode();
            if (!Client.OpCreateRoom(new EnterRoomArgs { RoomName = code, RoomOptions = new RoomOptions {
                IsVisible = false, IsOpen = false, MaxPlayers = 2, PlayerTtl = 0, EmptyRoomTtl = 0,
                BroadcastPropsChangeToAll = true, CleanupCacheOnLeave = true,
                CustomRoomProperties = new PhotonHashtable { { "authoritativeHost", 0 },
                    { "roomEpoch", RedboolPhotonWire.EpochText(Epoch) }, { "protocol", RedboolPhotonSession.ProtocolVersion } }
            } })) throw new InvalidOperationException("Raw host create request failed.");
            return code;
        }
        public void Send(byte channel, byte[] payload, bool reliable = false, byte[] epoch = null, uint? envelopeSequence = null)
        {
            int target = Client.CurrentRoom.Players.Keys.First(actor => actor != Client.LocalPlayer.ActorNumber);
            var bytes = RedboolPhotonWire.Encode(channel, envelopeSequence ?? ++sequence, epoch ?? Epoch, payload);
            if (!Client.OpRaiseEvent(42, bytes, new RaiseEventArgs { TargetActors = new[] { target }, CachingOption = EventCaching.DoNotCache },
                new SendOptions { DeliveryMode = reliable ? DeliveryMode.Reliable : DeliveryMode.UnreliableUnsequenced,
                    Channel = (byte)(reliable ? 1 : 0), Encrypt = true }))
                throw new InvalidOperationException("Raw peer event request failed.");
            Client.SendOutgoingCommands();
        }
        public void Service()
        {
            for (int i = 0; i < 128 && Client.DispatchIncomingCommands(); i++) { }
            for (int i = 0; i < 3 && Client.SendOutgoingCommands(); i++) { }
        }
        public void Disconnect() { ExpectedDisconnect = true; Client.Disconnect(); Client.SendOutgoingCommands(); }
        public void OnJoinedRoom()
        {
            if (Creating)
            {
                if (!Client.CurrentRoom.SetCustomProperties(new PhotonHashtable { { "authoritativeHost", Client.LocalPlayer.ActorNumber } }))
                    Error = "Raw host property publication failed.";
            }
            else
            {
                if (!RedboolPhotonWire.TryReadEpoch(Client.CurrentRoom.CustomProperties["roomEpoch"] as string, out Epoch))
                    Error = "Raw guest received an invalid epoch.";
                else Ready = true;
            }
        }
        public void OnRoomPropertiesUpdate(PhotonHashtable changed)
        {
            if (Creating && !Ready && Client.CurrentRoom.CustomProperties["authoritativeHost"] is int host
                && host == Client.LocalPlayer.ActorNumber)
            { Ready = true; Client.CurrentRoom.IsOpen = true; }
        }
        public void OnEvent(EventData ev)
        {
            if (ev.Code != 42 || !Client.InRoom || ev.Sender == Client.LocalPlayer.ActorNumber ||
                !Client.CurrentRoom.Players.ContainsKey(ev.Sender)) return;
            if (RedboolPhotonWire.TryDecode(ev.CustomData as byte[], Epoch, out byte channel, out _, out var payload))
                Payload?.Invoke(channel, payload);
        }
        public void OnDisconnected(DisconnectCause cause) { Ready = false; if (!ExpectedDisconnect) Error = "Raw peer disconnected: " + cause; }
        public void OnCreateRoomFailed(short code, string text) { Error = "Raw create failed: " + code; }
        public void OnJoinRoomFailed(short code, string text) { Error = "Raw join failed: " + code; }
        public void OnJoinRandomFailed(short code, string text) { Error = "Unexpected random join: " + code; }
        public void OnCustomAuthenticationFailed(string text) { Error = "Raw authentication failed."; }
        public void OnConnected() { } public void OnConnectedToMaster() { } public void OnCreatedRoom() { }
        public void OnLeftRoom() { Ready = false; } public void OnFriendListUpdate(List<FriendInfo> friends) { }
        public void OnRegionListReceived(RegionHandler regions) { } public void OnCustomAuthenticationResponse(Dictionary<string, object> data) { }
        public void OnPlayerEnteredRoom(Player player) { } public void OnPlayerLeftRoom(Player player) { }
        public void OnMasterClientSwitched(Player player) { } public void OnPlayerPropertiesUpdate(Player player, PhotonHashtable changed) { }
    }

    static RedboolPhotonSession session;
    static RawPeer peer;
    static CloudReport report;
    static PumpContext context;
    static readonly Stopwatch elapsed = new Stopwatch();
    static int entered, left;
    static readonly HashSet<RealtimeClient> watchedClients = new HashSet<RealtimeClient>();
    static readonly MethodInfo update = typeof(RedboolPhotonSession).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly FieldInfo clientField = typeof(RedboolPhotonSession).GetField("client", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly FieldInfo phaseField = typeof(RedboolPhotonSession).GetField("phase", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly FieldInfo mainThreadField = typeof(RedboolPhotonSession).GetField("mainThread", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly MethodInfo instanceSetter = typeof(RedboolPhotonSession).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public).GetSetMethod(true);
    static RealtimeClient SessionClient => clientField.GetValue(session) as RealtimeClient;
    static string Phase => phaseField.GetValue(session)?.ToString();

    public static void Run()
    {
        string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../verification/photon-cloud-check.json"));
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "-redboolPhotonReport") output = Path.GetFullPath(args[i + 1]);
        report = new CloudReport { sdkVersion = RealtimeClient.Version };
        elapsed.Restart(); entered = 0; left = 0; watchedClients.Clear();
        var originalContext = SynchronizationContext.Current;
        context = new PumpContext(); SynchronizationContext.SetSynchronizationContext(context);
        Exception failure = null;
        try
        {
            Check(!Application.isPlaying, "edit mode; no game/camera startup");
            Check(Guid.TryParse(RedboolPhotonSession.AppId, out var id) && id != Guid.Empty, "local App ID configured");
            ConnectionHandler.AppQuits = false;
            Check(RedboolPhotonSession.Instance == null, "no existing Session is replaced by the fixture");
            // Awake calls DontDestroyOnLoad, which is valid only in Play mode.
            // This edit-mode integration check deliberately excludes that lifecycle:
            // keep the object inactive and initialize only the two transport prerequisites.
            var fixture = new GameObject("Redbool Photon Cloud Check (inactive fixture)");
            fixture.SetActive(false);
            session = fixture.AddComponent<RedboolPhotonSession>();
            mainThreadField.SetValue(session, Thread.CurrentThread.ManagedThreadId);
            instanceSetter.Invoke(null, new object[] { session });
            session.RoomEntered += Entered; session.RoomLeft += Left;
            HostRound();
            GuestRound();
            Check(entered == 2, "exactly the two intended rooms entered");
            Check(left == 2, "each entered room left once");
            report.passed = true;
        }
        catch (Exception ex)
        {
            failure = ex is TargetInvocationException invocation && invocation.InnerException != null ? invocation.InnerException : ex;
            report.error = Redact(failure.GetType().Name + ": " + failure.Message);
        }
        finally
        {
            try
            {
                peer?.Disconnect(); session?.Leave();
                var until = elapsed.ElapsedMilliseconds + 1000;
                while (elapsed.ElapsedMilliseconds < until) { context.Drain(); peer?.Service(); if (session != null) { WatchEditorCleanup(SessionClient); update.Invoke(session, null); WatchEditorCleanup(SessionClient); } Thread.Sleep(1); }
            }
            catch (Exception cleanup) { if (report.error.Length == 0) { report.error = "Cleanup: " + cleanup.GetType().Name; report.passed = false; } }
            finally
            {
                foreach (var client in watchedClients) CleanupEditorHandler(client);
                if (session != null)
                {
                    session.RoomEntered -= Entered; session.RoomLeft -= Left;
                    instanceSetter.Invoke(null, new object[] { null });
                    UnityEngine.Object.DestroyImmediate(session.gameObject);
                }
            }
            SynchronizationContext.SetSynchronizationContext(originalContext);
            report.elapsedMs = elapsed.Elapsed.TotalMilliseconds;
            FillTiming();
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, JsonUtility.ToJson(report, true) + "\n");
            UnityEngine.Debug.Log("REDBOOL_PHOTON_CLOUD_CHECK " + (report.passed ? "PASS" : "FAIL") + " " + output);
            peer = null; session = null;
        }
        if (failure != null) throw new InvalidOperationException(report.error);
    }

    static void HostRound()
    {
        session.CreateRoom();
        Until(() => session.InRoom || session.Error.Length != 0, 30, "session host creation");
        Check(session.InRoom && session.IsHost && !session.PeerPresent, "Session creates a waiting host room");
        var room = SessionClient.CurrentRoom;
        Check(!room.IsVisible && room.MaxPlayers == 2 && room.IsOpen, "private room opens after host publication");
        Check(RedboolPhotonWire.TryNormalizeRoomCode(session.RoomCode, out _), "valid generated KR room code");
        Check(SessionClient.CurrentRegion.StartsWith("kr", StringComparison.Ordinal) &&
            SessionClient.RealtimePeer.TransportProtocol == ConnectionProtocol.Udp, "Session connected to kr over UDP");
        Check(SessionClient.RealtimePeer.IsEncryptionAvailable, "payload encryption established");
        ConnectPeer(); peer.Join(session.RoomCode);
        Until(() => peer.Ready && session.PeerPresent || peer.Error.Length != 0, 25, "guest joins session room");
        Check(peer.Ready && session.PeerPresent, "real second Cloud client joins");
        Until(() => !room.IsOpen, 3, "host closes full room");
        Check(room.Players.Count == 2 && !room.IsOpen, "two-player room closes after join");

        var sentAt = new Dictionary<int, double>();
        var received = new HashSet<int>(); var echoed = new HashSet<int>();
        int invalidEpochDelivered = 0; var reordered = new HashSet<byte>();
        Action<byte, byte[]> message = (channel, payload) => {
            if (channel == 91)
            {
                int sequence = ValidatePayload(payload);
                Check(sentAt.ContainsKey(sequence), "echo belongs to an actual sent probe");
                if (!echoed.Add(sequence)) return;
                double delay = elapsed.Elapsed.TotalMilliseconds - sentAt[sequence];
                if (delay <= 1000) { report.echoedWithin1000ms++; report.echoRttSamplesMs.Add(delay); }
                else report.echoedLate++;
            }
            else if (channel == 92) invalidEpochDelivered++;
            else if (channel == 93) reordered.Add(payload[0]);
        };
        session.Message += message;
        peer.Payload = (channel, payload) => {
            if (channel != 90) return;
            int sequence = ValidatePayload(payload);
            Check(sentAt.ContainsKey(sequence), "peer receives an actual sent probe");
            if (received.Add(sequence)) report.relayDeliverySamplesMs.Add(elapsed.Elapsed.TotalMilliseconds - sentAt[sequence]);
            peer.Send(91, payload);
        };
        try
        {
            for (int i = 0; i < 24; i++)
            {
                report.sessionServerRttSamplesMs.Add(SessionClient.RealtimePeer.Stats.RoundtripTime);
                report.peerServerRttSamplesMs.Add(peer.Client.RealtimePeer.Stats.RoundtripTime);
                sentAt[i] = elapsed.Elapsed.TotalMilliseconds; session.Send(90, Payload(i)); report.probesSent++; PumpFor(50);
            }
            PumpFor(1000);
            report.probesReceivedByPeer = received.Count;
            report.missingEchoes = report.probesSent - echoed.Count;
            Check(received.Count > 0 && report.echoedWithin1000ms > 0, "bidirectional unreliable encrypted Cloud relay within observation bound");
            int rejected = session.RejectedOutgoingMessages;
            session.Send(1, new byte[8193]);
            Check(session.InRoom && session.RejectedOutgoingMessages == rejected + 1, "oversize payload rejected without disconnect");
            peer.Send(92, new byte[] { 1 }, true, RedboolPhotonWire.NewEpoch());
            peer.Send(93, new byte[] { 10 }, true, envelopeSequence: 10);
            peer.Send(93, new byte[] { 8 }, true, envelopeSequence: 8);
            peer.Send(93, new byte[] { 9 }, true, envelopeSequence: 9);
            Until(() => reordered.Count == 3, 3, "unordered envelope sequences");
            PumpFor(150);
            Check(invalidEpochDelivered == 0 && reordered.SetEquals(new byte[] { 8, 9, 10 }), "old-room epoch rejected; unseen reordered envelopes accepted");
        }
        finally { session.Message -= message; peer.Payload = null; }
        peer.Disconnect();
        Until(() => !session.InRoom, 8, "guest departure ends host room");
        Check(!session.IsHost && !session.PeerPresent && left == 1, "guest departure invalidates Session room");
        Until(() => SessionClient == null, 5, "host disconnect cleanup");
    }

    static void GuestRound()
    {
        ConnectPeer(); string roomCode = peer.Create();
        Until(() => peer.Ready || peer.Error.Length != 0, 25, "raw host creation");
        Check(peer.Ready, "real raw host publishes compatible room identity");
        session.JoinRoom(roomCode);
        Until(() => Phase == "Joining" || session.InRoom || session.Error.Length != 0, 25, "in-flight guest join");
        Check(Phase == "Joining" && !session.InRoom, "cancel occurs while join is in flight");
        string cancelledUserId = SessionClient.AuthValues.UserId;
        double cancelledAt = elapsed.Elapsed.TotalMilliseconds;
        SnapshotCancellation("before cancel");
        session.Leave(); PumpFor(1500);
        SnapshotCancellation("after cancel cleanup");
        Until(() => SessionClient == null, 5, "cancelled join disconnect cleanup");
        Check(!session.InRoom && entered == 1, "cancelled join never enters the room");
        session.JoinRoom(roomCode);
        Until(() => SessionClient != null, 5, "retry starts a fresh transport connection");
        Check(SessionClient.AuthValues.UserId == cancelledUserId, "cancel retry retains the same anonymous participant identity");
        string lastState = "";
        double deadline = elapsed.Elapsed.TotalSeconds + 25;
        while (!session.InRoom && session.Error.Length == 0 && elapsed.Elapsed.TotalSeconds < deadline)
        {
            string state = Phase + ":" + SessionClient?.State + ":" + SessionClient?.Server;
            if (state != lastState) { SnapshotCancellation("retry " + state); lastState = state; }
            Pump();
        }
        SnapshotCancellation("retry result");
        if (session.InRoom) report.cancelToRetryJoinedMs = elapsed.Elapsed.TotalMilliseconds - cancelledAt;
        Check(session.InRoom && !session.IsHost && session.PeerPresent, "Session joins existing raw host");
        // Each client receives its own server event asynchronously. Guest OnJoinedRoom
        // can precede the raw host's OnPlayerEnteredRoom even in this same process.
        Until(() => peer.Client.CurrentRoom.Players.Count == 2, 3, "raw host observes joined guest");
        Check(peer.Client.CurrentRoom.Players.Count == 2, "both clients observe the joined room before payload tests");
        int down = 0, up = 0;
        Action<byte, byte[]> message = (channel, payload) => { if (channel == 94 && payload.SequenceEqual(new byte[] { 4, 5, 6 })) down++; };
        session.Message += message;
        peer.Payload = (channel, payload) => { if (channel == 95 && payload.SequenceEqual(new byte[] { 7, 8, 9 })) up++; };
        try
        {
            peer.Send(94, new byte[] { 4, 5, 6 }, true); session.Send(95, new byte[] { 7, 8, 9 }, true);
            Until(() => down == 1 && up == 1, 3, "guest bidirectional payload");
            Check(down == 1 && up == 1, "Session guest routes valid peer payloads both ways");
        }
        finally { session.Message -= message; peer.Payload = null; }
        int before = session.ConnectionEpoch;
        peer.Disconnect(); Until(() => !session.InRoom, 8, "host departure");
        Check(!session.IsHost && session.ConnectionEpoch != before, "host departure ends room without authority takeover");
        Until(() => SessionClient == null, 5, "guest disconnect cleanup");
        session.JoinRoom(RedboolPhotonWire.NewRoomCode());
        Until(() => session.Error.Length != 0, 25, "missing room rejection");
        Check(!session.InRoom && entered == 2, "join-existing does not create a missing room");
        session.Leave(); Until(() => SessionClient == null, 5, "missing room cleanup");
    }

    static void SnapshotCancellation(string label)
    {
        var room = peer?.Client.CurrentRoom;
        var actors = room == null ? "none" : string.Join(",", room.Players.Values.Select(p => p.ActorNumber + (p.IsInactive ? " inactive" : " active")));
        report.cancellationStates.Add(elapsed.Elapsed.TotalMilliseconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) +
            "ms " + label + "; session=" + Phase + "/" + SessionClient?.State + "/" + SessionClient?.Server +
            "; host=" + peer?.Client.State + "/" + peer?.Client.Server + "; actors=" + actors +
            "; expectedCount=" + (room?.ExpectedUsers?.Length ?? 0) + "; open=" + room?.IsOpen + "; error=" + session.Error);
    }

    static void ConnectPeer()
    {
        peer = new RawPeer(); peer.Connect();
        Until(() => peer.Client.IsConnectedAndReady && peer.Client.Server == ServerConnection.MasterServer || peer.Error.Length != 0, 25, "raw peer authentication");
        Check(peer.Error.Length == 0 && peer.Client.Server == ServerConnection.MasterServer, "raw peer reaches kr master");
    }
    static void Entered() { entered++; }
    static void Left() { left++; }
    static void WatchEditorCleanup(RealtimeClient client)
    {
        if (client == null || !watchedClients.Add(client)) return;
        // SDK OnStatusChanged stops the fallback timer, sets State=Disconnected,
        // then its callback container calls Handler.RemoveInstance() (Destroy).
        // This edit-mode fixture substitutes only native component destruction,
        // through the public StateChanged hook before that callback. Transport,
        // status handling and application callbacks remain the real SDK paths.
        client.StateChanged += (previous, current) => {
            if (current == ClientState.Disconnected) CleanupEditorHandler(client);
        };
    }
    static void CleanupEditorHandler(RealtimeClient client)
    {
        var handler = client.Handler;
        if (handler == null) return;
        handler.StopFallbackSendAckThread();
        UnityEngine.Object.DestroyImmediate(handler);
        // Keep the managed Handler reference: vendor terminal-state code invokes
        // its idempotent managed Stop method, while Unity's null comparison prevents
        // a second native Destroy from RemoveInstance. No vendor source is changed.
    }
    static void Pump()
    {
        if (elapsed.Elapsed.TotalSeconds > 120) throw new TimeoutException("Cloud check exceeded its total 120 second budget.");
        context.Drain(); WatchEditorCleanup(SessionClient); update.Invoke(session, null); WatchEditorCleanup(SessionClient);
        peer?.Service(); context.Drain(); Thread.Sleep(1);
    }
    static void PumpFor(int milliseconds)
    { double until = elapsed.Elapsed.TotalMilliseconds + milliseconds; while (elapsed.Elapsed.TotalMilliseconds < until) Pump(); }
    static void Until(Func<bool> condition, double seconds, string operation)
    {
        double until = elapsed.Elapsed.TotalSeconds + seconds;
        while (!condition())
        {
            if (elapsed.Elapsed.TotalSeconds > until)
                throw new TimeoutException(operation + "; Session=" + session.Status + "; error=" + session.Error + "; peer=" + peer?.Error);
            Pump();
        }
    }
    static void Check(bool passed, string name)
    { report.checks.Add(new CheckResult { name = name, passed = passed }); if (!passed) throw new InvalidOperationException(name); }
    static byte[] Payload(int sequence)
    { var bytes = new byte[256]; Buffer.BlockCopy(BitConverter.GetBytes(sequence), 0, bytes, 0, 4); for (int i = 4; i < bytes.Length; i++) bytes[i] = (byte)(sequence + i); return bytes; }
    static int ValidatePayload(byte[] bytes)
    {
        if (bytes == null || bytes.Length != 256) throw new InvalidOperationException("Probe payload size changed.");
        int sequence = BitConverter.ToInt32(bytes, 0);
        if (!bytes.SequenceEqual(Payload(sequence))) throw new InvalidOperationException("Probe payload bytes changed.");
        return sequence;
    }
    static void FillTiming()
    {
        double Percentile(List<double> values, double percentile) { var sorted = values.OrderBy(v => v).ToArray(); return sorted.Length == 0 ? -1 : sorted[(int)Math.Ceiling((sorted.Length - 1) * percentile)]; }
        report.relayDeliveryMedianMs = Percentile(report.relayDeliverySamplesMs, .5); report.relayDeliveryP95Ms = Percentile(report.relayDeliverySamplesMs, .95); report.relayDeliveryMaxMs = Percentile(report.relayDeliverySamplesMs, 1);
        report.echoRttMedianMs = Percentile(report.echoRttSamplesMs, .5); report.echoRttP95Ms = Percentile(report.echoRttSamplesMs, .95); report.echoRttMaxMs = Percentile(report.echoRttSamplesMs, 1);
        report.sessionServerRttMedianMs = Percentile(report.sessionServerRttSamplesMs, .5); report.sessionServerRttP95Ms = Percentile(report.sessionServerRttSamplesMs, .95); report.sessionServerRttMaxMs = Percentile(report.sessionServerRttSamplesMs, 1);
        report.peerServerRttMedianMs = Percentile(report.peerServerRttSamplesMs, .5); report.peerServerRttP95Ms = Percentile(report.peerServerRttSamplesMs, .95); report.peerServerRttMaxMs = Percentile(report.peerServerRttSamplesMs, 1);
    }
    static string Redact(string text) { var id = RedboolPhotonSession.AppId; return string.IsNullOrEmpty(id) ? text : text.Replace(id, "[App ID]"); }
}
#endif
