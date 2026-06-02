namespace CaisseApIpTester.Protocol;

public interface ITransport : IDisposable
{
    Task<string> SendAsync(string frame, CancellationToken ct = default);
}
