using System;
using System.IO;
using System.IO.Compression;

// The original source timestamps and JSON bytes survive the online hop unchanged.
// Compression is only for camera commands/ACKs, never camera frames.
public static class RedboolOnlineWire
{
    public const int MaxRaw = 8192;
    const int InputsHeaderBytes = 9;
    // A one-shot travels with its exact preceding source STATE in one relay
    // message, so network reordering cannot separate a guard release from it.
    // Ordinary ACK/SYNC/STATE packets retain their original format.
    public static byte[] PackInputs(byte[] state, byte[] inputEvent, int round)
    {
        if (state == null) return Pack(inputEvent, round);
        if (inputEvent == null || state.Length == 0 || inputEvent.Length == 0
            || state.Length > MaxRaw - InputsHeaderBytes
            || inputEvent.Length > MaxRaw - InputsHeaderBytes - state.Length) return null;
        using (var raw = new MemoryStream())
        using (var writer = new BinaryWriter(raw))
        {
            writer.Write((byte)0); writer.Write(state.Length); writer.Write(inputEvent.Length);
            writer.Write(state); writer.Write(inputEvent);
            return Pack(raw.ToArray(), round);
        }
    }
    public static bool TryUnpackInputs(byte[] packet, int round, out byte[] state, out byte[] inputEvent)
    {
        state = null; inputEvent = null;
        if (!TryUnpack(packet, round, out var raw)) return false;
        if (raw[0] != 0) { inputEvent = raw; return true; }
        if (raw.Length < InputsHeaderBytes + 2) return false;
        using (var input = new MemoryStream(raw, false))
        using (var reader = new BinaryReader(input))
        {
            reader.ReadByte();
            int stateLength = reader.ReadInt32(), eventLength = reader.ReadInt32();
            int bodyLength = raw.Length - InputsHeaderBytes;
            if (stateLength < 1 || stateLength >= bodyLength || eventLength != bodyLength - stateLength) return false;
            state = reader.ReadBytes(stateLength); inputEvent = reader.ReadBytes(eventLength);
            return true;
        }
    }
    public static byte[] Pack(byte[] data, int round)
    {
        if (data == null || data.Length == 0 || data.Length > MaxRaw || round < 0) return null;
        using (var output = new MemoryStream())
        {
            output.WriteByte(1);
            using (var writer = new BinaryWriter(output, System.Text.Encoding.UTF8, true))
            { writer.Write(round); writer.Write(data.Length); }
            using (var zip = new DeflateStream(output, CompressionLevel.Fastest, true)) zip.Write(data, 0, data.Length);
            return output.Length>MaxRaw?null:output.ToArray();
        }
    }
    public static bool TryUnpack(byte[] packet, int round, out byte[] data)
    {
        data = null;
        if (packet == null || packet.Length < 10 || packet.Length > MaxRaw + 128 || packet[0] != 1) return false;
        try
        {
            using (var input = new MemoryStream(packet, false))
            using (var reader = new BinaryReader(input))
            {
                reader.ReadByte();
                if (reader.ReadInt32() != round) return false;
                int size = reader.ReadInt32();
                if (size < 1 || size > MaxRaw) return false;
                var result = new byte[size];
                using (var zip = new DeflateStream(input, CompressionMode.Decompress, true))
                {
                    int offset = 0;
                    while (offset < size) { int count = zip.Read(result, offset, size - offset); if (count <= 0) return false; offset += count; }
                    if (zip.ReadByte() != -1) return false;
                }
                data = result; return true;
            }
        }
        catch (Exception e) when (e is IOException || e is ArgumentException) { return false; }
    }
}
