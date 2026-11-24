// Compiled with the production wire files by check_online_routing.py.
// No Unity/Photon substitutes, sockets, camera, sleep, SDK or cloud connection.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

static class OnlineRoutingTests
{
    static int assertions;
    static readonly List<string> failures = new List<string>();
    static void Check(bool condition, string label)
    { assertions++; if (!condition) failures.Add(label); }
    static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r") + "\"";
    static bool SafeUnpack(byte[] bytes, int round, out byte[] result, string label)
    {
        try { return RedboolOnlineWire.TryUnpack(bytes, round, out result); }
        catch (Exception error) { result = null; Check(false, label + " threw " + error.GetType().Name); return false; }
    }
    static void PairedInputs(byte[] inputEvent)
    {
        var state = Encoding.UTF8.GetBytes("{\"kind\":\"STATE\",\"sequence\":18,\"block_active\":false,\"capture_ns\":10000000000,\"sent_ns\":10020000000,\"ttl_ms\":130}");
        var packed = RedboolOnlineWire.PackInputs(state, inputEvent, 3);
        Check(RedboolOnlineWire.TryUnpackInputs(packed, 3, out var stateOut, out var eventOut)
            && state.SequenceEqual(stateOut) && inputEvent.SequenceEqual(eventOut), "Atomic pair preserves exact STATE then event bytes and original deadlines");
        Check(!RedboolOnlineWire.TryUnpackInputs(packed, 4, out _, out _), "Previous round rejects the whole STATE/event pair");
        var gate = new RedboolPhotonSequenceGate();
        Check(gate.Accept(1, 40) && gate.Accept(1, 39)
            && RedboolOnlineWire.TryUnpackInputs(packed, 3, out stateOut, out eventOut)
            && state.SequenceEqual(stateOut) && inputEvent.SequenceEqual(eventOut), "Reordering whole relay messages cannot separate the pair or reorder its members");
        Check(!gate.Accept(1, 39), "Duplicate relay pair is suppressed by the production sequence gate");
        foreach (string kind in new[] { "SYNC", "ACK", "STATE" })
        {
            var raw = Encoding.UTF8.GetBytes("{\"kind\":\"" + kind + "\"}");
            var single = RedboolOnlineWire.PackInputs(null, raw, 3);
            Check(single.SequenceEqual(RedboolOnlineWire.Pack(raw, 3)), "Unpaired " + kind + " keeps legacy packet bytes");
            Check(RedboolOnlineWire.TryUnpackInputs(single, 3, out stateOut, out eventOut)
                && stateOut == null && raw.SequenceEqual(eventOut), "Unpaired " + kind + " remains backwards compatible");
        }
        Check(RedboolOnlineWire.PackInputs(new byte[0], inputEvent, 3) == null
            && RedboolOnlineWire.PackInputs(state, new byte[0], 3) == null
            && RedboolOnlineWire.PackInputs(state, null, 3) == null, "Empty pair members are rejected");
        var maximumState = Enumerable.Repeat((byte)'s', RedboolOnlineWire.MaxRaw - 10).ToArray();
        var maximumPair = RedboolOnlineWire.PackInputs(maximumState, new byte[] { (byte)'e' }, 3);
        Check(RedboolOnlineWire.TryUnpackInputs(maximumPair, 3, out stateOut, out eventOut)
            && maximumState.SequenceEqual(stateOut) && eventOut.Length == 1, "Exact8192-byte combined raw limit including framing is accepted");
        Check(RedboolOnlineWire.PackInputs(maximumState, new byte[2], 3) == null
            && RedboolOnlineWire.PackInputs(new byte[RedboolOnlineWire.MaxRaw], inputEvent, 3) == null, "Combined length overflow is rejected before compression");
        RedboolOnlineWire.TryUnpack(packed, 3, out var combined);
        foreach (int index in new[] { 1, 5 }) foreach (int size in new[] { int.MinValue, -1, 0, int.MaxValue, combined.Length })
        {
            var corrupt = (byte[])combined.Clone(); Buffer.BlockCopy(BitConverter.GetBytes(size), 0, corrupt, index, 4);
            Check(!RedboolOnlineWire.TryUnpackInputs(RedboolOnlineWire.Pack(corrupt, 3), 3, out stateOut, out eventOut)
                && stateOut == null && eventOut == null, "Invalid pair length cannot expose a partial input");
        }
        for (int size = 1; size < 11; size++)
            Check(!RedboolOnlineWire.TryUnpackInputs(RedboolOnlineWire.Pack(new byte[size], 3), 3, out _, out _), "Truncated pair framing is rejected");
        var extra = new byte[combined.Length + 1]; Buffer.BlockCopy(combined, 0, extra, 0, combined.Length);
        Check(!RedboolOnlineWire.TryUnpackInputs(RedboolOnlineWire.Pack(extra, 3), 3, out _, out _), "Trailing pair payload cannot bypass exact lengths");
    }
    public static int Main()
    {
        var source = Encoding.UTF8.GetBytes("{\"kind\":\"GESTURE\",\"event_id\":\"original-id\",\"capture_ns\":10000000000,\"commit_ns\":10010000000,\"sent_ns\":10020000000,\"event_expires_ns\":10160000000,\"calibration_generation\":7,\"personal_status\":\"동작\"}");
        byte[] packed = RedboolOnlineWire.Pack(source, 3);
        Check(packed != null && packed.Length <= RedboolPhotonWire.MaxPayloadBytes, "Real compression stays inside relay payload budget");
        Check(SafeUnpack(packed, 3, out var restored, "roundtrip") && restored.SequenceEqual(source), "Original JSON bytes and timestamps survive online compression unchanged");
        PairedInputs(source);
        Check(!SafeUnpack(packed, 4, out _, "round mismatch"), "Previous round rejected");
        Check(RedboolOnlineWire.Pack(null, 3) == null && RedboolOnlineWire.Pack(new byte[0], 3) == null, "Empty inputs rejected");
        Check(RedboolOnlineWire.Pack(new byte[8193], 3) == null && RedboolOnlineWire.Pack(source, -1) == null, "Oversize input and negative round rejected");
        var maximum = Enumerable.Repeat((byte)7, RedboolOnlineWire.MaxRaw).ToArray();
        var maximumPacked = RedboolOnlineWire.Pack(maximum, 3);
        Check(SafeUnpack(maximumPacked, 3, out var maximumRestored, "maximum") && maximum.SequenceEqual(maximumRestored), "Maximum raw command roundtrips within bounded allocation");
        var random = new Random(5116);
        var noisy = new byte[RedboolOnlineWire.MaxRaw]; random.NextBytes(noisy);
        var noisyPacked = RedboolOnlineWire.Pack(noisy, 3);
        Check(noisyPacked == null || noisyPacked.Length <= RedboolPhotonWire.MaxPayloadBytes, "Incompressible maximum input never exceeds Photon payload limit");
        if (noisyPacked != null) Check(SafeUnpack(noisyPacked, 3, out var noisyRestored, "incompressible") && noisyRestored.SequenceEqual(noisy), "Accepted incompressible payload stays exact");
        foreach (int length in new[] { -1, 0, 8193, int.MaxValue })
        {
            var bad = (byte[])packed.Clone(); Buffer.BlockCopy(BitConverter.GetBytes(length), 0, bad, 5, 4);
            Check(!SafeUnpack(bad, 3, out _, "declared length"), "Invalid declared raw length rejected before allocation");
        }
        for (int i = 0; i < packed.Length; i++)
        {
            bool ok = SafeUnpack(packed.Take(i).ToArray(), 3, out var partial, "truncation");
            Check(!ok || partial.SequenceEqual(source), "Truncation cannot produce altered accepted JSON");
        }
        for (int i = 0; i < 2000; i++)
        {
            var bad = new byte[random.Next(10, 180)]; random.NextBytes(bad);
            bad[0] = 1; Buffer.BlockCopy(BitConverter.GetBytes(3), 0, bad, 1, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(random.Next(1, 129)), 0, bad, 5, 4);
            SafeUnpack(bad, 3, out _, "malformed deflate " + i);
            assertions++;
        }
        var roomEpoch = Enumerable.Range(0, 16).Select(i => (byte)(i + 1)).ToArray();
        var oldEpoch = (byte[])roomEpoch.Clone(); oldEpoch[0] ^= 1;
        var envelope = RedboolPhotonWire.Encode(1, 20, roomEpoch, packed);
        Check(RedboolPhotonWire.TryDecode(envelope, roomEpoch, out byte channel, out uint seq, out var payload) && channel == 1 && seq == 20, "Production relay envelope decodes command channel");
        Check(SafeUnpack(payload, 3, out var tunneled, "nested roundtrip") && source.SequenceEqual(tunneled), "Both real codec layers retain original bytes");
        Check(!RedboolPhotonWire.TryDecode(envelope, oldEpoch, out _, out _, out _), "Previous room epoch rejected before command decode");
        var gate = new RedboolPhotonSequenceGate();
        Check(gate.Accept(1, 20) && gate.Accept(1, 19), "Relay permits STATE/event reordering for receiver policy");
        Check(!gate.Accept(1, 19), "Relay duplicate suppressed");
        Check(gate.Accept(2, 19), "View sequence remains independent of input sequence");
        string[] rows = new[] {20,40,80,120,148,160}.Select(rtt =>
        {
            double uncertainty = rtt / 2.0 + 1;
            // Explicit model: 40ms source age at send + half RTT + 10ms main-thread wait + uncertainty.
            double conservativeAge = 40 + rtt / 2.0 + 10 + uncertainty;
            return "{\"corrected_rtt_ms\":" + rtt + ",\"uncertainty_ms\":" + uncertainty.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ",\"lan_sync_allowed\":" + (uncertainty <= 25 ? "true" : "false") + ",\"online_sync_allowed\":" + (uncertainty <= 75 ? "true" : "false")
                + ",\"modeled_source_age_plus_uncertainty_ms\":" + conservativeAge.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ",\"fresh_state_allowed\":" + (conservativeAge < 150 ? "true" : "false") + "}";
        }).ToArray();
        Console.WriteLine("{\"passed\":" + (failures.Count == 0 ? "true" : "false") + ",\"assertions\":" + assertions
            + ",\"failures\":[" + string.Join(",", failures.Select(Quote)) + "]"
            + ",\"scope\":\"Actual production command compression/round codec and relay epoch/sequence codec. No receiver, SDK, network, camera, game or rendered latency test.\""
            + ",\"latency_model_scope\":\"Algebra only: symmetric link, new sync,40ms source age at send,10ms host wait; no jitter/loss/render simulation.75ms synchronization cap does not imply playable input.\""
            + ",\"latency_model\":[" + string.Join(",", rows) + "]}");
        return failures.Count == 0 ? 0 : 1;
    }
}
