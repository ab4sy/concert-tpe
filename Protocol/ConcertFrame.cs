using System.Text;

namespace CaisseApIpTester.Protocol;

/// <summary>
/// STX + ASCII payload + ETX + LRC per CONCERT ref. 095 §2.3.
/// LRC = XOR of all payload bytes and ETX (STX excluded), initialized to 0.
/// </summary>
public static class ConcertFrame
{
    public const byte Stx = 0x02;
    public const byte Etx = 0x03;
    public const byte Enq = 0x05;
    public const byte Ack = 0x06;
    public const byte Eot = 0x04;
    public const byte Nak = 0x15;

    public static byte[] Wrap(string payloadAscii)
    {
        byte[] payload = Encoding.ASCII.GetBytes(payloadAscii);
        byte lrc = ComputeLrc(payload);
        var frame = new byte[1 + payload.Length + 1 + 1];
        frame[0] = Stx;
        payload.CopyTo(frame, 1);
        frame[^2] = Etx;
        frame[^1] = lrc;
        return frame;
    }

    public static string Unwrap(ReadOnlySpan<byte> payloadBytes) =>
        Encoding.ASCII.GetString(payloadBytes);

    public static byte ComputeLrc(ReadOnlySpan<byte> payloadIncludingUpToBeforeEtx)
    {
        byte lrc = 0;
        foreach (byte b in payloadIncludingUpToBeforeEtx)
            lrc ^= b;
        lrc ^= Etx;
        return lrc;
    }

    public static bool TryParse(ReadOnlySpan<byte> framed, out string payload, out string? error)
    {
        payload = string.Empty;
        error = null;

        if (framed.Length < 4 || framed[0] != Stx)
        {
            error = "Missing STX.";
            return false;
        }

        int etxIndex = framed.Slice(1).IndexOf((byte)Etx);
        if (etxIndex < 0)
        {
            error = "Missing ETX.";
            return false;
        }

        etxIndex += 1;
        if (etxIndex + 1 >= framed.Length)
        {
            error = "Missing LRC.";
            return false;
        }

        var payloadSpan = framed.Slice(1, etxIndex - 1);
        byte receivedLrc = framed[etxIndex + 1];
        byte expectedLrc = ComputeLrc(payloadSpan);

        if (receivedLrc != expectedLrc)
        {
            error = $"LRC mismatch: expected 0x{expectedLrc:X2}, got 0x{receivedLrc:X2}.";
            return false;
        }

        payload = Unwrap(payloadSpan);
        return true;
    }
}
