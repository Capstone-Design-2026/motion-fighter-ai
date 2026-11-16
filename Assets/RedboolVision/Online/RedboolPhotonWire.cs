using System;
using System.Collections.Generic;
using System.Security.Cryptography;

/// <summary>Small, SDK-independent boundary for the private-room transport.</summary>
public static class RedboolPhotonWire
{
    public const int MaxPayloadBytes = 8192;
    public const int PreferredPayloadBytes = 960;
    public const int HeaderBytes = 28;
    public const byte Version = 1;
    const string Alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    public static string NewRoomCode()
    {
        var code = new char[6];
        var one = new byte[1];
        using (var rng = RandomNumberGenerator.Create())
            for (int i = 0; i < code.Length; i++)
            {
                // Rejection sampling avoids bias when the alphabet does not divide 256.
                do { rng.GetBytes(one); } while (one[0] >= 256 - 256 % Alphabet.Length);
                code[i] = Alphabet[one[0] % Alphabet.Length];
            }
        return "KR-" + new string(code);
    }

    public static bool TryNormalizeRoomCode(string text, out string code)
    {
        code = null;
        if (text == null || text.Length > 32) return false;
        text = text.Trim().ToUpperInvariant();
        if (text.Length != 9 || !text.StartsWith("KR-", StringComparison.Ordinal)) return false;
        for (int i = 3; i < text.Length; i++) if (Alphabet.IndexOf(text[i]) < 0) return false;
        code = text;
        return true;
    }

    public static byte[] NewEpoch()
    {
        var bytes = new byte[16];
        using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
        return bytes;
    }

    public static string EpochText(byte[] epoch)
    {
        if (epoch == null || epoch.Length != 16) throw new ArgumentException("Room epoch must be 16 bytes.");
        return BitConverter.ToString(epoch).Replace("-", "");
    }

    public static bool TryReadEpoch(string text, out byte[] epoch)
    {
        epoch = null;
        if (text == null || text.Length != 32) return false;
        var result = new byte[16];
        for (int i = 0; i < result.Length; i++)
        {
            int a = Hex(text[i * 2]), b = Hex(text[i * 2 + 1]);
            if (a < 0 || b < 0) return false;
            result[i] = (byte)((a << 4) | b);
        }
        epoch = result;
        return true;
    }

    static int Hex(char c) => c >= '0' && c <= '9' ? c - '0' :
        c >= 'A' && c <= 'F' ? c - 'A' + 10 : c >= 'a' && c <= 'f' ? c - 'a' + 10 : -1;

    public static byte[] Encode(byte channel, uint sequence, byte[] epoch, byte[] payload)
    {
        if (epoch == null || epoch.Length != 16) throw new ArgumentException("Invalid room epoch.");
        if (payload == null || payload.Length == 0 || payload.Length > MaxPayloadBytes)
            throw new ArgumentException("Payload must contain 1..8192 bytes.");
        var result = new byte[HeaderBytes + payload.Length];
        result[0] = (byte)'R'; result[1] = (byte)'B'; result[2] = (byte)'O'; result[3] = (byte)'N';
        result[4] = Version; result[5] = channel;
        Buffer.BlockCopy(epoch, 0, result, 6, 16);
        result[22] = (byte)(sequence >> 24); result[23] = (byte)(sequence >> 16);
        result[24] = (byte)(sequence >> 8); result[25] = (byte)sequence;
        result[26] = (byte)(payload.Length >> 8); result[27] = (byte)payload.Length;
        Buffer.BlockCopy(payload, 0, result, HeaderBytes, payload.Length);
        return result;
    }

    public static bool TryDecode(byte[] data, byte[] epoch, out byte channel, out uint sequence, out byte[] payload)
    {
        channel = 0; sequence = 0; payload = null;
        if (data == null || data.Length <= HeaderBytes || data.Length > HeaderBytes + MaxPayloadBytes ||
            epoch == null || epoch.Length != 16) return false;
        if (data[0] != 'R' || data[1] != 'B' || data[2] != 'O' || data[3] != 'N' || data[4] != Version) return false;
        int length = (data[26] << 8) | data[27];
        if (length != data.Length - HeaderBytes) return false;
        for (int i = 0; i < 16; i++) if (data[i + 6] != epoch[i]) return false;
        channel = data[5];
        sequence = ((uint)data[22] << 24) | ((uint)data[23] << 16) | ((uint)data[24] << 8) | data[25];
        if (sequence == 0) return false;
        payload = new byte[length];
        Buffer.BlockCopy(data, HeaderBytes, payload, 0, length);
        return true;
    }
}

/// <summary>
/// Bounded duplicate suppression only. Reordered unseen messages are intentionally accepted:
/// application channels contain different input kinds and independent multipart view frames.
/// </summary>
public sealed class RedboolPhotonSequenceGate
{
    readonly HashSet<ulong> seen = new HashSet<ulong>();
    readonly Queue<ulong> order = new Queue<ulong>(512);
    public void Clear() { seen.Clear(); order.Clear(); }
    public bool Accept(byte channel, uint sequence)
    {
        if (sequence == 0) return false;
        ulong key = ((ulong)channel << 32) | sequence;
        if (!seen.Add(key)) return false;
        order.Enqueue(key);
        if (order.Count > 512) seen.Remove(order.Dequeue());
        return true;
    }
}
