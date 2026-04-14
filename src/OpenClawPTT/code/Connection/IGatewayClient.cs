using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OpenClawPTT;

/// <summary>
/// Abstracts the GatewayClient for testability.
/// All public members of GatewayClient are exposed through this interface.
/// </summary>
public interface IGatewayClient : IDisposable
{
    bool IsConnected { get; }
    string? SessionKey { get; }
    string? AgentId { get; }
    bool IsDisposed { get; }

    Task ConnectAsync(CancellationToken ct);
    Task DisconnectAsync(CancellationToken ct);
    Task<JsonElement> SendTextAsync(string body, CancellationToken ct);
    Task<JsonElement> SendAudioAsync(byte[] wavBytes, CancellationToken ct);
    Task<JsonElement> SendEventAsync(string eventName, object? parameters, CancellationToken ct);
    void RecreateWithConfig(AppConfig newConfig);
    IGatewayEventSource? GetEventSource();
}