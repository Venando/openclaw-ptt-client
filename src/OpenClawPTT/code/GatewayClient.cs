using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace OpenClawPTT;

public sealed class GatewayClient : IDisposable
{
    private readonly AppConfig _cfg;
    private readonly DeviceIdentity _dev;
    private ClientWebSocket _ws = null!;
    private CancellationTokenSource? _tickCts;
    private CancellationTokenSource? _recvCts;
    private Task? _recvTask;

    // pending request → response futures
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new();

    // one-shot event waiters (event name → future)
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _eventWaiters = new();

    private int _idCounter;
    private readonly SemaphoreSlim _reconnectLock = new SemaphoreSlim(1, 1);
    private bool _isReconnecting = false;
    private readonly CancellationTokenSource _disposeCts = new CancellationTokenSource();
    private Task? _reconnectTask = null;
    private bool _disposed = false;

    /// <summary>Fires for every inbound event (event name, payload).</summary>
    public event Action<string, JsonElement>? EventReceived;

    /// <summary>Fires when the agent sends a chat reply (body text).</summary>
    public event Action<string>? AgentReplyFull;

    /// <summary>Fires when the agent sends a chat reply (body text).</summary>
    public event Action<string>? AgentReplyDelta;
    public event Action? AgentReplyDeltaStart;
    public event Action? AgentReplyDeltaEnd;
    public event Action<string>? AgentThinking;

    /// <summary>Fires when the agent calls a tool (e.g. read, git, etc.).</summary>
    public event Action<string, string>? AgentToolCall; // (toolName, arguments)

    /// <summary>Fires when agent response contains [audio] marker with text for TTS.</summary>
    public event Action<string>? AgentReplyAudio;

    public GatewayClient(AppConfig cfg, DeviceIdentity dev)
    {
        _cfg = cfg;
        _dev = dev;
    }

    // ─── connect ────────────────────────────────────────────────────

    public async Task ConnectAsync(CancellationToken ct)
    {
        if (_ws != null)
        {
            _tickCts?.Cancel();
            _tickCts?.Dispose();
            _tickCts = null;

            if (_ws.State == WebSocketState.Open)
            {
                try { await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "reconnect", ct); }
                catch { }
            }
            _ws.Dispose();
            _ws = null!;
        }

        ClearPendingRequests();

        _ws = new ClientWebSocket();
        _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);

        var uri = new Uri(_cfg.GatewayUrl);
        ConsoleUi.Log("gateway", $"Connecting to {uri} ...");
        using var linkCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
        await _ws.ConnectAsync(uri, linkCts.Token);
        ConsoleUi.Log("gateway", $"[DEBUG] WebSocket state after connect: {_ws.State}");
        ConsoleUi.Log("gateway", $"[DEBUG] _disposeCts.IsCancellationRequested: {_disposeCts.IsCancellationRequested}");
        ConsoleUi.Log("gateway", "WebSocket open.");

        // Cancel and await any previous ReceiveLoop before launching a new one.
        var prevTask = _recvTask;
        _recvCts?.Cancel();
        if (prevTask != null)
            try { await prevTask.WaitAsync(TimeSpan.FromSeconds(1)); } catch { /* best effort */ }

        _recvCts = new CancellationTokenSource();
        _recvTask = Task.Run(() => ReceiveLoop(linkCts.Token), _recvCts.Token);

        // ── handshake ──
        Console.WriteLine("[DEBUG] ConnectAsync: reached ExecuteAsync call");
        int tickMs;
        string? sessionKey;
        try {
            Console.WriteLine("[DEBUG] ConnectAsync: about to call ExecuteAsync");
            var handler = new HandshakeHandler(_cfg, _dev, _ws, SendRequestAsync, WaitForEventAsync, LogMessage);
            (tickMs, sessionKey) = await handler.ExecuteAsync(ct);
            Console.WriteLine($"[DEBUG] ConnectAsync: ExecuteAsync completed, tickMs={tickMs}");
        } catch (Exception ex) {
            Console.WriteLine($"[DEBUG] ConnectAsync EXCEPTION from ExecuteAsync: {ex.GetType().Name}: {ex.Message}");
            throw;
        }
        _cfg.SessionKey = sessionKey ?? _cfg.SessionKey;

        // ── keepalive ──
        StartKeepalive(tickMs);
        ConsoleUi.Log("gateway", $"Keepalive every {tickMs}ms.");

        // ── subscribe ──
        await SendRequestAsync("sessions.subscribe", new Dictionary<string, object?> { ["sessionKey"] = _cfg.SessionKey }, ct);
    }

    private async Task<JsonElement> SendRequestAsync(string method, object? parameters, CancellationToken ct, TimeSpan? timeout = null)
    {
        var id = NextId();
        var frame = new Dictionary<string, object?>
        {
            ["type"] = "req",
            ["id"] = id,
            ["method"] = method,
            ["params"] = parameters
        };

        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        var json = JsonSerializer.Serialize(frame);
        var buf = Encoding.UTF8.GetBytes(json);
        await _ws.SendAsync(buf, WebSocketMessageType.Text, true, ct);

        var wait = timeout ?? TimeSpan.FromSeconds(30);
        using var timeCts = new CancellationTokenSource(wait);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeCts.Token);

        try
        {
            await using (linked.Token.Register(() => tcs.TrySetCanceled()))
                return await tcs.Task;
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private async Task<JsonElement> WaitForEventAsync(string eventName, TimeSpan timeout, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _eventWaiters[eventName] = tcs;

        using var timeCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeCts.Token);

        await using (linked.Token.Register(() => tcs.TrySetCanceled()))
        {
            try { Console.WriteLine($"[DEBUG] WaitForEventAsync resolved: {eventName}"); return await tcs.Task; }
            finally { _eventWaiters.TryRemove(eventName, out _); }
        }
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

    private string NextId() => $"ptt-{Interlocked.Increment(ref _idCounter):D6}";

    // ─── connection resilience ──────────────────────────────────────

    private async Task DisconnectInternalAsync(CancellationToken ct)
    {
        _tickCts?.Cancel();
        _tickCts?.Dispose();
        _tickCts = null;

        _recvCts?.Cancel();
        _recvCts?.Dispose();
        _recvCts = null;

        if (_ws.State == WebSocketState.Open)
        {
            try { await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "disconnect", ct); }
            catch { /* ignore */ }
        }

        ClearPendingRequests();
    }

    private void ClearPendingRequests()
    {
        foreach (var kvp in _pending) kvp.Value.TrySetCanceled();
        _pending.Clear();
        foreach (var kvp in _eventWaiters) kvp.Value.TrySetCanceled();
        _eventWaiters.Clear();
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
            _isReconnecting = true;
            ConsoleUi.Log("gateway", "Starting reconnection loop...");
            _reconnectTask = ReconnectLoopAsync(ct);
        }
        finally { _reconnectLock.Release(); }
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
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                ConsoleUi.LogError("gateway", $"Reconnection failed: {ex.Message}");
            }
        }

        await _reconnectLock.WaitAsync(ct);
        try { _isReconnecting = false; }
        finally { _reconnectLock.Release(); }
    }

    // ─── keepalive ──────────────────────────────────────────────────

    private void StartKeepalive(int intervalMs)
    {
        _tickCts = new CancellationTokenSource();
        var ct = _tickCts.Token;

        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(intervalMs, ct);
                try
                {
                    if (_ws.State == WebSocketState.Open)
                        await SendRequestAsync("tick", null, ct, TimeSpan.FromSeconds(5));
                }
                catch (OperationCanceledException) { break; }
                catch { /* swallow tick failures */ }
            }
        }, ct);
    }

    // ─── send audio ─────────────────────────────────────────────────

    public async Task<JsonElement> SendAudioAsync(byte[] wavBytes, CancellationToken ct)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"voice_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.wav");
        await File.WriteAllBytesAsync(tempPath, wavBytes, ct);

        var sessionKey = !string.IsNullOrEmpty(_cfg.SessionKey) ? _cfg.SessionKey : "main";
        var chatParams = new Dictionary<string, object?>
        {
            ["sessionKey"] = sessionKey,
            ["idempotencyKey"] = Guid.NewGuid().ToString(),
            ["message"] = $"file://{tempPath}"
        };

        return await SendRequestAsync("chat.send", chatParams, ct);
    }

    /// <summary>Send a text message via chat.send.</summary>
    public async Task<JsonElement> SendTextAsync(string body, CancellationToken ct)
    {
        var sessionKey = !string.IsNullOrEmpty(_cfg.SessionKey) ? _cfg.SessionKey : "main";
        var chatParams = new Dictionary<string, object?>
        {
            ["sessionKey"] = sessionKey,
            ["idempotencyKey"] = Guid.NewGuid().ToString(),
            ["message"] = body
        };

        return await SendRequestAsync("chat.send", chatParams, ct);
    }

    // ─── receive pump ───────────────────────────────────────────────

    private async Task ReceiveLoop(CancellationToken ct)
    {
        var router = new MessageRouter(_cfg, _pending, _eventWaiters, this);
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
                        ConsoleUi.LogError("gateway", $"WARNING: fragment filled buffer ({buf.Length} bytes)");

                } while (!result.EndOfMessage);

                var json = Encoding.UTF8.GetString(ms.ToArray());
                ConsoleUi.Log("gateway", $"[DEBUG] Frame received, json preview: {json[..Math.Min(200, json.Length)]}");
                router.ProcessFrame(json);
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _disposeCts.Cancel();
        _reconnectTask?.Wait(TimeSpan.FromSeconds(5));

        _tickCts?.Cancel();
        _tickCts?.Dispose();

        _recvCts?.Cancel();
        _recvCts?.Dispose();

        if (_ws != null && _ws.State == WebSocketState.Open)
        {
            try { _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).Wait(3000); }
            catch { /* best effort */ }
            _ws.Dispose();
        }

        _reconnectLock.Dispose();
        _disposeCts.Dispose();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  HandshakeHandler — connect.challenge → connect flow, auth params, signature
    // ═══════════════════════════════════════════════════════════════════

    private sealed class HandshakeHandler
    {
        private readonly AppConfig _cfg;
        private readonly DeviceIdentity _dev;
        private readonly ClientWebSocket _ws;
        private readonly Func<string, object?, CancellationToken, TimeSpan?, Task<JsonElement>> _sendRequest;
        private readonly Func<string, TimeSpan, CancellationToken, Task<JsonElement>> _waitForEvent;
        private readonly Action<Dictionary<string, object?>> _logMessage;

        public HandshakeHandler(
            AppConfig cfg,
            DeviceIdentity dev,
            ClientWebSocket ws,
            Func<string, object?, CancellationToken, TimeSpan?, Task<JsonElement>> sendRequest,
            Func<string, TimeSpan, CancellationToken, Task<JsonElement>> waitForEvent,
            Action<Dictionary<string, object?>> logMessage)
        {
            _cfg = cfg;
            _dev = dev;
            _ws = ws;
            _sendRequest = sendRequest;
            _waitForEvent = waitForEvent;
            _logMessage = logMessage;
        }

        internal async Task<(int tickMs, string? sessionKey)> ExecuteAsync(CancellationToken ct)
        {
            // 1. wait for connect.challenge
            ConsoleUi.Log("gateway", "Waiting for connect.challenge ...");
            Console.WriteLine("[DEBUG] ExecuteAsync: calling _waitForEvent for connect.challenge");
            var challenge = await _waitForEvent("connect.challenge", TimeSpan.FromSeconds(10), ct);
            Console.WriteLine("[DEBUG] ExecuteAsync: _waitForEvent returned, about to extract nonce");
            var nonce = challenge.GetProperty("nonce").GetString()!;
            Console.WriteLine($"[DEBUG] ExecuteAsync ENTRY, nonce={nonce}");

            // 2. build + sign connect request
            var authToken = _cfg.AuthToken ?? "";
            var scopes = new[] { "operator.read", "operator.write", "operator.approvals" };
            var platform = DeviceIdentity.GetPlatform().ToLowerInvariant();
            var deviceFamily = "desktop";
            var clientId = "cli";
            var mode = "cli";
            var signedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Console.WriteLine("[DEBUG] ExecuteAsync: about to build signature");
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

            if (_cfg.LogConnect) _logMessage(connectParams);

            ConsoleUi.Log("gateway", "Sending connect ...");
            Console.WriteLine($"[DEBUG] ExecuteAsync: about to send connect with nonce={nonce}, authToken prefix={authToken[..Math.Min(8, authToken.Length)]}");
            JsonElement hello = await _sendRequest("connect", connectParams, ct, null);

            // 3. validate hello-ok
            var helloType = hello.TryGetProperty("type", out var htEl) ? htEl.GetString() : null;
            if (helloType != "hello-ok")
                throw new Exception($"Handshake rejected: {hello}");

            ConsoleUi.LogOk("gateway", "Authenticated — hello-ok received.");

            var options = new JsonSerializerOptions { WriteIndented = true };
            if (_cfg.LogHello)
            {
                var prettyHello = JsonSerializer.Serialize(hello, options);
                var extraPretty = System.Text.RegularExpressions.Regex.Replace(prettyHello, "(?m)^(  )+", m => new string(' ', m.Length * 2));
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine("--- SERVER HELLO PAYLOAD ---");
                Console.WriteLine(extraPretty);
                Console.WriteLine("----------------------------");
                Console.ResetColor();
            }

            if (hello.TryGetProperty("auth", out var authEl)
                && authEl.TryGetProperty("deviceToken", out var dtEl))
            {
                _cfg.DeviceToken = dtEl.GetString();
                new ConfigManager().Save(_cfg);
            }

            string? sessionKey = null;
            if (hello.TryGetProperty("snapshot", out var snapshot))
            {
                if (_cfg.LogHello)
                {
                    var prettySnapshot = JsonSerializer.Serialize(snapshot, options);
                    Console.ForegroundColor = ConsoleColor.Magenta;
                    Console.WriteLine("--- SERVER SNAPSHOT PAYLOAD ---");
                    Console.WriteLine(prettySnapshot);
                    Console.WriteLine("----------------------------");
                    Console.ResetColor();
                }

                if (snapshot.TryGetProperty("sessionDefaults", out var defaults)
                    && defaults.TryGetProperty("mainSessionKey", out var keyEl))
                {
                    sessionKey = keyEl.GetString();
                    ConsoleUi.Log("gateway", $"Session initialized: {sessionKey}");
                }
            }

            var tickMs = 15_000;
            if (hello.TryGetProperty("policy", out var pol)
                && pol.TryGetProperty("tickIntervalMs", out var tEl))
                tickMs = tEl.GetInt32();

            return (tickMs, sessionKey);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  MessageRouter — ProcessFrame, HandleEvent, HandleResponse
    // ═══════════════════════════════════════════════════════════════════

    private sealed class MessageRouter
    {
        private readonly AppConfig _cfg;
        private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending;
        private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _eventWaiters;
        private readonly GatewayClient _outer;

        public MessageRouter(
            AppConfig cfg,
            ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> pending,
            ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> eventWaiters,
            GatewayClient outer)
        {
            _cfg = cfg;
            _pending = pending;
            _eventWaiters = eventWaiters;
            _outer = outer;
        }

        public void ProcessFrame(string json)
        {
            Console.WriteLine($"[DEBUG] ProcessFrame: {json[..Math.Min(200, json.Length)]}");
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString();

            switch (type)
            {
                case "res": HandleResponse(root); break;
                case "event": HandleEvent(root); break;
            }
        }

        private void HandleResponse(JsonElement root)
        {
            var id = root.GetProperty("id").GetString()!;
            if (!_pending.TryRemove(id, out var tcs)) return;

            var ok = root.GetProperty("ok").GetBoolean();
            if (ok)
            {
                tcs.SetResult(root.TryGetProperty("payload", out var p) ? p.Clone() : default);
            }
            else
            {
                var err = root.TryGetProperty("error", out var e) ? e.Clone().ToString() : "unknown error";
                tcs.SetException(new GatewayException(err, root.Clone()));
            }
        }

        private void HandleEvent(JsonElement root)
        {
            var name = root.GetProperty("event").GetString()!;
            Console.WriteLine($"[DEBUG] HandleEvent ENTRY: {name}");
            var payload = root.TryGetProperty("payload", out var p) ? p.Clone() : default;

            if (_eventWaiters.TryRemove(name, out var tcs))
                tcs.TrySetResult(payload);

            Console.WriteLine($"[DEBUG] HANDLEEVENT {name}");
            if (name == "connect.challenge")
                Console.WriteLine($"[DEBUG] HANDLEEVENT connect.challenge nonce={payload.GetProperty("nonce").GetString()}");

            switch (name)
            {
                case "session.message": HandleSessionMessage(payload); return;
                case "agent": HandleAgentStream(payload); return;
                case "chat": HandleChatFinal(payload); return;
                default:
                    _outer.EventReceived?.Invoke(name, payload);
                    if (name == "exec.approval.requested") HandleApprovalRequest(payload);
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
                        _outer.AgentThinking?.Invoke(thinking);
                }
                else if (type == "toolCall" && block.TryGetProperty("name", out var nameEl) && block.TryGetProperty("arguments", out var argsEl))
                {
                    var toolName = nameEl.GetString() ?? string.Empty;
                    var args = argsEl.GetRawText();
                    if (_cfg.DebugToolCalls) Console.WriteLine($"[DEBUG] ToolCall: {toolName}({args})");
                    _outer.AgentToolCall?.Invoke(toolName, args);
                }
                else if (type == "text" && block.TryGetProperty("text", out var textEl))
                {
                    var text = textEl.GetString() ?? string.Empty;
                    if (!string.IsNullOrEmpty(text))
                    {
                        var (hasAudio, hasText, audioText, textContent) = ExtractMarkedContent(text);
                        if (hasAudio)
                            _outer.AgentReplyAudio?.Invoke(audioText);
                        if (!startFired)
                        {
                            _outer.AgentReplyDeltaStart?.Invoke();
                            startFired = true;
                        }
                        if (hasText)
                            _outer.AgentReplyFull?.Invoke(textContent);
                        else if (hasAudio)
                            _outer.AgentReplyFull?.Invoke(StripAudioTags(text));
                    }
                }
                else if (type == "audio" && block.TryGetProperty("audio", out var audioEl))
                {
                    var audioText = audioEl.GetString() ?? string.Empty;
                    if (!string.IsNullOrEmpty(audioText))
                    {
                        if (!startFired)
                        {
                            _outer.AgentReplyDeltaStart?.Invoke();
                            startFired = true;
                        }
                        _outer.AgentReplyAudio?.Invoke(audioText);
                    }
                }
                else
                {
                    Console.WriteLine($"[DEBUG] Unknown block type=\"{type}\": {block}");
                }
            }

            if (startFired)
                _outer.AgentReplyDeltaEnd?.Invoke();
        }

        private void HandleAgentStream(JsonElement payload)
        {
            if (!_cfg.RealTimeReplyOutput) return;
            if (!payload.TryGetProperty("data", out var data)) return;

            if (data.TryGetProperty("phase", out var phase))
            {
                var phaseType = phase.GetString() ?? string.Empty;
                if (phaseType == "start") _outer.AgentReplyDeltaStart?.Invoke();
                if (phaseType == "end") _outer.AgentReplyDeltaEnd?.Invoke();
            }

            if (data.TryGetProperty("delta", out var delta))
            {
                var chunk = delta.GetString() ?? string.Empty;
                if (!string.IsNullOrEmpty(chunk))
                    _outer.AgentReplyDelta?.Invoke(chunk);
            }
        }

        private void HandleChatFinal(JsonElement payload)
        {
            if (!payload.TryGetProperty("state", out var state)) return;
            if (state.GetString() != "final") return;

            if (_cfg.RealTimeReplyOutput)
            {
                _outer.AgentReplyDeltaEnd?.Invoke();
                return;
            }

            if (!payload.TryGetProperty("message", out var messageEl)) return;
            if (!messageEl.TryGetProperty("content", out var contentEl)) return;

            var text = ExtractFullText(contentEl);
            if (!string.IsNullOrEmpty(text))
            {
                _outer.AgentReplyDeltaStart?.Invoke();
                _outer.AgentReplyFull?.Invoke(text);
                _outer.AgentReplyDeltaEnd?.Invoke();
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

        private static (bool hasAudio, bool hasText, string audioText, string textContent) ExtractMarkedContent(string fullMessage)
        {
            var audioText = string.Empty;
            var textContent = string.Empty;

            var audioMatch = System.Text.RegularExpressions.Regex.Match(fullMessage, @"\[audio\](.*?)\[/audio\]", System.Text.RegularExpressions.RegexOptions.Singleline);
            if (audioMatch.Success)
            {
                audioText = audioMatch.Groups[1].Value.Trim();
            }
            else
            {
                var openTagIndex = fullMessage.IndexOf("[audio]", StringComparison.OrdinalIgnoreCase);
                if (openTagIndex >= 0)
                    audioText = fullMessage.Substring(openTagIndex + 7).Trim();
            }

            var textMatch = System.Text.RegularExpressions.Regex.Match(fullMessage, @"\[text\](.*?)\[/text\]", System.Text.RegularExpressions.RegexOptions.Singleline);
            if (textMatch.Success)
            {
                textContent = textMatch.Groups[1].Value.Trim();
            }
            else
            {
                var openTagIndex = fullMessage.IndexOf("[text]", StringComparison.OrdinalIgnoreCase);
                if (openTagIndex >= 0)
                    textContent = fullMessage.Substring(openTagIndex + 6).Trim();
            }

            if (string.IsNullOrEmpty(audioText) && string.IsNullOrEmpty(textContent) && !string.IsNullOrEmpty(fullMessage))
                textContent = fullMessage;

            return (!string.IsNullOrEmpty(audioText), !string.IsNullOrEmpty(textContent), audioText, textContent);
        }

        private static string StripAudioTags(string text)
            => System.Text.RegularExpressions.Regex.Replace(text, @"\[audio\](.*?)\[/audio\]", "$1", System.Text.RegularExpressions.RegexOptions.Singleline).Trim();

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
                        await _outer.SendRequestAsync("exec.approval.resolve", new Dictionary<string, object?> { ["id"] = idEl.GetString(), ["approved"] = true }, CancellationToken.None, TimeSpan.FromSeconds(10));
                    }
                    catch (Exception ex)
                    {
                        ConsoleUi.LogError("approval", ex.Message);
                    }
                });
            }
        }
    }
}

// ─── gateway exception ─────────────────────────────────────────────

public sealed class GatewayException : Exception
{
    public JsonElement? Raw { get; }
    public string? DetailCode { get; }
    public string? RecommendedStep { get; }

    public GatewayException(string message, JsonElement? raw = null) : base(message)
    {
        Raw = raw;
        if (raw?.ValueKind == JsonValueKind.Object)
        {
            if (raw.Value.TryGetProperty("error", out var err)
                && err.TryGetProperty("details", out var det))
            {
                DetailCode = det.TryGetProperty("code", out var c) ? c.GetString() : null;
                RecommendedStep = det.TryGetProperty("recommendedNextStep", out var r) ? r.GetString() : null;
            }
        }
    }
}
