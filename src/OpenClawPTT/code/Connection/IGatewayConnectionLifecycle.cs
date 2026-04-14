using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OpenClawPTT;

/// <summary>
/// Abstracts GatewayConnectionLifecycle for testability.
/// </summary>
public interface IGatewayConnectionLifecycle : IDisposable
{
    bool IsConnected { get; }
    MessageFraming? GetFraming();
    Task<JsonElement> SendRequestAsync(string method, object? parameters, CancellationToken ct, TimeSpan? timeout = null);
    Task DisconnectAsync(CancellationToken ct);
    Task ConnectAsync(CancellationToken ct);
}
