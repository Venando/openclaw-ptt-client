using System;
using System.Text.Json;

namespace OpenClawPTT;

public sealed class GatewayEventSource : IGatewayEventSource
{
    public event Action<string, JsonElement>? EventReceived;
    public event Action<string>? AgentReplyFull;
    public event Action<string>? AgentReplyDelta;
    public event Action? AgentReplyDeltaStart;
    public event Action? AgentReplyDeltaEnd;
    public event Action<string>? AgentThinking;
    public event Action<string, string>? AgentToolCall;
    public event Action<string>? AgentReplyAudio;
}
