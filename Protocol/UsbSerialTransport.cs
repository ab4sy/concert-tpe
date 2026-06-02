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
        _port = new SerialPort(portName, baudRate, Parity.Even, 7, StopBits.One)
        {
            ReadTimeout = (int)_timeout.TotalMilliseconds,
            WriteTimeout = (int)_timeout.TotalMilliseconds,
            Handshake = Handshake.None,
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

    private byte ReadByte(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        int b = _port.BaseStream.ReadByte();
        if (b < 0)
            throw new EndOfStreamException("Serial port closed.");
        return (byte)b;
    }

    private void WriteByte(byte b) => _port.BaseStream.WriteByte(b);

    private void WriteBuffer(byte[] buffer, int offset, int count) =>
        _port.BaseStream.Write(buffer, offset, count);

    public void Dispose()
    {
        if (_port.IsOpen) _port.Close();
        _port.Dispose();
    }
}
