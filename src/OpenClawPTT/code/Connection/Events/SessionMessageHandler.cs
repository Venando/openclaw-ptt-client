using System.Text.Json;
using OpenClawPTT.Services;

namespace OpenClawPTT;

/// <summary>
/// Handles session-related gateway events: session.message, agent, and chat.
/// Extracts content blocks (text, thinking, tool calls, audio) and fires
/// the appropriate events through <see cref="IGatewayEventSource"/>.
/// </summary>
public class SessionMessageHandler : IEventHandler<SessionMessageEvent>
{
    private readonly IGatewayEventSource _events;
    private readonly AppConfig _cfg;
    private readonly IContentExtractor _contentExtractor;
    private readonly IColorConsole _console;
    private readonly DeviceIdentity? _device;

    public SessionMessageHandler(
        IGatewayEventSource events,
        AppConfig cfg,
        IContentExtractor? contentExtractor = null,
        IColorConsole? console = null,
        DeviceIdentity? device = null)
    {
        _events = events;
        _cfg = cfg;
        _contentExtractor = contentExtractor ?? new ContentExtractor();
        _console = console ?? new ColorConsole(new StreamShellHost());
        _device = device;
    }

    public Task HandleAsync(SessionMessageEvent evt)
    {
        switch (evt.EventName)
        {
            case "session.message":
                HandleSessionMessage(evt.Payload);
                break;
            case "agent":
                HandleAgentStream(evt.Payload);
                break;
            case "chat":
                HandleChatFinal(evt.Payload);
                break;
        }

        return Task.CompletedTask;
    }

    private void HandleSessionMessage(JsonElement payload)
    {
        if (!payload.TryGetProperty("message", out var messageEl)) return;
        if (!messageEl.TryGetProperty("role", out var roleEl)) return;
        var role = roleEl.GetString();

        // Handle user messages from other nodes — display in real-time
        if (string.Equals(role, "user", StringComparison.OrdinalIgnoreCase))
        {
            HandleUserMessage(messageEl);
            return;
        }

        if (role != "assistant") return;
        if (!messageEl.TryGetProperty("content", out var contentEl)) return;
        if (contentEl.ValueKind != JsonValueKind.Array) return;

        // In realtime mode, HandleAgentStream owns the delta lifecycle (phase=start/end).
        // HandleSessionMessage fires content events only — no double delta framing.
        bool emitDelta = !_cfg.RealTimeReplyOutput;
        bool startFired = false;

        foreach (var block in contentEl.EnumerateArray())
        {
            if (!block.TryGetProperty("type", out var typeEl)) continue;
            var type = typeEl.GetString();

            if (type == "thinking" && block.TryGetProperty("thinking", out var thinkingEl))
            {
                var thinking = thinkingEl.GetString() ?? string.Empty;
                if (!string.IsNullOrEmpty(thinking))
                    _events.RaiseAgentThinking(thinking);
            }
            else if (type == "toolCall" && block.TryGetProperty("name", out var nameEl) && block.TryGetProperty("arguments", out var argsEl))
            {
                var toolName = nameEl.GetString() ?? string.Empty;
                var args = argsEl.GetRawText();
                _console.Log("debug", $"ToolCall: {toolName}({args})", LogLevel.Debug);
                _events.RaiseAgentToolCall(toolName, args);
            }
            else if (type == "text" && block.TryGetProperty("text", out var textEl))
            {
                var text = textEl.GetString() ?? string.Empty;
                if (!string.IsNullOrEmpty(text))
                {
                    var (hasAudio, hasText, audioText, textContent) = _contentExtractor.ExtractMarkedContent(text);
                    if (hasAudio)
                        _events.RaiseAgentReplyAudio(audioText);
                    if (emitDelta && !startFired)
                    {
                        _events.RaiseAgentReplyDeltaStart();
                        startFired = true;
                    }
                    if (hasText)
                        _events.RaiseAgentReplyFull(textContent);
                    else if (hasAudio)
                        _events.RaiseAgentReplyFull(_contentExtractor.StripAudioTags(text));
                }
            }
            else if (type == "audio" && block.TryGetProperty("audio", out var audioEl))
            {
                var audioText = audioEl.GetString() ?? string.Empty;
                if (!string.IsNullOrEmpty(audioText))
                {
                    if (emitDelta && !startFired)
                    {
                        _events.RaiseAgentReplyDeltaStart();
                        startFired = true;
                    }
                    _events.RaiseAgentReplyAudio(audioText);
                }
            }
            else
            {
                _console.Log("debug", $"Unknown block type=\"{type}\": {block}");
            }
        }

        if (emitDelta && startFired)
            _events.RaiseAgentReplyDeltaEnd();
    }

    private void HandleAgentStream(JsonElement payload)
    {
        if (!_cfg.RealTimeReplyOutput) return;
        if (!payload.TryGetProperty("data", out var data)) return;

        if (data.TryGetProperty("phase", out var phase))
        {
            var phaseType = phase.GetString() ?? string.Empty;
            if (phaseType == "start") _events.RaiseAgentReplyDeltaStart();
            if (phaseType == "end") _events.RaiseAgentReplyDeltaEnd();
        }

        if (data.TryGetProperty("delta", out var delta))
        {
            var chunk = delta.GetString() ?? string.Empty;
            if (!string.IsNullOrEmpty(chunk))
                _events.RaiseAgentReplyDelta(chunk);
        }
    }

    private void HandleChatFinal(JsonElement payload)
    {
        if (!payload.TryGetProperty("state", out var state)) return;
        if (state.GetString() != "final") return;

        if (_cfg.RealTimeReplyOutput)
        {
            // In realtime mode, streaming responses are already handled by HandleAgentStream
            // HandleChatFinal should be silent (no double End)
            return;
        }

        if (!payload.TryGetProperty("message", out var messageEl)) return;
        if (!messageEl.TryGetProperty("content", out var contentEl)) return;

        var text = ExtractFullText(contentEl);
        if (!string.IsNullOrEmpty(text))
        {
            _events.RaiseAgentReplyDeltaStart();
            _events.RaiseAgentReplyFull(text);
            _events.RaiseAgentReplyDeltaEnd();
        }
    }

    private static string ExtractFullText(JsonElement contentElement)
    {
        if (contentElement.ValueKind != JsonValueKind.Array)
            return string.Empty;

        var textParts = new List<string>();
        foreach (var item in contentElement.EnumerateArray())
        {
            if (item.TryGetProperty("type", out var typeElement) &&
                typeElement.GetString() == "text" &&
                item.TryGetProperty("text", out var textElement))
            {
                textParts.Add(textElement.GetString() ?? string.Empty);
            }
        }
        return string.Join("", textParts);
    }

    /// <summary>
    /// Handles a user message (role="user") from a <c>session.message</c> event.
    /// Extracts text content and fires <see cref="IGatewayEventSource.RaiseUserMessageReceived(string)"/>
    /// so that messages sent from other nodes are displayed in real-time.
    /// </summary>
    private void HandleUserMessage(JsonElement messageEl)
    {
        // Check sender metadata to avoid displaying our own echoed messages.
        // Each instance has a unique deviceId; messages from other nodes should be displayed.
        if (messageEl.TryGetProperty("sender", out var senderEl) &&
            senderEl.TryGetProperty("id", out var senderIdEl))
        {
            var senderId = senderIdEl.GetString() ?? "";
            if (senderId == _device?.ClientId)
                return; // Our own message — skip
        }

        if (!messageEl.TryGetProperty("content", out var contentEl))
            return;

        // Filter out internal/system messages with no meaningful user text
        var text = ExtractUserContentText(contentEl);
        if (string.IsNullOrWhiteSpace(text))
            return;

        if (!UserMessageHelper.HasUserInput(text))
            return;

        var userText = UserMessageHelper.ExtractUserMessage(text);
        if (string.IsNullOrWhiteSpace(userText))
            return;

        _events.RaiseUserMessageReceived(userText);
    }

    /// <summary>
    /// Extracts text content from a user message's content field,
    /// which may be a plain string or an array of content blocks.
    /// </summary>
    private static string ExtractUserContentText(JsonElement contentEl)
    {
        if (contentEl.ValueKind == JsonValueKind.String)
            return contentEl.GetString() ?? string.Empty;

        if (contentEl.ValueKind == JsonValueKind.Array)
        {
            var parts = new List<string>();
            foreach (var block in contentEl.EnumerateArray())
            {
                if (block.TryGetProperty("type", out var typeEl) &&
                    typeEl.GetString() == "text" &&
                    block.TryGetProperty("text", out var textEl))
                {
                    parts.Add(textEl.GetString() ?? string.Empty);
                }
            }
            return string.Join("", parts);
        }

        return string.Empty;
    }
}
