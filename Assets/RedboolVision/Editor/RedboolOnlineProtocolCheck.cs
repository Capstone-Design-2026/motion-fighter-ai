using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Actual production receiver Process/Reply checks in Edit mode. Inactive objects
/// never bind UDP, start a game, open a camera, or connect to Photon. Synthetic
/// timestamps test timing gates; they do not measure real network/render latency.
/// </summary>
public static class RedboolOnlineProtocolCheck
{
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    const BindingFlags Public = BindingFlags.Public | BindingFlags.Instance;
    const long Ms = 1000000L;
    static readonly List<string> checks = new List<string>();
    static readonly Type ReceiverType = typeof(RedboolDemoReceiver);
    static readonly Type PacketType = ReceiverType.GetNestedType("Packet", BindingFlags.NonPublic);
    static readonly Type ReceivedType = ReceiverType.GetNestedType("Received", BindingFlags.NonPublic);
    static readonly MethodInfo Process = ReceiverType.GetMethod("Process", Private);
    static long Now => (long)ReceiverType.GetMethod("NowNs", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);

    [Serializable] sealed class Packet
    {
        public int version = 2, ttl_ms = 150, calibration_version = 1, calibration_generation = 1;
        public string kind = "STATE", stream_id = "online-editor-check", actor_id = "P2", capture_epoch = "online-editor-check";
        public string sync_id = "editor-clock", event_id, side = "LEFT", gesture, movement = "NEUTRAL", tracking = "VALID", fsm_state = "READY";
        public long sequence, sent_ns, capture_ns, commit_ns, receiver_offset_ns, clock_uncertainty_ns = Ms, event_expires_ns;
        public bool block_active, calibrated = true;
        public float movement_strength;
    }
    [Serializable] sealed class Ack { public string status, event_id; public long sequence, receiver_ns, applied_ns, ack_sent_ns; }
    [Serializable] sealed class Report
    {
        public bool passed;
        public int assertions;
        public string scope = "Actual production Unity receiver on inactive Edit-mode objects plus production view codec/assembler with synthetic arrivals; no UDP, Photon, camera, gameplay or rendered latency measurement";
        public string error;
        public string[] checks;
    }

    sealed class Fixture : IDisposable
    {
        public readonly GameObject Object;
        public readonly RedboolDemoReceiver Receiver;
        public Ack Last;
        public int Replies, Events;
        readonly IPEndPoint peer = new IPEndPoint(IPAddress.Loopback, 45066);
        public Fixture(bool online)
        {
            Object = new GameObject("Inactive online protocol fixture"); Object.SetActive(false);
            Receiver = Object.AddComponent<RedboolDemoReceiver>();
            Receiver.OnlineRemoteInput = online; Receiver.PvpOwned = true; Receiver.expectedActorId = "P2";
            Receiver.PracticeContext = () => null;
            Receiver.PracticeAction += (action, confidence, movement, strength) => { if (action == "JAB") Events++; };
            Check(Get("udp") == null && !Receiver.Bound, "Inactive receiver has no UDP socket");
            var sync = State(1); sync.kind = "SYNC";
            Check(Apply(sync) == "SYNC", "Real receiver returns a timestamped SYNC through callback");
            Check(Last.receiver_ns > 0 && Last.ack_sent_ns >= Last.receiver_ns, "SYNC preserves receiver clock timestamps");
        }
        public object Get(string name) => ReceiverType.GetField(name, Private).GetValue(Receiver);
        public string Apply(Packet packet, Func<bool> valid = null)
            => ApplyBytes(Encoding.UTF8.GetBytes(JsonUtility.ToJson(packet)), valid);
        public string ApplyBytes(byte[] bytes, Func<bool> valid = null)
        {
            Last = null;
            string json = Encoding.UTF8.GetString(bytes);
            var decoded = JsonUtility.FromJson(json, PacketType);
            object received = Activator.CreateInstance(ReceivedType);
            ReceivedType.GetField("json", Public).SetValue(received, json);
            ReceivedType.GetField("time", Public).SetValue(received, Now);
            ReceivedType.GetField("peer", Public).SetValue(received, peer);
            ReceivedType.GetField("valid", Public).SetValue(received, valid ?? (() => true));
            ReceivedType.GetField("reply", Public).SetValue(received, (Action<byte[]>)(bytes =>
            { Replies++; Last = JsonUtility.FromJson<Ack>(Encoding.UTF8.GetString(bytes)); }));
            Process.Invoke(Receiver, new[] { decoded, received });
            return Last != null ? Last.status : null;
        }
        public void Dispose()
        {
            Check(Get("udp") == null, "Receiver check never opened a UDP socket");
            UnityEngine.Object.DestroyImmediate(Object);
        }
    }

    static void Check(bool ok, string reason)
    { checks.Add((ok ? "PASS " : "FAIL ") + reason); if (!ok) throw new InvalidOperationException(reason); }
    static Packet State(long sequence, int generation = 1, long ageMs = 10)
    {
        long now = Now;
        return new Packet { sequence = sequence, sent_ns = now, capture_ns = now - ageMs * Ms, calibration_generation = generation };
    }
    static Packet Event(long sequence, int generation = 1)
    {
        long now = Now;
        return new Packet { kind = "ATTACK", sequence = sequence, event_id = Guid.NewGuid().ToString("N"),
            sent_ns = now, capture_ns = now - 2 * Ms, commit_ns = now - Ms,
            event_expires_ns = now - Ms + 150 * Ms, calibration_generation = generation };
    }
    static RedboolViewPacket[] ViewPackets(ulong sequence)
    {
        var session = new Guid("eb3bc963-0d6c-481d-8bf9-8e8d7498fa05");
        var actor = new RedboolViewActor { scale = Vector3.one, rotation = Quaternion.identity, hp = 100, maxHp = 100 };
        for (int i = 0; i < actor.bones.Length; i++) actor.bones[i] = Quaternion.identity;
        var packets = new[] {
            new RedboolViewPacket {kind=1,seq=sequence,session=session,time=sequence/30.0,
                world=new RedboolViewWorld {round=3,remaining=120,fov=60,rotation=Quaternion.identity}},
            new RedboolViewPacket {kind=2,seq=sequence,session=session,time=sequence/30.0,actor=actor},
            new RedboolViewPacket {kind=3,seq=sequence,session=session,time=sequence/30.0,actor=actor}
        };
        for (int i = 0; i < packets.Length; i++)
        {
            byte[] encoded = RedboolViewProtocol.Encode(packets[i]);
            Check(encoded.Length <= RedboolViewProtocol.MaxBytes && RedboolViewProtocol.TryDecode(encoded, out var decoded),
                "Real display packet stays within codec budget and decodes");
            RedboolViewProtocol.TryDecode(encoded, out packets[i]);
        }
        return packets;
    }
    static void ViewAndCoalescing()
    {
        var assembler = new RedboolViewAssembler(); var packets = ViewPackets(20);
        Check(assembler.Push(packets[2], .100) == null && assembler.Push(packets[0], .120) == null,
            "Reordered display fragments cannot expose a partial world");
        var complete = assembler.Push(packets[1], .150);
        Check(complete != null && complete.Complete && complete.seq == 20,
            "Delayed reordered display fragments form one exact production snapshot");
        Check(assembler.Push(packets[1], .160) == null, "Duplicate display fragment cannot replay a completed snapshot");
        var lost = ViewPackets(21); assembler.Push(lost[0], .170); assembler.Push(lost[2], .175);
        var newer = ViewPackets(22); assembler.Push(newer[0], .180); assembler.Push(newer[1], .190);
        Check(assembler.Push(newer[2], .200)?.seq == 22, "New complete snapshot recovers after a lost display fragment");
        Check(assembler.Push(lost[1], .210) == null && assembler.Latest.seq == 22,
            "Late missing fragment cannot rewind the displayed snapshot");
        assembler.Reset(); var expired = ViewPackets(23); assembler.Push(expired[0], .300); assembler.Push(expired[1], .350);
        Check(assembler.Push(expired[2], .421) == null && assembler.Latest == null,
            "Display assembly rejects more than120ms spread between required parts");
        // Both production headers come from JSON. Roundtrip both fixture values
        // because JsonUtility normalizes unset strings from null to empty.
        var state = JsonUtility.FromJson<RedboolOnlineInputBridge.Header>(JsonUtility.ToJson(
            new RedboolOnlineInputBridge.Header {stream_id="s",capture_ns=100,calibration_generation=1,
                calibrated=true,tracking="VALID",block_active=true,movement="NEUTRAL",fsm_state="READY"}));
        var repeated = JsonUtility.FromJson<RedboolOnlineInputBridge.Header>(JsonUtility.ToJson(state));
        repeated.sequence=200; repeated.sent_ns=300;
        Check(RedboolOnlineInputBridge.SameControl(state,repeated), "Online repeated source/control is coalesced despite a new send timestamp");
        repeated.block_active=false;
        Check(!RedboolOnlineInputBridge.SameControl(state,repeated), "Guard release is never suppressed as a repeated held STATE");
        repeated.block_active=true; repeated.calibration_generation=2;
        Check(!RedboolOnlineInputBridge.SameControl(state,repeated), "Calibration reset is never coalesced into prior generation");
        repeated.calibration_generation=1; repeated.capture_ns=101;
        Check(!RedboolOnlineInputBridge.SameControl(state,repeated), "A new capture remains independently forwardable");
        repeated.capture_ns=100; repeated.tracking="STALE";
        Check(!RedboolOnlineInputBridge.SameControl(state,repeated), "Tracking loss remains independently forwardable");
    }
    static byte[] Pair(Packet state, Packet inputEvent)
        => RedboolOnlineWire.PackInputs(Encoding.UTF8.GetBytes(JsonUtility.ToJson(state)),
            Encoding.UTF8.GetBytes(JsonUtility.ToJson(inputEvent)), 3);
    static void PairedReceiver()
    {
        using (var fixture = new Fixture(true))
        {
            var held = State(10); held.block_active = true;
            Check(fixture.Apply(held) == "TUTORIAL_APPLIED" && fixture.Receiver.BlockActive,
                "Pair fixture starts with a held guard");
            var release = State(20); var action = Event(21); byte[] packet = Pair(release, action);
            Check(RedboolOnlineWire.TryUnpackInputs(packet, 3, out var stateBytes, out var eventBytes),
                "Production atomic source/action envelope decodes");
            Check(fixture.ApplyBytes(stateBytes) == "TUTORIAL_APPLIED" && !fixture.Receiver.BlockActive,
                "Paired source releases guard before its action is processed");
            Check(fixture.ApplyBytes(eventBytes) == "TUTORIAL_APPLIED" && fixture.Events == 1,
                "Paired action reaches the real receiver once");
            Check(fixture.ApplyBytes(stateBytes) == "OLD_SEQUENCE"
                && fixture.ApplyBytes(eventBytes) == "TUTORIAL_APPLIED" && fixture.Events == 1,
                "Duplicate STATE/action pair returns cached event outcome without replay");
            var delayedState = State(30); var delayedEvent = Event(31); packet = Pair(delayedState, delayedEvent);
            Check(fixture.Apply(State(40)) == "TUTORIAL_APPLIED", "Newer STATE overtakes an entire source/action pair");
            Check(RedboolOnlineWire.TryUnpackInputs(packet, 3, out stateBytes, out eventBytes)
                && fixture.ApplyBytes(stateBytes) == "OLD_SEQUENCE"
                && fixture.ApplyBytes(eventBytes) == "TUTORIAL_APPLIED" && fixture.Events == 2,
                "Fresh delayed pair event survives newer STATE without rewinding STATE");
            Check(!RedboolOnlineWire.TryUnpackInputs(packet, 4, out _, out _),
                "Rematch rejects prior-round source and action together");
        }
        using (var fixture = new Fixture(true))
        {
            var staleState = State(20, ageMs: 200); var expired = Event(21);
            expired.capture_ns = expired.commit_ns = Now - 200 * Ms;
            expired.event_expires_ns = expired.commit_ns + 150 * Ms;
            byte[] packet = Pair(staleState, expired);
            Check(RedboolOnlineWire.TryUnpackInputs(packet, 3, out var stateBytes, out var eventBytes),
                "Pair transport retains expired timestamps for authoritative validation");
            Check(fixture.ApplyBytes(stateBytes) == "EXPIRED" && fixture.ApplyBytes(eventBytes) == "EXPIRED"
                && fixture.Events == 0 && !fixture.Receiver.LiveReady,
                "Atomic bundling cannot renew an expired STATE or action");
        }
    }
    static void ViewOwnerLifetime()
    {
        string[] overrideArgs = { "REDBOOL", "--redbool-view-port", "15067" };
        Check(RedboolViewLink.ResolvePeerPort(true, overrideArgs) == RedboolViewProtocol.Port,
            "Online peer identity ignores a previous LAN port override");
        Check(RedboolViewLink.ResolvePeerPort(false, overrideArgs) == 15067,
            "LAN keeps the explicit display port override");
        Check(RedboolViewLink.ResolvePeerPort(false, new[] { "--redbool-view-port", "65536" }) == RedboolViewProtocol.Port
            && RedboolViewLink.ResolvePeerPort(true, null) == RedboolViewProtocol.Port,
            "Invalid or absent display override retains the protocol default");
        var pvp = typeof(RedboolPvp); var linkType = typeof(RedboolViewLink);
        const BindingFlags statics = BindingFlags.Public | BindingFlags.Static;
        var active = pvp.GetProperty("Active", statics); var online = pvp.GetProperty("Online", statics);
        var instance = pvp.GetProperty("Instance", statics);
        object oldActive = active.GetValue(null), oldOnline = online.GetValue(null), oldInstance = instance.GetValue(null);
        float oldTimeScale = Time.timeScale;
        var ownerObject = new GameObject("Inactive online owner fixture"); ownerObject.SetActive(false);
        var sessionObject = new GameObject("Inactive online session fixture"); sessionObject.SetActive(false);
        try
        {
            var owner = ownerObject.AddComponent<RedboolPvp>();
            var link = ownerObject.AddComponent<RedboolViewLink>();
            var session = sessionObject.AddComponent<RedboolPhotonSession>();
            typeof(RedboolPhotonSession).GetProperty("ConnectionEpoch").SetValue(session, 7);
            typeof(RedboolPhotonSession).GetProperty("InRoom").SetValue(session, true);
            linkType.GetField("game", Private).SetValue(link, owner);
            linkType.GetField("online", Private).SetValue(link, true);
            linkType.GetField("client", Private).SetValue(link, true);
            linkType.GetField("initialized", Private).SetValue(link, true);
            linkType.GetField("onlineSession", Private).SetValue(link, session);
            linkType.GetField("onlineEpoch", Private).SetValue(link, 7);
            active.SetValue(null, true); online.SetValue(null, true); instance.SetValue(null, owner);
            var valid = linkType.GetProperty("OnlineOwnerValid", Private);
            Check((bool)valid.GetValue(link), "Display owner gate accepts the matching live match and epoch");
            Action<string> checkStopped = reason =>
            {
                Check(!(bool)valid.GetValue(link), reason);
                Time.timeScale = .75f;
                linkType.GetMethod("Update", Private).Invoke(link, null);
                linkType.GetMethod("LateUpdate", Private).Invoke(link, null);
                linkType.GetMethod("ReceiveOnline", Private).Invoke(link, new object[] { (byte)2, new byte[] { 1 } });
                var queue = (System.Collections.ICollection)linkType.GetField("onlineInbox", Private).GetValue(link);
                Check(Time.timeScale == .75f && queue.Count == 0,
                    "Retired display cannot pause a menu or enqueue another room callback");
            };
            online.SetValue(null, false); checkStopped("Leaving online mode invalidates the old display"); online.SetValue(null, true);
            active.SetValue(null, false); checkStopped("Ended match invalidates the old display"); active.SetValue(null, true);
            instance.SetValue(null, null); checkStopped("Replaced match owner invalidates the old display"); instance.SetValue(null, owner);
            typeof(RedboolPhotonSession).GetProperty("ConnectionEpoch").SetValue(session, 8);
            checkStopped("New room epoch invalidates old display callbacks");
            linkType.GetField("online", Private).SetValue(link, false);
            Check((bool)valid.GetValue(link), "LAN display keeps its existing owner-gate behavior");
            Check(linkType.GetField("socket", Private).GetValue(link) == null,
                "Display lifecycle checks never open a UDP socket");
        }
        finally
        {
            active.SetValue(null, oldActive); online.SetValue(null, oldOnline); instance.SetValue(null, oldInstance);
            Time.timeScale = oldTimeScale;
            UnityEngine.Object.DestroyImmediate(ownerObject); UnityEngine.Object.DestroyImmediate(sessionObject);
        }
    }
    public static void Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Run outside Play mode.");
        checks.Clear(); string error = null;
        try
        {
            ViewAndCoalescing();
            ViewOwnerLifetime();
            PairedReceiver();
            foreach (bool online in new[] { false, true })
                using (var fixture = new Fixture(online))
                {
                    Check(fixture.Apply(State(20)) == "TUTORIAL_APPLIED", (online ? "Online" : "LAN") + " accepts fresh STATE");
                    string outcome = fixture.Apply(Event(19));
                    Check(outcome == (online ? "TUTORIAL_APPLIED" : "OLD_SEQUENCE"),
                        online ? "Fresh event19 survives STATE20 overtake online" : "LAN keeps original shared sequence behavior");
                    Check(fixture.Events == (online ? 1 : 0), "Only accepted one-shot reaches practice callback");
                }
            foreach (bool online in new[] { false, true })
                using (var fixture = new Fixture(online))
                {
                    var state = State(20); state.clock_uncertainty_ns = 60 * Ms;
                    Check(fixture.Apply(state) == (online ? "TUTORIAL_APPLIED" : "INVALID_TIMING"),
                        online ? "Online explicitly accepts60ms uncertainty" : "LAN retains25ms uncertainty maximum");
                }
            using (var fixture = new Fixture(true))
            {
                var state = State(20); state.clock_uncertainty_ns = 76 * Ms;
                Check(fixture.Apply(state) == "INVALID_TIMING", "Online still rejects uncertainty above75ms");
                state = State(21); state.clock_uncertainty_ns = 61 * Ms; state.sent_ns -= 70 * Ms; state.capture_ns = state.sent_ns - 40 * Ms; state.ttl_ms = 110;
                Check(fixture.Apply(state) == "EXPIRED", "Synthetic120ms route can synchronize yet fail original source lifetime");
                Check(!fixture.Receiver.LiveReady, "Rejected high-latency state cannot authorize input");
            }
            using (var fixture = new Fixture(true))
            {
                var state = State(20); state.block_active = true;
                Check(fixture.Apply(state) == "TUTORIAL_APPLIED" && fixture.Receiver.BlockActive, "Fresh guard is accepted");
                long deadline = (long)fixture.Get("deadline");
                Check(deadline <= state.capture_ns + 150 * Ms - state.clock_uncertainty_ns,
                    "Online guard deadline cannot exceed original capture150ms minus uncertainty");
                var stale = State(21, ageMs: 200); stale.block_active = true;
                Check(fixture.Apply(stale) == "EXPIRED", "Fresh packet TTL cannot revive200ms-old source");
                Check((long)fixture.Get("deadline") == deadline, "Rejected stale STATE never renews existing lease");
                var attack = Event(22); attack.commit_ns -= 200 * Ms; attack.capture_ns = attack.commit_ns; attack.event_expires_ns = attack.commit_ns + 150 * Ms;
                Check(fixture.Apply(attack) == "EXPIRED", "Expired event stays expired despite new packet send time");
                attack = Event(23); attack.event_expires_ns = attack.commit_ns + 151 * Ms;
                Check(fixture.Apply(attack) == "INVALID_TIMING", "Event expiry extension beyond original150ms is rejected");
                attack = Event(24); attack.capture_ns = attack.commit_ns + 1;
                Check(fixture.Apply(attack) == "INVALID_TIMING", "Capture cannot occur after event commit");
            }
            using (var fixture = new Fixture(true))
            {
                Check(fixture.Apply(State(20)) == "TUTORIAL_APPLIED", "Initial generation is live");
                var old = Event(21);
                Check(fixture.Apply(State(22, generation: 2)) == "TUTORIAL_APPLIED", "New calibration generation is accepted");
                Check(fixture.Apply(old) == "INPUT_LOCKED", "Old in-flight generation cannot act after calibration reset");
                Check(fixture.Apply(State(23, generation: 1)) == "INPUT_LOCKED", "Older generation cannot replace current STATE");
                var forged = Event(24, generation: 2); forged.capture_ns = Now - 100 * Ms;
                Check(fixture.Apply(forged) == "INPUT_LOCKED", "Old capture cannot impersonate new calibration generation");
                Check(fixture.Events == 0, "Rejected generation events never reach action callbacks");
            }
            using (var fixture = new Fixture(true))
            {
                var preparing = State(20, generation: 2, ageMs: 90); preparing.calibrated = false; preparing.tracking = "CALIBRATING";
                Check(fixture.Apply(preparing) == "TUTORIAL_APPLIED", "Preparing STATE can establish a new generation without live input");
                Check(fixture.Apply(State(21, generation: 2)) == "TUTORIAL_APPLIED", "First valid capture establishes the generation source floor");
                var beforeValid = Event(22, generation: 2); beforeValid.capture_ns = Now - 50 * Ms;
                Check(fixture.Apply(beforeValid) == "INPUT_LOCKED", "Capture before first VALID generation frame is rejected even after a preparing STATE");
            }
            using (var fixture = new Fixture(true))
            {
                Check(fixture.Apply(State(20)) == "TUTORIAL_APPLIED", "Boundary fixture starts with live STATE");
                int replies = fixture.Replies; long sequence = (long)fixture.Get("onlineEventSequence");
                var pending = Event(21); bool sameRoundAndEpoch = true; Func<bool> valid = () => sameRoundAndEpoch;
                // The item was admitted while valid, then the room/round changed before Process.
                sameRoundAndEpoch = false;
                Check(fixture.Apply(pending, valid) == null && fixture.Replies == replies,
                    "Queued prior-round/session item is dropped before application and ACK");
                Check((long)fixture.Get("onlineEventSequence") == sequence && fixture.Events == 0,
                    "Boundary rejection leaves sequence and gameplay callbacks untouched");
            }
        }
        catch (Exception ex) { error = ex.ToString(); throw; }
        finally
        {
            var report = new Report { passed = error == null, assertions = checks.Count, checks = checks.ToArray(), error = error };
            string output = Environment.GetEnvironmentVariable("REDBOOL_ONLINE_CHECK_OUTPUT");
            if (!string.IsNullOrEmpty(output))
            {
                string parent = Path.GetDirectoryName(output); if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                File.WriteAllText(output, JsonUtility.ToJson(report, true));
            }
            Debug.Log("REDBOOL ONLINE PROTOCOL " + (report.passed ? "PASS " : "FAIL ") + checks.Count + "; inactive receiver; no camera/network/game");
        }
    }
}
