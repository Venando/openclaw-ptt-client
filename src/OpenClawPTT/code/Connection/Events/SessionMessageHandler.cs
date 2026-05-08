using System.Text.Json;
using OpenClawPTT.Services;

namespace OpenClawPTT;

/// <summary>
/// Handles session-related gateway events: session.message, agent, and chat.
/// Extracts content blocks (text, thinking, tool calls, audio) and fires
/// the appropriate events through <see cref="IGatewayEventSource"/>.
/// Also detects phase=error lifecycle events and stopReason=error messages,
/// surfacing provider failures and fallback information.
/// </summary>
public class SessionMessageHandler : IEventHandler<SessionMessageEvent>
{
    private readonly IGatewayEventSource _events;
    private readonly AppConfig _cfg;
    private readonly IContentExtractor _contentExtractor;
    private readonly IColorConsole _console;

    public SessionMessageHandler(
        IGatewayEventSource events,
        AppConfig cfg,
        IContentExtractor? contentExtractor = null,
        IColorConsole? console = null)
    {
        _events = events;
        _cfg = cfg;
        _contentExtractor = contentExtractor ?? new ContentExtractor();
        _console = console ?? new ColorConsole(new StreamShellHost());
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
        if (roleEl.GetString() != "assistant") return;

        // Check for error messages (stopReason=error, content may be empty)
        if (messageEl.TryGetProperty("stopReason", out var stopReason) && stopReason.GetString() == "error")
        {
            HandleErrorMessage(messageEl);
            return;
        }

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

    /// <summary>
    /// Handles an error response from the agent (stopReason=error).
    /// The lifecycle agent event (phase=error) already displays the error
    /// prominently — this is a secondary data source with extra metadata
    /// (provider, model). Log at debug to avoid duplicate display.
    /// </summary>
    private void HandleErrorMessage(JsonElement messageEl)
    {
        var errorMessage = messageEl.TryGetProperty("errorMessage", out var errEl)
            ? errEl.GetString() ?? "Unknown error"
            : "Unknown error";
        var provider = messageEl.TryGetProperty("provider", out var provEl)
            ? provEl.GetString() : null;
        var model = messageEl.TryGetProperty("model", out var modEl)
            ? modEl.GetString() : null;

        var providerModel = provider != null ? $"{provider}/{model}" : "?";
        _console.Log("debug", $"{providerModel} stopReason=error: {errorMessage}", LogLevel.Debug);
    }

    private void HandleAgentStream(JsonElement payload)
    {
        if (!payload.TryGetProperty("data", out var data)) return;

        // Handle lifecycle events
        if (data.TryGetProperty("phase", out var phase))
        {
            var phaseType = phase.GetString() ?? string.Empty;

            if (phaseType == "start")
            {
                if (_cfg.RealTimeReplyOutput)
                    _events.RaiseAgentReplyDeltaStart();
            }
            else if (phaseType == "end")
            {
                if (_cfg.RealTimeReplyOutput)
                    _events.RaiseAgentReplyDeltaEnd();
            }
            else if (phaseType == "error")
            {
                // Gateway sends lifecycle error events to WebSocket clients.
                // Show the error message immediately regardless of reply mode.
                var errorMsg = data.TryGetProperty("error", out var e)
                    ? e.GetString() ?? "Unknown agent error"
                    : "Unknown agent error";

                _console.Log("debug", $"Agent lifecycle error: {errorMsg}", LogLevel.Debug);

                if (IsQuotaError(errorMsg))
                {
                    _console.PrintModelFailed(errorMsg);
                }
                else if (errorMsg.Contains("fallback", StringComparison.OrdinalIgnoreCase) ||
                         errorMsg.Contains("failover", StringComparison.OrdinalIgnoreCase))
                {
                    _console.PrintWarning($"Agent: {errorMsg}");
                }
                else
                {
                    _console.PrintError($"Agent error: {errorMsg}");

                    // Also notify adapter for display
                    if (_cfg.RealTimeReplyOutput)
                    {
                        _events.RaiseAgentReplyDeltaStart();
                        _events.RaiseAgentReplyFull(errorMsg);
                        _events.RaiseAgentReplyDeltaEnd();
                    }
                }
            }
        }

        // Handle streaming delta (only in realtime mode)
        if (_cfg.RealTimeReplyOutput && data.TryGetProperty("delta", out var delta))
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

    private static bool IsQuotaError(string message)
    {
        return message.Contains("usage limit", StringComparison.OrdinalIgnoreCase)
            || message.Contains("quota", StringComparison.OrdinalIgnoreCase)
            || message.Contains("insufficient funds", StringComparison.OrdinalIgnoreCase)
            || message.Contains("billing cycle", StringComparison.OrdinalIgnoreCase)
            || message.Contains("billing error", StringComparison.OrdinalIgnoreCase)
            || message.Contains("insufficient balance", StringComparison.OrdinalIgnoreCase);
    }
}
