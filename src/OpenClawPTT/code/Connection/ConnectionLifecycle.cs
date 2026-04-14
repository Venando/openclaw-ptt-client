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
public sealed class ConnectionLifecycle
{
    private readonly AppConfig _cfg;
    private readonly DeviceIdentity _dev;

    private IClientWebSocket _ws = null!;
    private CancellationTokenSource? _tickCts;
    private ReceivePump _receivePump = null!;

    private readonly SemaphoreSlim _reconnectLock = new SemaphoreSlim(1, 1);
    private bool _isReconnecting = false;
    private Task? _reconnectTask = null;
    private readonly CancellationTokenSource _disposeCts = new CancellationTokenSource();

    // ─── Dependencies ───────────────────────────────────────────────
    private readonly IGatewayEventSource _events;
    private MessageFraming? _framing;
    private SessionMessageHandler _handler = null!;

    public ConnectionLifecycle(AppConfig cfg, DeviceIdentity dev, IGatewayEventSource events)
    {
        _cfg = cfg;
        _dev = dev;
        _events = events;
        _handler = new SessionMessageHandler(_cfg, (method, parameters, ct, timeout) => SendRequestAsync(method, parameters, ct, timeout));
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

    public MessageFraming GetFraming() => _framing!;

    // ─── connect ─────────────────────────────────────────────────────

    public async Task DisconnectAsync(CancellationToken ct)
    {
        await DisconnectInternalAsync(ct);
    }

    // ─── test support ──────────────────────────────────────────────

    internal void TestHandleEvent(string eventJson)
    {
        using var doc = JsonDocument.Parse(eventJson);
        var root = doc.RootElement;
        var name = root.GetProperty("event").GetString()!;
        var payload = root.TryGetProperty("payload", out var p) ? p.Clone() : default;
        HandleEvent(name, payload);
    }

    internal void TestHandleSessionMessage(string payloadJson)
    {
        using var doc = JsonDocument.Parse(payloadJson);
        _handler.HandleSessionMessage(doc.RootElement);
    }

    internal static string TestStripAudioTags(string text) => SessionMessageHandler.StripAudioTags(text);

    internal (bool hasAudio, bool hasText, string audioText, string textContent) TestExtractMarkedContent(string fullMessage)
        => SessionMessageHandler.ExtractMarkedContent(fullMessage);

    public async Task ConnectAsync(CancellationToken ct)
    {
        // Prevent concurrent connect attempts (two connects would leak sockets)
        await _reconnectLock.WaitAsync(ct);
        try
        {
            // Clean up any existing connection before reconnecting
            await DisposeConnection(ct);

        // TODO: Accept IClientWebSocket via constructor for testability (PR #37)
        _ws = new ClientWebSocketAdapter();

        var linkCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
        var linkedCt = linkCts.Token;
        try
        {
            _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
        _framing = new MessageFraming(_ws, _cfg);

        // Safe to clear now that _framing is assigned
        _framing.ClearPendingRequests();
        _framing.ClearEventWaiters();

        var uri = new Uri(_cfg.GatewayUrl);
        ConsoleUi.Log("gateway", $"Connecting to {uri} ...");
        await _ws.ConnectAsync(uri, linkedCt);
        ConsoleUi.Log("gateway", "WebSocket open.");

        // start receive pump
        _receivePump = new ReceivePump(_ws, _framing, _handler);
        _receivePump.OnEvent = HandleEvent;
        _receivePump.Start(linkedCt);

        // ── 1. wait for connect.challenge ──
        ConsoleUi.Log("gateway", "Waiting for connect.challenge ...");
        var challenge = await _framing.WaitForEventAsync("connect.challenge", TimeSpan.FromSeconds(10), linkedCt);
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
        JsonElement hello = await SendRequestAsync("connect", connectParams, linkedCt);

        // ── 3. validate hello-ok ──
        var helloType = hello.TryGetProperty("type", out var htEl) ? htEl.GetString() : null;
        if (helloType != "hello-ok")
            throw new Exception($"Handshake rejected: {hello}");

        // Check for error field even on hello-ok
        if (hello.TryGetProperty("error", out var err))
            throw new Exception($"Server returned hello-ok with error: {err}");

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
                string prettySnapshot = JsonSerializer.Serialize(snapshot, options);
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine("--- SERVER SNAPSHOT PAYLOAD ---");
                Console.WriteLine(prettySnapshot);
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

        StartKeepalive(tickMs, linkedCt);
        ConsoleUi.Log("gateway", $"Keepalive every {tickMs}ms.");

        var subscribeParams = new Dictionary<string, object?>
        {
            ["sessionKey"] = _cfg.SessionKey  // "agent:main:main"
        };

        await SendRequestAsync("sessions.subscribe", subscribeParams, linkedCt);
        }
        finally { linkCts.Dispose(); }
        }
        finally { _reconnectLock.Release(); }
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

    private void HandleEvent(string eventName, JsonElement payload)
    {
        switch (eventName)
        {
            case "session.message":
                _events.RaiseEventReceived(eventName, payload);
                var r = _handler.HandleSessionMessage(payload);
                if (r.HasAudio) _events.RaiseAgentReplyAudio(r.AudioText);
                if (r.HasText) _events.RaiseAgentReplyFull(r.TextContent);
                if (!string.IsNullOrEmpty(r.Thinking)) _events.RaiseAgentThinking(r.Thinking ?? "");
                if (!string.IsNullOrEmpty(r.ToolCallName)) _events.RaiseAgentToolCall(r.ToolCallName, r.ToolCallArgs ?? "");
                return;
            case "agent":
                _events.RaiseEventReceived(eventName, payload);
                _handler.HandleAgentStream(payload); // realtime-only, no events needed from return
                return;
            case "chat":
                _events.RaiseEventReceived(eventName, payload);
                _handler.HandleChatFinal(payload);
                return;
            case "exec.approval.requested":
                _events.RaiseEventReceived(eventName, payload);
                _ = _handler.HandleApprovalRequest(payload);
                return;
            default:
                _events.RaiseEventReceived(eventName, payload);
                return;
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

        _framing?.ClearPendingRequests();
        _framing?.ClearEventWaiters();
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
        if (_disposeCts.IsCancellationRequested) return;

        await _reconnectLock.WaitAsync(ct);
        try
        {
            if (_isReconnecting) return;
            _isReconnecting = true;
        }
        finally
        {
            _reconnectLock.Release();
        }
        ConsoleUi.Log("gateway", "Starting reconnection loop...");
        _reconnectTask = ReconnectLoopAsync(ct);
    }

    private async Task ReconnectLoopAsync(CancellationToken ct)
    {
        var linkCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
        var linkedCt = linkCts.Token;
        try
        {
            while (!linkedCt.IsCancellationRequested)
            {
                var delayMs = (int)(_cfg.ReconnectDelaySeconds * 1000);
                ConsoleUi.Log("gateway", $"Waiting {_cfg.ReconnectDelaySeconds}s before reconnection attempt...");
                await Task.Delay(delayMs, linkedCt);

                ConsoleUi.Log("gateway", "Attempting to reconnect...");
                try
                {
                    await ConnectAsync(linkedCt);
                    ConsoleUi.LogOk("gateway", "Reconnected successfully.");
                    _isReconnecting = false;
                    break;
                }
                catch (OperationCanceledException) when (linkedCt.IsCancellationRequested)
                {
                    _isReconnecting = false;
                    break;
                }
                catch (Exception ex)
                {
                    ConsoleUi.LogError("gateway", $"Reconnection failed: {ex.Message}");
                }
            }
        }
        finally
        {
            linkCts.Dispose();
        }
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
        _receivePump?.Dispose();

        if (_ws != null && _ws.State == WebSocketState.Open)
        {
            try { _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).Wait(3000); }
            catch { /* best effort */ }
        }
        _ws?.Dispose();

        // Clean up any in-flight send/request TCS to prevent tasks hanging forever
        _framing?.ClearPendingRequests();
        _framing?.ClearEventWaiters();

        _reconnectLock.Dispose();
        try { _disposeCts.Dispose(); } catch (ObjectDisposedException) { /* already disposed */ }
    }
}
