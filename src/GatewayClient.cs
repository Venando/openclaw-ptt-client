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

    /// <summary>Fires for every inbound event (event name, payload).</summary>
    public event Action<string, JsonElement>? EventReceived;

    /// <summary>Fires when the agent sends a chat reply (body text).</summary>
    public event Action<string>? AgentReplyFull;

    /// <summary>Fires when the agent sends a chat reply (body text).</summary>
    public event Action<string>? AgentReplyDelta;
    public event Action? AgentReplyDeltaStart;
    public event Action? AgentReplyDeltaEnd;

    public GatewayClient(AppConfig cfg, DeviceIdentity dev)
    {
        _cfg = cfg;
        _dev = dev;
    }

    // ─── connect ────────────────────────────────────────────────────

    public async Task ConnectAsync(CancellationToken ct)
    {
        // Clean up any existing connection before reconnecting
        if (_ws != null)
        {
            // Stop tick task
            _tickCts?.Cancel();
            _tickCts?.Dispose();
            _tickCts = null;
            
            // Close socket if open
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
        Log("gateway", $"Connecting to {uri} ...");
        await _ws.ConnectAsync(uri, ct);
        Log("gateway", "WebSocket open.");

        // start receive pump
        _recvTask = Task.Run(() => ReceiveLoop(ct), ct);

        // ── 1. wait for connect.challenge ──
        Log("gateway", "Waiting for connect.challenge ...");
        var challenge = await WaitForEventAsync("connect.challenge", TimeSpan.FromSeconds(10), ct);
        var nonce = challenge.GetProperty("nonce").GetString()!;
        Log("gateway", $"Challenge nonce: {nonce[..12]}...");

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
        
        var redactedPayload = string.IsNullOrEmpty(authToken)
            ? sigPayload
            : sigPayload.Replace(authToken, "***REDACTED***");

        Log("gateway", $"Signature payload: {redactedPayload}");

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
            ["caps"] = Array.Empty<string>(),
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
        
        LogMessage(connectParams);

        Log("gateway", "Sending connect ...");
        JsonElement hello = await SendRequestAsync("connect", connectParams, ct);

        // ── 3. validate hello-ok ──
        var helloType = hello.TryGetProperty("type", out var htEl) ? htEl.GetString() : null;
        if (helloType != "hello-ok")
            throw new Exception($"Handshake rejected: {hello}");

        LogOk("gateway", "Authenticated — hello-ok received.");

        var options = new JsonSerializerOptions { WriteIndented = true };
        if (_cfg.LogHello)
        {
            string prettyHello = JsonSerializer.Serialize(hello, options);
            string extraPretty = System.Text.RegularExpressions.Regex.Replace(prettyHello, "(?m)^(  )+", m => new string(' ', m.Length * 2));
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
            Log("gateway", "Device token persisted.");
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
                Log("gateway", $"Session initialized: {_cfg.SessionKey}");
            }
        }

        // ── 4. start keepalive ticks ──
        var tickMs = 15_000;
        if (hello.TryGetProperty("policy", out var pol)
            && pol.TryGetProperty("tickIntervalMs", out var tEl))
            tickMs = tEl.GetInt32();

        StartKeepalive(tickMs);
        Log("gateway", $"Keepalive every {tickMs}ms.");
    }//

    private void LogMessage(Dictionary<string, object?> parameters)
    {
        var loggableParams = new Dictionary<string, object?>(parameters);

        // Use TryGetValue to avoid "key not present" exceptions
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
        Log("gateway", $"Sending request:\n{JsonSerializer.Serialize(loggableParams, options)}");
    }

    private static string Redact(string value)
    {
        if (value.Length <= 8) return "***";
        return $"{value[..4]}...{value[^4..]}";
    }

    // ─── connection resilience ──────────────────────────────────────

    private async Task DisconnectInternalAsync(CancellationToken ct)
    {
        // Cancel tick task
        _tickCts?.Cancel();
        _tickCts?.Dispose();
        _tickCts = null;
        
        // Close WebSocket if open
        if (_ws.State == WebSocketState.Open)
        {
            try
            {
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "disconnect", ct);
            }
            catch { /* ignore */ }
        }
        
        // Clear pending requests and event waiters
        ClearPendingRequests();
        
        // Ensure receive loop is stopped (it will exit due to socket closure or cancellation)
        // No need to cancel _recvTask as it uses the same ct.
    }

    private void ClearPendingRequests()
    {
        foreach (var kvp in _pending)
        {
            kvp.Value.TrySetCanceled();
        }
        _pending.Clear();
        
        foreach (var kvp in _eventWaiters)
        {
            kvp.Value.TrySetCanceled();
        }
        _eventWaiters.Clear();
    }

    private async Task HandleDisconnectionAsync(CancellationToken ct)
    {
        try
        {
            // Clean up existing connection
            await DisconnectInternalAsync(ct);
            // Schedule reconnect (fire-and-forget)
            _ = ScheduleReconnectAsync(ct);
        }
        catch (Exception ex)
        {
            LogError("gateway", $"Error during disconnection handling: {ex.Message}");
        }
    }

    private async Task ScheduleReconnectAsync(CancellationToken ct)
    {
        // Ensure only one reconnection attempt at a time
        await _reconnectLock.WaitAsync(ct);
        try
        {
            if (_isReconnecting) return;
            _isReconnecting = true;
            Log("gateway", "Starting reconnection loop...");
            _reconnectTask = ReconnectLoopAsync(ct);
        }
        finally
        {
            _reconnectLock.Release();
        }
    }

    private async Task ReconnectLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            // Wait for configured delay before attempting reconnect
            var delayMs = (int)(_cfg.ReconnectDelaySeconds * 1000);
            Log("gateway", $"Waiting {_cfg.ReconnectDelaySeconds}s before reconnection attempt...");
            await Task.Delay(delayMs, ct);
            
            Log("gateway", "Attempting to reconnect...");
            try
            {
                await ConnectAsync(ct);
                LogOk("gateway", "Reconnected successfully.");
                break; // success
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Shutdown requested, exit loop
                break;
            }
            catch (Exception ex)
            {
                LogError("gateway", $"Reconnection failed: {ex.Message}");
                // Continue loop to retry after next delay
            }
        }
        
        // Clear reconnection flag only if we are no longer trying
        await _reconnectLock.WaitAsync(ct);
        try
        {
            _isReconnecting = false;
            // Keep _reconnectTask as completed task (still referenced)
        }
        finally
        {
            _reconnectLock.Release();
        }
    }

    // ─── send audio ─────────────────────────────────────────────────

    /// <summary>
    /// Send recorded WAV bytes as an audio attachment via chat.send.
    /// Returns the request ack payload.
    /// </summary>
    public async Task<JsonElement> SendAudioAsync(byte[] wavBytes, CancellationToken ct)
    {
        var tempPath = Path.Combine(Path.GetTempPath(),
            $"voice_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.wav");

        await File.WriteAllBytesAsync(tempPath, wavBytes, ct);

        var base64 = Convert.ToBase64String(wavBytes);
        var sessionKey = !string.IsNullOrEmpty(_cfg.SessionKey) ? _cfg.SessionKey : "main";

        var chatParams = new Dictionary<string, object?>
        {
            ["sessionKey"] = sessionKey,
            ["idempotencyKey"] = Guid.NewGuid().ToString(),
            ["message"] = $"file://{tempPath}",
             /*    NOT WORKING, Maybe because file format is not correct or not supported
                ["attachments"] = new[]
                    {
                        new Dictionary<string, object>
                        {
                            ["name"] = $"voice_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.wav",
                            ["type"] = "audio",
                            ["mimeType"] = "audio/wav",
                            ["content"] = base64,
                            ["filename"] = $"voice_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.wav",
                            ["data"] = $"data:audio/wav;base64,{base64}"
                        }
                    }
            */
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
            ["idempotencyKey"] = Guid.NewGuid().ToString(), // Prevents duplicate processing
            ["message"] = body
        };

        return await SendRequestAsync("chat.send", chatParams, ct);
    }
    // ─── low-level framing ──────────────────────────────────────────

    private string NextId() =>
        $"ptt-{Interlocked.Increment(ref _idCounter):D6}";

    private async Task<JsonElement> SendRequestAsync(
        string method,
        object? parameters,
        CancellationToken ct,
        TimeSpan? timeout = null)
    {
        var id = NextId();
        var frame = new Dictionary<string, object?>
        {
            ["type"] = "req",
            ["id"] = id,
            ["method"] = method,
            ["params"] = parameters
        };

        var tcs = new TaskCompletionSource<JsonElement>(
            TaskCreationOptions.RunContinuationsAsynchronously);

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

    private async Task<JsonElement> WaitForEventAsync(
        string eventName,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<JsonElement>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        _eventWaiters[eventName] = tcs;

        using var timeCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeCts.Token);

        await using (linked.Token.Register(() => tcs.TrySetCanceled()))
        {
            try { return await tcs.Task; }
            finally { _eventWaiters.TryRemove(eventName, out _); }
        }
    }

    // ─── receive pump ───────────────────────────────────────────────

    private async Task ReceiveLoop(CancellationToken ct)
    {
        var buf = new byte[64 * 1024];

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
                        Log("gateway", "Server closed connection.");
                        // Schedule reconnection
                        _ = HandleDisconnectionAsync(ct);
                        return;
                    }
                    ms.Write(buf, 0, result.Count);
                } while (!result.EndOfMessage);

                var json = Encoding.UTF8.GetString(ms.ToArray());
                ProcessFrame(json);
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException ex)
        {
            LogError("gateway", $"WebSocket error: {ex.Message}");
            // Schedule reconnection
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
        if (!_pending.TryRemove(id, out var tcs))
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

        // resolve one-shot waiter
        if (_eventWaiters.TryRemove(name, out var tcs))
            tcs.TrySetResult(payload);

        var isFullReplay = name is "chat";
        var isPartialReplay = name is "agent";

        // if (isFullReplay || isPartialReplay)
        // {
        //     Console.WriteLine($"[{name}]: " + payload);
        // }

        if (_cfg.RealTimeReplyOutput)
        {
            if (isPartialReplay)
            {
                if (payload.TryGetProperty("data", out var dataElement))
                {
                    if (dataElement.TryGetProperty("phase", out var phaseElement))
                    {
                        var phaseType = phaseElement.GetString() ?? string.Empty;
                        if (phaseType == "start")
                            AgentReplyDeltaStart?.Invoke();
                    }

                    if (dataElement.TryGetProperty("delta", out var deltaElement))
                    {
                        var newChunk = deltaElement.GetString() ?? string.Empty;
                        AgentReplyDelta?.Invoke(newChunk);
                    }
                }
                return;
            }

            if (isFullReplay)
            {
                // Check if this is the final message
                if (payload.TryGetProperty("state", out var stateElement) &&
                    stateElement.GetString() == "final")
                {
                    AgentReplyDeltaEnd?.Invoke();
                }
            }
        }
        else
        {
            if (isFullReplay)
            {
                // Only call AgentReplyFull for final state messages
                if (payload.TryGetProperty("state", out var stateElement) &&
                    stateElement.GetString() == "final" &&
                    payload.TryGetProperty("message", out var messageElement) &&
                    messageElement.TryGetProperty("content", out var contentElement))
                {
                    // Extract the full text from content array
                    string fullMessage = ExtractFullText(contentElement);
                    AgentReplyFull?.Invoke(fullMessage);
                }
                return;
            }
        }

        EventReceived?.Invoke(name, payload);

        // handle exec approvals inline
        if (name == "exec.approval.requested")
            HandleApprovalRequest(payload);
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
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine();
        Console.WriteLine("  ⚠  Exec approval requested");

        if (payload.TryGetProperty("description", out var d))
            Console.WriteLine($"     {d.GetString()}");

        if (payload.TryGetProperty("command", out var cmd))
            Console.WriteLine($"     $ {cmd.GetString()}");

        Console.ResetColor();
        Console.WriteLine("     (auto-approving from PTT client)");

        // auto-approve — fire and forget
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
                    LogError("approval", ex.Message);
                }
            });
        }
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

    // ─── helpers ────────────────────────────────────────────────────

    private static void Log(string tag, string msg)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write($"  [{tag}] ");
        Console.ResetColor();
        Console.WriteLine(msg);
    }

    private static void LogOk(string tag, string msg)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write($"  [{tag}] ");
        Console.ResetColor();
        Console.WriteLine(msg);
    }

    private static void LogError(string tag, string msg)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Write($"  [{tag}] ");
        Console.ResetColor();
        Console.WriteLine(msg);
    }

    public void Dispose()
    {
        _disposeCts.Cancel();
        _reconnectTask?.Wait(TimeSpan.FromSeconds(5));
        
        _tickCts?.Cancel();
        _tickCts?.Dispose();

        if (_ws.State == WebSocketState.Open)
        {
            try { _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).Wait(3000); }
            catch { /* best effort */ }
        }
        _ws.Dispose();
        
        _reconnectLock.Dispose();
        _disposeCts.Dispose();
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