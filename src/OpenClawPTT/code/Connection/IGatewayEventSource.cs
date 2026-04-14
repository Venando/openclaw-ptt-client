using System;
using System.Text.Json;

namespace OpenClawPTT;

public interface IGatewayEventSource
{
    event Action<string, JsonElement>? EventReceived;
    event Action<string>? AgentReplyFull;
    event Action<string>? AgentReplyDelta;
    event Action? AgentReplyDeltaStart;
    event Action? AgentReplyDeltaEnd;
    event Action<string>? AgentThinking;
    event Action<string, string>? AgentToolCall;
    event Action<string>? AgentReplyAudio;
}
