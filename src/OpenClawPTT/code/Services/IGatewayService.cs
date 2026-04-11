using System;
using System.Text.Json;

namespace OpenClawPTT.Services;

/// <summary>
/// Subset of IGatewayService events relevant for UI-layer adapters.
/// Allows UiEventAdapter to be tested in isolation without a real GatewayService.
/// </summary>
public interface IGatewayUIEvents
{
    event Action<string>? AgentReplyFull;
    event Action? AgentReplyDeltaStart;
    event Action<string>? AgentReplyDelta;
    event Action? AgentReplyDeltaEnd;
    event Action<string>? AgentThinking;
    event Action<string, string>? AgentToolCall;
    event Action<string, JsonElement>? EventReceived;
    event Action<string>? AgentReplyAudio;
}

public interface IGatewayService : IDisposable, IGatewayUIEvents
{
    Task ConnectAsync(CancellationToken ct = default);
    Task SendTextAsync(string text, CancellationToken ct = default);
    void RecreateWithConfig(AppConfig newConfig);
}