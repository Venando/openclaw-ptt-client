using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace OpenClawPTT;

/// <summary>
/// Handles the WebSocket handshake: waits for connect.challenge,
/// builds and sends the signed connect request, validates hello-ok.
/// </summary>
public sealed class AuthHandler
{
    private readonly DeviceIdentity _dev;
    private readonly AppConfig _cfg;

    private IClientWebSocket _ws = null!;
    private CancellationToken _ct;
    private int _idCounter;

    // one-shot event waiters (event name → future)
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _eventWaiters = new();

    // pending request → response futures
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new();

    private Task? _recvTask;

    public AuthHandler(DeviceIdentity dev, AppConfig cfg)
    {
        _dev = dev;
        _cfg = cfg;
    }

    /// <summary>
    /// Full auth handshake: connect → wait challenge → send connect → validate hello.
    /// Returns the authenticated IClientWebSocket and hello JsonElement.
    /// </summary>
    public async Task<(IClientWebSocket ws, JsonElement hello)> AuthenticateAsync(CancellationToken ct)
    {
        _ct = ct;
        await ConnectSocketAsync();

        // start receive pump
        _recvTask = Task.Run(() => ReceiveLoop(ct), ct);

        // 1. wait for connect.challenge
        string nonce = await WaitForChallengeAsync(ct);

        // 2. send connect and validate hello-ok
        JsonElement hello = await SendConnectAsync(nonce, ct);

        return (_ws, hello);
    }

    private async Task ConnectSocketAsync()
    {
        await DisposeConnection();

        _ws = new ClientWebSocketAdapter();
        _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);

        var uri = new Uri(_cfg.GatewayUrl);
        ConsoleUi.Log("gateway", $"Connecting to {uri} ...");
        await _ws.ConnectAsync(uri, _ct);
        ConsoleUi.Log("gateway", "WebSocket open.");
    }

    /// <summary>
    /// Waits for the connect.challenge event and returns the nonce string.
    /// </summary>
    public async Task<string> WaitForChallengeAsync(CancellationToken ct)
    {
        ConsoleUi.Log("gateway", "Waiting for connect.challenge ...");
        var challenge = await WaitForEventAsync("connect.challenge", TimeSpan.FromSeconds(10), ct);
        var nonceEl = challenge.GetProperty("nonce");
        if (nonceEl.ValueKind == JsonValueKind.Null || nonceEl.ValueKind == JsonValueKind.Undefined)
            throw new GatewayException("Server challenge missing nonce", challenge);
        var nonce = nonceEl.GetString() ?? throw new GatewayException("Server challenge nonce is empty", challenge);
        return nonce;
    }

    /// <summary>
    /// Builds the signed auth payload using the given nonce, sends the connect
    /// request, waits for the hello response, and validates hello-ok.
    /// Returns the hello JsonElement.
    /// </summary>
    public async Task<JsonElement> SendConnectAsync(string nonce, CancellationToken ct)
    {
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

        // validate hello-ok
        var helloType = hello.TryGetProperty("type", out var htEl) ? htEl.GetString() : null;
        if (helloType == "hello-ok" && hello.TryGetProperty("error", out var err))
            throw new GatewayException($"Server returned hello-ok with error: {err}", hello);
        if (helloType != "hello-ok")
            throw new Exception($"Handshake rejected: {hello}");

        ConsoleUi.LogOk("gateway", "Authenticated — hello-ok received.");
        return hello;
    }

    // ─── private helpers ────────────────────────────────────────────

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
                        ConsoleUi.Log("gateway", "Server closed connection during auth.");
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
            ConsoleUi.LogError("gateway", $"WebSocket error during auth: {ex.Message}");
        }
        catch (Exception ex)
        {
            ConsoleUi.LogError("gateway", $"AuthHandler ReceiveLoop error: {ex.GetType().Name}: {ex.Message}");
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

        if (_eventWaiters.TryRemove(name, out var tcs))
            tcs.TrySetResult(payload);
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

    private async Task DisposeConnection()
    {
        if (_ws == null) return;

        try
        {
            if (_ws.State == WebSocketState.Open || _ws.State == WebSocketState.CloseReceived)
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                try
                {
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", timeoutCts.Token);
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

    public void Dispose()
    {
        _recvTask?.Wait(TimeSpan.FromSeconds(5));
        if (_ws != null && _ws.State == WebSocketState.Open)
        {
            try { _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).Wait(3000); }
            catch { /* best effort */ }
        }
        _ws?.Dispose();
    }
}
