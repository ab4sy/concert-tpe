namespace CaisseApIpTester.Protocol;

/// <summary>
/// CONCERT ref. 095 link procedure for a secondary terminal (TPV / caisse).
/// Emission: ENQ → ACK → STX data ETX LRC → ACK → EOT.
/// Reception: ENQ → ACK → STX … ETX LRC → ACK → EOT.
/// </summary>
public sealed class ConcertLinkLayer
{
    private readonly Func<CancellationToken, byte> _readByte;
    private readonly Action<byte> _writeByte;
    private readonly Action<byte[], int, int> _writeBytes;

    public int MaxDataRetries { get; set; } = 3;
    public int MaxSessionRetries { get; set; } = 3;
    public int TraMs { get; set; } = 100;
    public int TrMs { get; set; } = 10;
    public int T1SecondaryMs { get; set; } = 600;

    public ConcertLinkLayer(
        Func<CancellationToken, byte> readByte,
        Action<byte> writeByte,
        Action<byte[], int, int> writeBytes)
    {
        _readByte = readByte;
        _writeByte = writeByte;
        _writeBytes = writeBytes;
    }

    public void EmitSecondary(string payloadAscii, CancellationToken ct)
    {
        for (int n2 = 0; n2 < MaxSessionRetries; n2++)
        {
            ct.ThrowIfCancellationRequested();

            _writeByte(ConcertFrame.Enq);
            WaitTurnaround();

            if (!WaitForAck(ct))
                continue;

            if (TrySendDataFrame(payloadAscii, ct))
            {
                _writeByte(ConcertFrame.Eot);
                WaitTurnaround();
                Thread.Sleep(TraMs);
                return;
            }

            _writeByte(ConcertFrame.Eot);
            WaitTurnaround();
        }

        throw new IOException("CONCERT emission failed after maximum session retries.");
    }

    public string Receive(CancellationToken ct, int? applicationTimeoutMs = null)
    {
        int timeout = applicationTimeoutMs ?? T1SecondaryMs * MaxSessionRetries * 2;
        var deadline = Environment.TickCount64 + timeout;

        while (Environment.TickCount64 < deadline)
        {
            ct.ThrowIfCancellationRequested();

            if (!TryReadByte(out byte b, ct, remainingMs(deadline)))
                continue;

            if (b != ConcertFrame.Enq)
                continue;

            WaitTurnaround();
            _writeByte(ConcertFrame.Ack);
            WaitTurnaround();

            string payload = ReadDataFrame(ct);
            WaitTurnaround();
            _writeByte(ConcertFrame.Ack);
            WaitTurnaround();

            if (TryReadByte(out byte eot, ct, remainingMs(deadline)) && eot == ConcertFrame.Eot)
            {
                Thread.Sleep(TraMs);
                return payload;
            }
        }

        throw new TimeoutException("CONCERT reception timed out waiting for terminal response.");
    }

    private bool TrySendDataFrame(string payloadAscii, CancellationToken ct)
    {
        for (int n1 = 0; n1 < MaxDataRetries; n1++)
        {
            ct.ThrowIfCancellationRequested();
            byte[] frame = ConcertFrame.Wrap(payloadAscii);
            _writeBytes(frame, 0, frame.Length);
            WaitTurnaround();

            if (WaitForAck(ct))
                return true;
        }

        return false;
    }

    private string ReadDataFrame(CancellationToken ct)
    {
        for (int n1 = 0; n1 < MaxDataRetries; n1++)
        {
            ct.ThrowIfCancellationRequested();
            ReadUntil(ConcertFrame.Stx, ct);

            var buf = new List<byte>(128);
            while (true)
            {
                byte b = _readByte(ct);
                if (b == ConcertFrame.Etx)
                    break;
                buf.Add(b);
            }

            byte lrc = _readByte(ct);
            byte[] payloadBytes = [.. buf];
            byte expected = ConcertFrame.ComputeLrc(payloadBytes);

            if (lrc == expected)
                return ConcertFrame.Unwrap(payloadBytes);

            WaitTurnaround();
            _writeByte(ConcertFrame.Nak);
            WaitTurnaround();
        }

        throw new IOException("CONCERT data frame invalid after maximum retries.");
    }

    private bool WaitForAck(CancellationToken ct)
    {
        var deadline = Environment.TickCount64 + T1SecondaryMs;
        while (Environment.TickCount64 < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (!TryReadByte(out byte b, ct, remainingMs(deadline)))
                continue;
            if (b == ConcertFrame.Ack) return true;
            if (b == ConcertFrame.Nak) return false;
        }

        return false;
    }

    private void ReadUntil(byte target, CancellationToken ct)
    {
        var deadline = Environment.TickCount64 + T1SecondaryMs;
        while (Environment.TickCount64 < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (TryReadByte(out byte b, ct, remainingMs(deadline)) && b == target)
                return;
        }

        throw new TimeoutException($"Timed out waiting for 0x{target:X2}.");
    }

    private static int remainingMs(long deadline) =>
        (int)Math.Max(1, deadline - Environment.TickCount64);

    private bool TryReadByte(out byte value, CancellationToken ct, int timeoutMs)
    {
        value = 0;
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                value = _readByte(ct);
                return true;
            }
            catch (TimeoutException)
            {
                return false;
            }
        }

        return false;
    }

    private void WaitTurnaround()
    {
        if (TrMs > 0)
            Thread.Sleep(TrMs);
    }
}
