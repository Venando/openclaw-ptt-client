using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using OpenClawPTT.Services;

namespace OpenClawPTT;

public class GatewayMessager : IDisposable
{
    private readonly IClientWebSocket _ws;
    private readonly IGatewayEventSource _events;
    private readonly AppConfig _cfg;
    private readonly Func<MessageFraming>? _framingFactory;
    private readonly MessageFraming _framing;
    private readonly Action<CancellationToken>? _onDisconnection;
    private readonly IContentExtractor _contentExtractor;
    private readonly IColorConsole _console;

    public IMessageFraming GetFraming() => _framing;

    public GatewayMessager(
        IClientWebSocket ws,
        IGatewayEventSource events,
        AppConfig cfg,
        Action<CancellationToken>? onDisconnection = null,
        Func<MessageFraming>? framingFactory = null,
        IContentExtractor? contentExtractor = null,
        IColorConsole? console = null)
    {
        _ws = ws;
        _cfg = cfg;
        _events = events;
        _framingFactory = framingFactory;
        _framing = _framingFactory != null ? _framingFactory() : new MessageFraming(_ws, _cfg);
        _onDisconnection = onDisconnection;
        _contentExtractor = contentExtractor ?? new ContentExtractor();
        _console = console ?? new ColorConsole(new StreamShellHost());
    }

    public async Task ReceiveLoop(CancellationToken ct)
    {
        var buf = new byte[512 * 1024];

        try
        {
            while (!ct.IsCancellationRequested && _ws.State == WebSocketState.Open)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await _ws.ReceiveAsync(buf, ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _console.Log("gateway", "Server closed connection.");
                        _onDisconnection?.Invoke(ct);
                        return;
                    }
                    ms.Write(buf, 0, result.Count);

                    if (result.Count == buf.Length)
                        _console.LogError("gateway", $"WARNING: fragment filled buffer ({buf.Length} bytes) — consider increasing buffer size");

                } while (!result.EndOfMessage);

                var json = Encoding.UTF8.GetString(ms.ToArray());

                ProcessFrame(json);
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation is orchestrated from above (via _disposeCts linked token).
            // No reconnection needed — dispose path handles cleanup.
        }
        catch (WebSocketException ex)
        {
            _console.LogError("gateway", $"WebSocket error: {ex.Message}");
            _onDisconnection?.Invoke(ct);
        }
        catch (Exception ex)
        {
            _console.LogError("gateway", $"ReceiveLoop unexpected error: {ex.GetType().Name}: {ex.Message}");
            _onDisconnection?.Invoke(ct);
        }
    }

    public void ProcessFrame(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var type = root.GetProperty("type").GetString();

        switch (type)
        {
            case "res":
                HandleResponse(root);
                break;
            case "event":
                HandleEvent(root);
                break;
        }
    }

    private void HandleResponse(JsonElement root)
    {
        var id = root.GetProperty("id").GetString()!;
        if (!_framing.TryRemovePending(id, out var tcs) || tcs == null)
            return;

        var ok = root.GetProperty("ok").GetBoolean();
        if (ok)
        {
            tcs.SetResult(root.TryGetProperty("payload", out var p)
                ? p.Clone()
                : default);
        }
        else
        {
            var err = root.TryGetProperty("error", out var e)
                ? e.Clone().ToString()
                : "unknown error";
            tcs.SetException(new GatewayException(err, root.Clone()));
        }
    }

    private void HandleEvent(JsonElement root)
    {
        var name = root.GetProperty("event").GetString()!;
        var payload = root.TryGetProperty("payload", out var p) ? p.Clone() : default;

        // resolve one-shot waiter via MessageFraming (skip if _framing not yet initialized)
        if (_framing != null)
            _framing.ResolveEventWaiter(name, payload);

        // Filter messages not belonging to the active agent session
        if (payload.TryGetProperty("sessionKey", out JsonElement sessionKeyEl))
        {
            var msgSessionKey = sessionKeyEl.GetString();
            if (!AgentRegistry.IsMessageForActiveSession(msgSessionKey))
                return;
        }

        switch (name)
        {
            case "session.message":
                _events.RaiseEventReceived(name, payload);
                HandleSessionMessage(payload);
                return;

            case "agent":
                _events.RaiseEventReceived(name, payload);
                HandleAgentStream(payload);
                return;

            case "chat":
                _events.RaiseEventReceived(name, payload);
                HandleChatFinal(payload);
                return;

            default:
                _events.RaiseEventReceived(name, payload);
                if (name == "exec.approval.requested")
                    HandleApprovalRequest(payload);
                return;
        }
    }

    private void HandleSessionMessage(JsonElement payload)
    {
        if (!payload.TryGetProperty("message", out var messageEl)) return;
        if (!messageEl.TryGetProperty("role", out var roleEl)) return;
        if (roleEl.GetString() != "assistant") return;
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
                if (_cfg.DebugToolCalls) _console.Log("debug", $"ToolCall: {toolName}({args})");
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

    private string ExtractFullText(JsonElement contentElement)
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

    private void HandleApprovalRequest(JsonElement payload)
    {
        _console.PrintWarning("Exec approval requested");

        if (payload.TryGetProperty("description", out var d))
            _console.PrintInfo($"    {d.GetString()}");

        if (payload.TryGetProperty("command", out var cmd))
            _console.PrintInfo($"    $ {cmd.GetString()}");
        _console.PrintInfo("(auto-approving from PTT client)");

        if (payload.TryGetProperty("id", out var idEl))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await SendRequestAsync("exec.approval.resolve", new Dictionary<string, object?>
                    {
                        ["id"] = idEl.GetString(),
                        ["approved"] = true
                    }, CancellationToken.None, TimeSpan.FromSeconds(10));
                }
                catch (Exception ex)
                {
                    _console.LogError("approval", ex.Message);
                }
            });
        }
    }


    public async Task<JsonElement> SendRequestAsync(
        string method,
        object? parameters,
        CancellationToken ct,
        TimeSpan? timeout = null)
        => await _framing.SendRequestAsync(method, parameters, ct, timeout);

    public void ClearFraming()
    {
        _framing?.ClearPendingRequests();
        _framing?.ClearEventWaiters();
    }

    public void Dispose()
    {
        ClearFraming();
    }

    // ─── test support ──────────────────────────────────────────────

    internal void TestProcessFrame(string json) => ProcessFrame(json);

    internal void TestHandleEvent(string eventJson)
    {
        using var doc = JsonDocument.Parse(eventJson);
        HandleEvent(doc.RootElement);
    }

    internal void TestHandleSessionMessage(string payloadJson)
    {
        using var doc = JsonDocument.Parse(payloadJson);
        HandleSessionMessage(doc.RootElement);
    }

    internal string TestStripAudioTags(string text) => _contentExtractor.StripAudioTags(text);

    internal (bool hasAudio, bool hasText, string audioText, string textContent) TestExtractMarkedContent(string fullMessage)
        => _contentExtractor.ExtractMarkedContent(fullMessage);

}