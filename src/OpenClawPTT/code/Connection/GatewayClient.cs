using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OpenClawPTT;

/// <summary>
/// Thin coordinator that owns child components, wires them together,
/// and delegates all operations to them.
/// </summary>
public sealed class GatewayClient : IGatewayClient
{
    private readonly AppConfig _cfg;
    private readonly DeviceIdentity _dev;
    private readonly IGatewayEventSource _eventSource;

    private ConnectionLifecycle? _lifecycle;
    private MessageFraming? _framing;

    private bool _isDisposed;

    public GatewayClient(AppConfig cfg, DeviceIdentity dev, IGatewayEventSource eventSource)
    {
        _cfg = cfg;
        _dev = dev;
        _eventSource = eventSource;
        _lifecycle = new ConnectionLifecycle(_cfg, _dev, _eventSource);
    }

    // ─── IGatewayClient properties ──────────────────────────────────

    public bool IsConnected => _lifecycle?.IsConnected ?? false;

    public string? SessionKey => _cfg.SessionKey;

    public string? AgentId => null; // Not implemented in this version

    public bool IsDisposed => _isDisposed;

    public IGatewayEventSource? GetEventSource() => _eventSource;

    // ─── connect ────────────────────────────────────────────────────

    public async Task ConnectAsync(CancellationToken ct)
    {
        ThrowIfDisposed();

        // lifecycle was created in constructor for testability.
        // ConnectAsync internally manages keepalive (reads tickIntervalMs from server hello).
        await _lifecycle.ConnectAsync(ct);

        _framing = _lifecycle.GetFraming();
    }

    // ─── disconnect ─────────────────────────────────────────────────

    public async Task DisconnectAsync(CancellationToken ct)
    {
        ThrowIfDisposed();
        await (_lifecycle?.DisconnectAsync(ct) ?? Task.CompletedTask);
    }

    // ─── send ───────────────────────────────────────────────────────

    /// <summary>Send a text message via chat.send.</summary>
    public async Task<JsonElement> SendTextAsync(string body, CancellationToken ct)
    {
        ThrowIfDisposed();
        if (_lifecycle == null || !_lifecycle.IsConnected)
            throw new InvalidOperationException("Not connected. Call ConnectAsync first.");
        var sessionKey = !string.IsNullOrEmpty(_cfg.SessionKey) ? _cfg.SessionKey : "main";
        var chatParams = new Dictionary<string, object?>
        {
            ["sessionKey"] = sessionKey,
            ["idempotencyKey"] = Guid.NewGuid().ToString(),
            ["message"] = body
        };
        return await _framing!.SendRequestAsync("chat.send", chatParams, ct);
    }

    /// <summary>
    /// Send recorded WAV bytes as an audio attachment via chat.send.
    /// Returns the request ack payload.
    /// </summary>
    public async Task<JsonElement> SendAudioAsync(byte[] wavBytes, CancellationToken ct)
    {
        ThrowIfDisposed();
        if (_lifecycle == null || !_lifecycle.IsConnected)
            throw new InvalidOperationException("Not connected. Call ConnectAsync first.");

        var tempPath = Path.Combine(Path.GetTempPath(),
            $"voice_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.wav");

        await File.WriteAllBytesAsync(tempPath, wavBytes, ct);

        var sessionKey = !string.IsNullOrEmpty(_cfg.SessionKey) ? _cfg.SessionKey : "main";

        var chatParams = new Dictionary<string, object?>
        {
            ["sessionKey"] = sessionKey,
            ["idempotencyKey"] = Guid.NewGuid().ToString(),
            ["message"] = $"file://{tempPath}",
        };

        return await _framing!.SendRequestAsync("chat.send", chatParams, ct);
    }

    /// <summary>Sends a generic event/request to the gateway and returns the response payload.</summary>
    public async Task<JsonElement> SendEventAsync(string eventName, object? parameters, CancellationToken ct)
    {
        ThrowIfDisposed();
        if (_lifecycle == null || !_lifecycle.IsConnected)
            throw new InvalidOperationException("Not connected. Call ConnectAsync first.");
        return await _framing!.SendRequestAsync(eventName, parameters, ct);
    }

    // ─── recreate ───────────────────────────────────────────────────

    /// <summary>Recreates the client with a new config (disposes old, creates new).</summary>
    public void RecreateWithConfig(AppConfig newConfig)
    {
        // Note: GatewayClient is created by GatewayService, so this is a no-op here.
        // The actual recreate logic is handled by GatewayService.RecreateWithConfig.
    }

    // ─── test support ────────────────────────────────────────────────────────

    /// <summary>Processes a full session.message event JSON string for testing.</summary>
    internal void TestHandleSessionMessage(string eventJson)
    {
        // Route the event JSON ({"type":"event","event":"session.message","payload":{...}})
        // through ConnectionLifecycle's HandleEvent, which dispatches to HandleSessionMessage.
        _lifecycle?.TestHandleEvent(eventJson);
    }

    /// <summary>For testing only — bypasses HandleEvent and calls HandleSessionMessage directly on the lifecycle.</summary>
    internal void TestHandleSessionMessageDirect(string payloadJson)
    {
        _lifecycle?.TestHandleSessionMessage(payloadJson);
    }

    /// <summary>Strips audio tags — exposes private static for testing.</summary>
    internal static string TestStripAudioTags(string text) => ConnectionLifecycle.TestStripAudioTags(text);

    /// <summary>Extracts marked content — exposes internal method on lifecycle for testing.</summary>
    internal (bool hasAudio, bool hasText, string audioText, string textContent) TestExtractMarkedContent(string fullMessage)
        => _lifecycle?.TestExtractMarkedContent(fullMessage)
           ?? (false, false, "", "");

    // ─── helpers ─────────────────────────────────────────────────────

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(GatewayClient));
    }

    // ─── IDisposable ─────────────────────────────────────────────────

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _lifecycle?.Dispose();
        _framing = null;
        _lifecycle = null;
    }
}