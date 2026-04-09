using System;
using System.Text.Json;

namespace OpenClawPTT.Services;

public interface IGatewayService : IDisposable
{
    event Action<string>? AgentReplyFull;
    event Action? AgentReplyDeltaStart;
    event Action<string>? AgentReplyDelta;
    event Action? AgentReplyDeltaEnd;
    event Action<string>? AgentThinking;
    event Action<string, string>? AgentToolCall;
    event Action<string, JsonElement>? EventReceived;
    event Action<string>? AgentReplyAudio;

    Task ConnectAsync(CancellationToken ct = default);
    Task SendTextAsync(string text, CancellationToken ct = default);
    void RecreateWithConfig(AppConfig newConfig);
}