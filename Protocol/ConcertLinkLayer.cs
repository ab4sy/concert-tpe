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
    // How long to wait for the terminal's STX after we ACK its ENQ. The spec T1
    // is 600 ms but this (Android-based) TPE can take longer to start the frame.
    public int StxWaitMs { get; set; } = 5000;

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
        string? lastText = null;

        while (Environment.TickCount64 < deadline)
        {
            ct.ThrowIfCancellationRequested();

            if (!TryReadByte(out byte b, ct, remainingMs(deadline)))
                continue;

            if (b != ConcertFrame.Enq)
            {
                // This TPE can send an out-of-protocol status burst, e.g.
                // NAK + "TRANSACTION DECLINED" as plain ASCII with no STX/ETX.
                // Report it, then allow a short grace period for a real frame.
                string text = CollectTextBurst(b, ct, out bool enqFollowed);
                if (text.Length > 0)
                {
                    Console.WriteLine($"[TPE] \"{text}\"");
                    lastText = text;
                    deadline = Math.Min(deadline, Environment.TickCount64 + TextGraceMs);
                }
                if (!enqFollowed)
                    continue;
            }

            WaitTurnaround();
            _writeByte(ConcertFrame.Ack);
            WaitTurnaround();

            string payload;
            try
            {
                payload = ReadDataFrame(ct);
            }
            catch (TimeoutException tex)
            {
                // The terminal opened a session (ENQ) but never sent its frame.
                // Keep listening for a fresh ENQ until the application deadline.
                Console.WriteLine($"[SERIAL] {tex.Message} — waiting for the terminal to retry...");
                continue;
            }

            WaitTurnaround();
            _writeByte(ConcertFrame.Ack);
            WaitTurnaround();

            // The frame is already validated (LRC) and ACKed; a missing or late
            // EOT must not discard it.
            if (!TryReadByte(out byte eot, ct, Math.Min(T1SecondaryMs, remainingMs(deadline))) || eot != ConcertFrame.Eot)
                Console.WriteLine("[SERIAL] No EOT after data frame (ignored).");

            Thread.Sleep(TraMs);
            return payload;
        }

        if (lastText != null)
            throw new IOException($"Terminal message: \"{lastText}\" (no CONCERT response frame followed).");
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
            WaitForStx(ct);

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

    // Gather printable ASCII following a stray control byte until ~300 ms of
    // silence. Returns "" when the stray byte carried no text with it.
    private string CollectTextBurst(byte first, CancellationToken ct, out bool enqFollowed)
    {
        enqFollowed = false;
        var sb = new System.Text.StringBuilder();
        if (first is >= 0x20 and < 0x7F) sb.Append((char)first);
        while (TryReadByte(out byte b, ct, 300))
        {
            if (b is >= 0x20 and < 0x7F) sb.Append((char)b);
            else if (b == ConcertFrame.Enq) { enqFollowed = true; break; } // real session starting
        }
        return sb.ToString().Trim();
    }

    // After an out-of-protocol text burst, how long to still wait for a real frame.
    public int TextGraceMs { get; set; } = 5000;

    // Wait for STX; if the terminal did not see our ACK it repeats ENQ, so ACK
    // again instead of letting the ENQ burn the wait window.
    private void WaitForStx(CancellationToken ct)
    {
        var deadline = Environment.TickCount64 + StxWaitMs;
        while (Environment.TickCount64 < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (!TryReadByte(out byte b, ct, remainingMs(deadline)))
                continue;
            if (b == ConcertFrame.Stx)
                return;
            if (b == ConcertFrame.Enq)
            {
                WaitTurnaround();
                _writeByte(ConcertFrame.Ack);
                WaitTurnaround();
            }
        }

        throw new TimeoutException($"Timed out waiting for STX ({StxWaitMs} ms after ACK).");
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
