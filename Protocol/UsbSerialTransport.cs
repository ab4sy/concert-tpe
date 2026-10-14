using System.IO.Ports;
using System.Text;

namespace CaisseApIpTester.Protocol;

/// <summary>
/// CONCERT EURO/CAISSE over RS-232 (ref. 095 §1–2).
/// TPV (caisse) acts as secondary terminal: emits request, then receives TPE response.
/// </summary>
public sealed class UsbSerialTransport : ITransport
{
    private readonly SerialPort _port;
    private readonly TimeSpan _timeout;
    private ConcertLinkLayer? _link;

    public UsbSerialTransport(string portName, int baudRate = 9600, TimeSpan? timeout = null)
    {
        _timeout = timeout ?? TimeSpan.FromSeconds(180);
        // Per-byte read timeout is a short poll interval, NOT the application
        // timeout: the link layer drives its own deadlines (ENQ/ACK retries,
        // waiting for the card response). A byte that arrives sooner returns
        // immediately, so this only bounds how fast a *missing* byte is noticed.
        // Using the full 180 s here defeats the retry logic and hangs for
        // minutes when the terminal doesn't answer the ENQ.
        _port = new SerialPort(portName, baudRate, Parity.Even, 7, StopBits.One)
        {
            ReadTimeout = 500,
            WriteTimeout = 2000,
            Handshake = Handshake.None,
            // USB CDC-ACM terminals commonly hold their TX until the host raises
            // DTR/RTS; asserting both is harmless on a 3-wire link.
            DtrEnable = true,
            RtsEnable = true,
            Encoding = Encoding.ASCII
        };
    }

    public Task<string> SendAsync(string payload, CancellationToken ct = default) =>
        Task.Run(() => Exchange(payload, ct), ct);

    private string Exchange(string payload, CancellationToken ct)
    {
        if (!_port.IsOpen)
        {
            Console.WriteLine($"[SERIAL] Opening {_port.PortName} @ {_port.BaudRate} 7E1...");
            _port.Open();
            Console.WriteLine("[SERIAL] Port open.");
        }

        _link ??= new ConcertLinkLayer(ReadByte, WriteByte, WriteBuffer);

        Console.WriteLine($"[SERIAL] Emitting A4 request ({payload.Length} chars)...");
        _link.EmitSecondary(payload, ct);

        Console.WriteLine("[SERIAL] Waiting for TPE response...");
        string response = _link.Receive(ct, (int)_timeout.TotalMilliseconds);
        Console.WriteLine($"[SERIAL] Received A4 response ({response.Length} chars).");
        return response;
    }

    // Set CONCERT_TRACE=1 to dump every byte on the wire (link-layer debugging).
    private static readonly bool Trace =
        Environment.GetEnvironmentVariable("CONCERT_TRACE") == "1";

    private static void TraceByte(string dir, byte b)
    {
        if (!Trace) return;
        string name = b switch
        {
            0x02 => "STX", 0x03 => "ETX", 0x04 => "EOT", 0x05 => "ENQ",
            0x06 => "ACK", 0x15 => "NAK",
            _ => b is >= 0x20 and < 0x7F ? ((char)b).ToString() : "?"
        };
        Console.WriteLine($"  [{dir} {DateTime.Now:HH:mm:ss.fff}] {b:X2} {name}");
    }

    private byte ReadByte(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        int b = _port.BaseStream.ReadByte();
        if (b < 0)
            throw new EndOfStreamException("Serial port closed.");
        TraceByte("RX", (byte)b);
        return (byte)b;
    }

    private void WriteByte(byte b)
    {
        TraceByte("TX", b);
        _port.BaseStream.WriteByte(b);
    }

    private void WriteBuffer(byte[] buffer, int offset, int count)
    {
        if (Trace)
            Console.WriteLine($"  [TX {DateTime.Now:HH:mm:ss.fff}] frame {Convert.ToHexString(buffer, offset, count)}");
        _port.BaseStream.Write(buffer, offset, count);
    }

    public void Dispose()
    {
        if (_port.IsOpen) _port.Close();
        _port.Dispose();
    }
}
