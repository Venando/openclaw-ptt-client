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
public sealed class GatewayConnectionLifecycle : IGatewayConnector, IGatewayConnectionLifecycle
{
    private readonly AppConfig _cfg;
    private readonly DeviceIdentity _deviceIdentity;
    private readonly Func<IClientWebSocket> _socketFactory;

    private IClientWebSocket _ws = null!;
    private CancellationTokenSource? _tickCts;
    private Task? _recvTask;

    private readonly CancellationTokenSource _disposeCts = new CancellationTokenSource();

    // ─── Dependencies ───────────────────────────────────────────────
    private readonly IGatewayEventSource _events;
    private GatewayMessager? _gatewayMessager;
    private GatewayReconnector _gatewayReconnector;

    public GatewayConnectionLifecycle(AppConfig cfg, DeviceIdentity dev, IGatewayEventSource events,
        Func<IClientWebSocket>? socketFactory = null)
    {
        _cfg = cfg;
        _deviceIdentity = dev;
        _events = events;
        _socketFactory = socketFactory ?? (() => new ClientWebSocketAdapter());
        _gatewayReconnector = new GatewayReconnector(cfg, this, _disposeCts.Token);
    }

    // ─── ISender ──────────────────────────────────────────────────────

    public async Task<JsonElement> SendRequestAsync(
        string method,
        object? parameters,
        CancellationToken ct,
        TimeSpan? timeout = null)
    {
        if (_gatewayMessager == null)
            return default;

        return await _gatewayMessager.GetFraming().SendRequestAsync(method, parameters, ct, timeout);
    }

    // ─── Properties ──────────────────────────────────────────────────

    public bool IsConnected => _ws?.State == WebSocketState.Open;

    public MessageFraming? GetFraming() => _gatewayMessager?.GetFraming();

    // ─── connect ─────────────────────────────────────────────────────

    public async Task DisconnectAsync(CancellationToken ct)
    {
        await DisconnectInternalAsync(ct);
    }

    public async Task ConnectAsync(CancellationToken ct)
    {
        // Prevent concurrent connect attempts (two connects would leak sockets)
        await _gatewayReconnector.ReconnectLock.WaitAsync(ct);
        try
        {
            // Clean up any existing connection before reconnecting
            await DisposeConnection(ct);

            // TODO: Accept IClientWebSocket via constructor for testability (PR #37)
            _ws = _socketFactory();

            var linkCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
            var linkedCt = linkCts.Token;
            try
            {
                _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);

                _gatewayMessager = new GatewayMessager(_ws, _events, _cfg, ct => _ = HandleDisconnectionAsync(ct));

                var uri = new Uri(_cfg.GatewayUrl);
                ConsoleUi.Log("gateway", $"Connecting to {uri} ...");
                await _ws.ConnectAsync(uri, linkedCt);
                ConsoleUi.Log("gateway", "WebSocket open.");

                // start receive pump
                _recvTask = Task.Run(() => _gatewayMessager.ReceiveLoop(linkedCt), linkedCt);

                // ── 1. wait for connect.challenge ──
                ConsoleUi.Log("gateway", "Waiting for connect.challenge ...");
                var challenge = await _gatewayMessager.GetFraming().WaitForEventAsync("connect.challenge", TimeSpan.FromSeconds(10), linkedCt);
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

                var sigPayload = _deviceIdentity.BuildV3Payload(platform, deviceFamily, clientId, mode, "operator", scopes, signedAt, authToken, nonce);

                if (_cfg.LogConnect)
                {
                    var redactedPayload = string.IsNullOrEmpty(authToken)
                    ? sigPayload
                    : sigPayload.Replace(authToken, "***REDACTED***");
                    ConsoleUi.Log("gateway", $"Signature payload: {redactedPayload}");
                }

                var signature = _deviceIdentity.Sign(sigPayload);

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
                        ["id"] = _deviceIdentity.DeviceId,
                        ["publicKey"] = _deviceIdentity.PublicKeyBase64,
                        ["signature"] = signature,
                        ["signedAt"] = signedAt,
                        ["nonce"] = nonce
                    }
                };

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
        finally { _gatewayReconnector.ReconnectLock.Release(); }
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

        _gatewayMessager?.ClearFraming();
    }

    private async Task HandleDisconnectionAsync(CancellationToken ct)
    {
        try
        {
            await DisposeConnection(ct);
            _ = _gatewayReconnector.ScheduleReconnectAsync(ct);
        }
        catch (Exception ex)
        {
            ConsoleUi.LogError("gateway", $"Error during disconnection handling: {ex.Message}");
        }
    }

    // ─── dispose connection ───────────────────────────────────────────

    private async Task DisposeConnection(CancellationToken ct)
    {
        if (_tickCts != null)
        {
            await _tickCts.CancelAsync();
            _tickCts.Dispose();
            _tickCts = null;
        }

        _gatewayMessager?.Dispose();
        _gatewayMessager = null;

        if (_ws == null) return;

        try
        {
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

        _tickCts?.Cancel();
        _tickCts?.Dispose();
        _recvTask?.Wait(TimeSpan.FromSeconds(3));

        if (_ws != null && _ws.State == WebSocketState.Open)
        {
            try { _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).Wait(3000); }
            catch { /* best effort */ }
        }
        _ws?.Dispose();

        // Clean up any in-flight send/request TCS to prevent tasks hanging forever
        _gatewayMessager?.Dispose();
        _gatewayMessager = null;

        _gatewayReconnector?.Dispose();
        try { _disposeCts.Dispose(); } catch (ObjectDisposedException) { /* already disposed */ }
    }
}
