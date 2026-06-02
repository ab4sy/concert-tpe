using System.Net.Sockets;
using System.Text;

namespace CaisseApIpTester.Protocol;

/// <summary>
/// TCP bridge to a payment terminal. Uses the same STX/ETX/LRC framing and,
/// when supported by the gateway, the CONCERT ENQ/ACK/EOT session.
/// </summary>
public sealed class CaisseApIpClient : ITransport
{
    public string Host { get; }
    public int Port { get; }
    public TimeSpan Timeout { get; }

    private const int BufferSize = 4096;

    public CaisseApIpClient(string host, int port, TimeSpan? timeout = null)
    {
        Host = host;
        Port = port;
        Timeout = timeout ?? TimeSpan.FromSeconds(180);
    }

    public async Task<string> SendAsync(string payload, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Timeout);

        using var tcp = new TcpClient();
        tcp.ReceiveTimeout = (int)Timeout.TotalMilliseconds;
        tcp.SendTimeout = (int)Timeout.TotalMilliseconds;

        Console.WriteLine($"[TCP] Connecting to {Host}:{Port}...");
        await tcp.ConnectAsync(Host, Port, cts.Token);
        Console.WriteLine("[TCP] Connected.");

        using var stream = tcp.GetStream();

        var link = new ConcertLinkLayer(
            _ => ReadByteBlocking(stream, cts.Token),
            b => stream.WriteByte(b),
            (buf, off, len) => stream.Write(buf, off, len));

        Console.WriteLine($"[TCP] Emitting A4 request ({payload.Length} chars)...");
        await Task.Run(() => link.EmitSecondary(payload, cts.Token), cts.Token);

        Console.WriteLine("[TCP] Waiting for TPE response...");
        string response = await Task.Run(
            () => link.Receive(cts.Token, (int)Timeout.TotalMilliseconds),
            cts.Token);

        Console.WriteLine($"[TCP] Received A4 response ({response.Length} chars).");
        return response;
    }

    private static byte ReadByteBlocking(NetworkStream stream, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        byte[] one = new byte[1];
        int read = stream.Read(one, 0, 1);
        if (read == 0)
            throw new EndOfStreamException("TCP connection closed.");
        return one[0];
    }

    public void Dispose() { }
}
