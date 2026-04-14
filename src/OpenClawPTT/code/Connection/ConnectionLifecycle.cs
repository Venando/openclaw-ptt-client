using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenClawPTT;

/// <summary>
/// Owns the WebSocket connection lifecycle: connect, receive pump, reconnection, and disconnect.
/// Coordinates with MessageFraming for framing/sending and SessionMessageHandler for event dispatch.
/// </summary>
public sealed class ConnectionLifecycle : ISender
{
    private readonly AppConfig _cfg;
    private readonly DeviceIdentity _dev;

    private IClientWebSocket _ws = null!;
    private CancellationTokenSource? _tickCts;
    private Task? _recvTask;

    private readonly SemaphoreSlim _reconnectLock = new SemaphoreSlim(1, 1);
    private bool _isReconnecting = false;
    private Task? _reconnectTask = null;
    private readonly CancellationTokenSource _disposeCts = new CancellationTokenSource();

    // ─── Dependencies ───────────────────────────────────────────────
    private readonly IGatewayEventSource _events;
    private MessageFraming? _framing;

    public ConnectionLifecycle(AppConfig cfg, DeviceIdentity dev, IGatewayEventSource events)
    {
        _cfg = cfg;
        _dev = dev;
        _events = events;
    }

    // ─── ISender ──────────────────────────────────────────────────────

    public async Task<JsonElement> SendRequestAsync(
        string method,
        object? parameters,
        CancellationToken ct,
        TimeSpan? timeout = null)
        => await _framing.SendRequestAsync(method, parameters, ct, timeout);

    // ─── Properties ──────────────────────────────────────────────────

    public bool IsConnected => _ws?.State == WebSocketState.Open;

    public IClientWebSocket? Socket => _ws;

    public MessageFraming GetFraming() => _framing!;

    // ─── connect ─────────────────────────────────────────────────────

    public async Task DisconnectAsync(CancellationToken ct)
    {
        await DisconnectInternalAsync(ct);
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

    public async Task ConnectAsync(CancellationToken ct)
    {
        // Clean up any existing connection before reconnecting
        await DisposeConnection(ct);

        _framing.ClearPendingRequests();
        _framing.ClearEventWaiters();

        _ws = new ClientWebSocketAdapter();
        _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
        _framing = new MessageFraming(_ws, _cfg);

        var uri = new Uri(_cfg.GatewayUrl);
        ConsoleUi.Log("gateway", $"Connecting to {uri} ...");
        await _ws.ConnectAsync(uri, ct);
        ConsoleUi.Log("gateway", "WebSocket open.");

        // start receive pump
        _recvTask = Task.Run(() => ReceiveLoop(ct), ct);

        // ── 1. wait for connect.challenge ──
        ConsoleUi.Log("gateway", "Waiting for connect.challenge ...");
        var challenge = await _framing.WaitForEventAsync("connect.challenge", TimeSpan.FromSeconds(10), ct);
        var nonce = challenge.GetProperty("nonce").GetString()!;

        // ── 2. build + sign connect request ──
        var authToken = _cfg.AuthToken ?? "";
        var token = _cfg.DeviceToken ?? _cfg.AuthToken ?? "";
        var scopes = new[] { "operator.read", "operator.write", "operator.approvals" };
        var platform = DeviceIdentity.GetPlatform().ToLowerInvariant();
        var deviceFamily = "desktop";
        var clientId = "cli";
        var mode = "cli";
        var signedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var sigPayload = _dev.BuildV3Payload(platform, deviceFamily, clientId, mode, "operator", scopes, signedAt, authToken, nonce);

        if (_cfg.LogConnect)
        {
            var redactedPayload = string.IsNullOrEmpty(authToken)
            ? sigPayload
            : sigPayload.Replace(authToken, "***REDACTED***");
            ConsoleUi.Log("gateway", $"Signature payload: {redactedPayload}");
        }

        var signature = _dev.Sign(sigPayload);

        var authDict = new Dictionary<string, object> { ["token"] = authToken };
        if (!string.IsNullOrEmpty(_cfg.DeviceToken))
            authDict["deviceToken"] = _cfg.DeviceToken;

        var connectParams = new Dictionary<string, object?>
        {
            ["minProtocol"] = 3,
            ["maxProtocol"] = 3,
            ["client"] = new Dictionary<string, object>
            {
                ["id"] = clientId,
                ["version"] = _cfg.ClientVersion,
                ["platform"] = platform,
                ["mode"] = mode,
                ["deviceFamily"] = deviceFamily
            },
            ["role"] = "operator",
            ["scopes"] = scopes,
            ["caps"] = new[] { "streaming", "stream.text", "agent.stream", "text.stream" },
            ["commands"] = Array.Empty<string>(),
            ["permissions"] = new Dictionary<string, object>(),
            ["auth"] = authDict,
            ["locale"] = _cfg.Locale,
            ["userAgent"] = $"openclaw-ptt-cli/{_cfg.ClientVersion}",
            ["device"] = new Dictionary<string, object>
            {
                ["id"] = _dev.DeviceId,
                ["publicKey"] = _dev.PublicKeyBase64,
                ["signature"] = signature,
                ["signedAt"] = signedAt,
                ["nonce"] = nonce
            }
        };

        if (_cfg.LogConnect)
        {
            LogMessage(connectParams);
        }

        ConsoleUi.Log("gateway", "Sending connect ...");
        JsonElement hello = await SendRequestAsync("connect", connectParams, ct);

        // ── 3. validate hello-ok ──
        var helloType = hello.TryGetProperty("type", out var htEl) ? htEl.GetString() : null;
        if (helloType != "hello-ok")
            throw new Exception($"Handshake rejected: {hello}");

        ConsoleUi.LogOk("gateway", "Authenticated — hello-ok received.");

        var options = new JsonSerializerOptions { WriteIndented = true };
        if (_cfg.LogHello)
        {
            string prettyHello = JsonSerializer.Serialize(hello, options);
            string extraPretty = Regex.Replace(prettyHello, "(?m)^(  )+", m => new string(' ', m.Length * 2));
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("--- SERVER HELLO PAYLOAD ---");
            Console.WriteLine(extraPretty);
            Console.WriteLine("----------------------------");
            Console.ResetColor();
        }

        // persist device token if issued
        if (hello.TryGetProperty("auth", out var authEl)
            && authEl.TryGetProperty("deviceToken", out var dtEl))
        {
            _cfg.DeviceToken = dtEl.GetString();
            new ConfigManager().Save(_cfg);
        }

        if (hello.TryGetProperty("snapshot", out var snapshot))
        {
            if (_cfg.LogHello)
            {
                string prettySpanshot = JsonSerializer.Serialize(snapshot, options);
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine("--- SERVER SNAPSHOT PAYLOAD ---");
                Console.WriteLine(prettySpanshot);
                Console.WriteLine("----------------------------");
                Console.ResetColor();
            }

            if (snapshot.TryGetProperty("sessionDefaults", out var defaults)
                && defaults.TryGetProperty("mainSessionKey", out var keyEl))
            {
                _cfg.SessionKey = keyEl.GetString();
                ConsoleUi.Log("gateway", $"Session initialized: {_cfg.SessionKey}");
            }
        }

        // ── 4. start keepalive ticks ──
        var tickMs = 15_000;
        if (hello.TryGetProperty("policy", out var pol)
            && pol.TryGetProperty("tickIntervalMs", out var tEl))
            tickMs = tEl.GetInt32();

        StartKeepalive(tickMs, ct);
        ConsoleUi.Log("gateway", $"Keepalive every {tickMs}ms.");

        var subscribeParams = new Dictionary<string, object?>
        {
            ["sessionKey"] = _cfg.SessionKey  // "agent:main:main"
        };

        await SendRequestAsync("sessions.subscribe", subscribeParams, ct);
    }

    private void LogMessage(Dictionary<string, object?> parameters)
    {
        var loggableParams = new Dictionary<string, object?>(parameters);

        if (loggableParams.TryGetValue("auth", out var authObj) && authObj is Dictionary<string, object> auth)
        {
            var sanitizedAuth = new Dictionary<string, object>();
            foreach (var (k, v) in auth)
                sanitizedAuth[k] = v is string s ? Redact(s) : v;
            loggableParams["auth"] = sanitizedAuth;
        }

        if (loggableParams.TryGetValue("device", out var deviceObj) && deviceObj is Dictionary<string, object> device)
        {
            var sanitizedDevice = new Dictionary<string, object>(device);
            if (device.TryGetValue("signature", out var sig) && sig is string sigStr)
                sanitizedDevice["signature"] = Redact(sigStr);
            if (device.TryGetValue("publicKey", out var pub) && pub is string pubStr)
                sanitizedDevice["publicKey"] = Redact(pubStr);
            loggableParams["device"] = sanitizedDevice;
        }

        var options = new JsonSerializerOptions { WriteIndented = true };
        ConsoleUi.Log("gateway", $"Sending request:\n{JsonSerializer.Serialize(loggableParams, options)}");
    }

    private static string Redact(string value)
    {
        if (value.Length <= 8) return "***";
        return $"{value[..4]}...{value[^4..]}";
    }

    // ─── receive pump ───────────────────────────────────────────────

    private async Task ReceiveLoop(CancellationToken ct)
    {
        var buf = new byte[256 * 1024];

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
                        ConsoleUi.Log("gateway", "Server closed connection.");
                        _ = HandleDisconnectionAsync(ct);
                        return;
                    }
                    ms.Write(buf, 0, result.Count);

                    if (result.Count == buf.Length)
                        ConsoleUi.LogError("gateway", $"WARNING: fragment filled buffer ({buf.Length} bytes) — consider increasing buffer size");

                } while (!result.EndOfMessage);

                var json = Encoding.UTF8.GetString(ms.ToArray());

                ProcessFrame(json);
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException ex)
        {
            ConsoleUi.LogError("gateway", $"WebSocket error: {ex.Message}");
            _ = HandleDisconnectionAsync(ct);
        }
        catch (Exception ex)
        {
            ConsoleUi.LogError("gateway", $"ReceiveLoop unexpected error: {ex.GetType().Name}: {ex.Message}");
            _ = HandleDisconnectionAsync(ct);
        }
    }

    private void ProcessFrame(string json)
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
        if (!_framing.TryRemovePending(id, out var tcs))
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

        switch (name)
        {
            case "session.message":
                HandleSessionMessage(payload);
                return;

            case "agent":
                HandleAgentStream(payload);
                return;

            case "chat":
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
                if (_cfg.DebugToolCalls) Console.WriteLine($"[DEBUG] ToolCall: {toolName}({args})");
                _events.RaiseAgentToolCall(toolName, args);
            }
            else if (type == "text" && block.TryGetProperty("text", out var textEl))
            {
                var text = textEl.GetString() ?? string.Empty;
                if (!string.IsNullOrEmpty(text))
                {
                    var (hasAudio, hasText, audioText, textContent) = ExtractMarkedContent(text);
                    if (hasAudio)
                        _events.RaiseAgentReplyAudio(audioText);
                    if (!startFired)
                    {
                        _events.RaiseAgentReplyDeltaStart();
                        startFired = true;
                    }
                    if (hasText)
                        _events.RaiseAgentReplyFull(textContent);
                    else if (hasAudio)
                        _events.RaiseAgentReplyFull(StripAudioTags(text));
                }
            }
            else if (type == "audio" && block.TryGetProperty("audio", out var audioEl))
            {
                var audioText = audioEl.GetString() ?? string.Empty;
                if (!string.IsNullOrEmpty(audioText))
                {
                    if (!startFired)
                    {
                        _events.RaiseAgentReplyDeltaStart();
                        startFired = true;
                    }
                    _events.RaiseAgentReplyAudio(audioText);
                }
            }
            else
            {
                Console.WriteLine($"[DEBUG] Unknown block type=\"{type}\": {block}");
            }
        }

        if (startFired)
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
            _events.RaiseAgentReplyDeltaEnd();
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

    private (bool hasAudio, bool hasText, string audioText, string textContent) ExtractMarkedContent(string fullMessage)
    {
        var audioText = string.Empty;
        var textContent = string.Empty;

        var audioMatch = Regex.Match(fullMessage, @"\[audio\](.*?)\[/audio\]", RegexOptions.Singleline);
        if (audioMatch.Success)
        {
            audioText = audioMatch.Groups[1].Value.Trim();
        }
        else
        {
            var openTagIndex = fullMessage.IndexOf("[audio]", StringComparison.OrdinalIgnoreCase);
            if (openTagIndex >= 0)
            {
                audioText = fullMessage.Substring(openTagIndex + 7).Trim();
            }
        }

        var textMatch = Regex.Match(fullMessage, @"\[text\](.*?)\[/text\]", RegexOptions.Singleline);
        if (textMatch.Success)
        {
            textContent = textMatch.Groups[1].Value.Trim();
        }
        else
        {
            var openTagIndex = fullMessage.IndexOf("[text]", StringComparison.OrdinalIgnoreCase);
            if (openTagIndex >= 0)
            {
                textContent = fullMessage.Substring(openTagIndex + 6).Trim();
            }
        }

        if (string.IsNullOrEmpty(audioText) && string.IsNullOrEmpty(textContent) && !string.IsNullOrEmpty(fullMessage))
        {
            textContent = fullMessage;
        }

        return (!string.IsNullOrEmpty(audioText), !string.IsNullOrEmpty(textContent), audioText, textContent);
    }

    private static string StripAudioTags(string text)
    {
        return Regex.Replace(text, @"\[audio\](.*?)\[/audio\]", "$1", RegexOptions.Singleline).Trim();
    }

    private void HandleApprovalRequest(JsonElement payload)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine();
        Console.WriteLine("  ⚠  Exec approval requested");

        if (payload.TryGetProperty("description", out var d))
            Console.WriteLine($"     {d.GetString()}");

        if (payload.TryGetProperty("command", out var cmd))
            Console.WriteLine($"     $ {cmd.GetString()}");

        Console.ResetColor();
        Console.WriteLine("     (auto-approving from PTT client)");

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
                    ConsoleUi.LogError("approval", ex.Message);
                }
            });
        }
    }

    // ─── keepalive ──────────────────────────────────────────────────

    private void StartKeepalive(int intervalMs, CancellationToken ct)
    {
        _tickCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var tickCt = _tickCts.Token;

        _ = Task.Run(async () =>
        {
            while (!tickCt.IsCancellationRequested)
            {
                await Task.Delay(intervalMs, tickCt);
                try
                {
                    if (_ws.State == WebSocketState.Open)
                        await SendRequestAsync("tick", null, tickCt, TimeSpan.FromSeconds(5));
                }
                catch (OperationCanceledException) { break; }
                catch { /* swallow tick failures */ }
            }
        }, tickCt);
    }

    // ─── connection resilience ───────────────────────────────────────

    private async Task DisconnectInternalAsync(CancellationToken ct)
    {
        _tickCts?.Cancel();
        _tickCts?.Dispose();
        _tickCts = null;

        if (_ws.State == WebSocketState.Open)
        {
            try
            {
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "disconnect", ct);
            }
            catch { /* ignore */ }
        }

        _framing.ClearPendingRequests();
        _framing.ClearEventWaiters();
    }

    private async Task HandleDisconnectionAsync(CancellationToken ct)
    {
        try
        {
            await DisconnectInternalAsync(ct);
            _ = ScheduleReconnectAsync(ct);
        }
        catch (Exception ex)
        {
            ConsoleUi.LogError("gateway", $"Error during disconnection handling: {ex.Message}");
        }
    }

    private async Task ScheduleReconnectAsync(CancellationToken ct)
    {
        await _reconnectLock.WaitAsync(ct);
        try
        {
            if (_isReconnecting) return;
        }
        finally
        {
            _reconnectLock.Release();
        }
        _isReconnecting = true; // set outside lock so ReconnectLoopAsync doesn't need to re-acquire
        ConsoleUi.Log("gateway", "Starting reconnection loop...");
        _reconnectTask = ReconnectLoopAsync(ct);
    }

    private async Task ReconnectLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var delayMs = (int)(_cfg.ReconnectDelaySeconds * 1000);
            ConsoleUi.Log("gateway", $"Waiting {_cfg.ReconnectDelaySeconds}s before reconnection attempt...");
            await Task.Delay(delayMs, ct);

            ConsoleUi.Log("gateway", "Attempting to reconnect...");
            try
            {
                await ConnectAsync(ct);
                ConsoleUi.LogOk("gateway", "Reconnected successfully.");
                break;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                ConsoleUi.LogError("gateway", $"Reconnection failed: {ex.Message}");
            }
        }

        _isReconnecting = false; // no lock needed: only one reconnect runs at a time
    }

    // ─── dispose connection ───────────────────────────────────────────

    private async Task DisposeConnection(CancellationToken ct)
    {
        if (_ws == null) return;

        try
        {
            if (_tickCts != null)
            {
                await _tickCts.CancelAsync();
                _tickCts.Dispose();
                _tickCts = null;
            }

            if (_ws.State == WebSocketState.Open || _ws.State == WebSocketState.CloseReceived)
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

                try
                {
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", linkedCts.Token);
                }
                catch (Exception)
                {
                    _ws.Abort();
                }
            }
        }
        finally
        {
            _ws.Dispose();
            _ws = null!;
        }
    }

    // ─── IDisposable ─────────────────────────────────────────────────

    public void Dispose()
    {
        try { _disposeCts.Cancel(); } catch (ObjectDisposedException) { /* already disposed */ }
        _reconnectTask?.Wait(TimeSpan.FromSeconds(5));

        _tickCts?.Cancel();
        _tickCts?.Dispose();
        _recvTask?.Wait(TimeSpan.FromSeconds(3));

        if (_ws != null && _ws.State == WebSocketState.Open)
        {
            try { _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).Wait(3000); }
            catch { /* best effort */ }
        }
        _ws?.Dispose();

        _reconnectLock.Dispose();
        try { _disposeCts.Dispose(); } catch (ObjectDisposedException) { /* already disposed */ }
    }
}
