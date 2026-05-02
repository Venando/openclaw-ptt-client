using System;
using System.Collections.Generic;
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
    private readonly Func<IGatewayConnectionLifecycle>? _lifecycleFactory;

    private IGatewayConnectionLifecycle? _lifecycle;

    private bool _isDisposed;

    public GatewayClient(AppConfig cfg, DeviceIdentity dev, IGatewayEventSource eventSource,
        Func<IGatewayConnectionLifecycle>? lifecycleFactory = null)
    {
        _cfg = cfg;
        _dev = dev;
        _eventSource = eventSource;
        _lifecycleFactory = lifecycleFactory ?? (() => new GatewayConnectionLifecycle(_cfg, _dev, _eventSource));
        _lifecycle = _lifecycleFactory();
    }

    // ─── IGatewayClient properties ──────────────────────────────────

    public bool IsConnected => _lifecycle?.IsConnected ?? false;

    public string? SessionKey => AgentRegistry.ActiveSessionKey;

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
        var sessionKey = AgentRegistry.ActiveSessionKey ?? "main";
        var chatParams = new Dictionary<string, object?>
        {
            ["sessionKey"] = sessionKey,
            ["idempotencyKey"] = Guid.NewGuid().ToString(),
            ["message"] = body
        };

        return await TrySendAsync("chat.send", chatParams, ct);
    }

    private async Task<JsonElement> TrySendAsync(string method, object? chatParams, CancellationToken ct)
    {
        if (_lifecycle == null)
            return default;

        var framing = _lifecycle.GetFraming();

        if (framing == null)
            return default;

        return await framing.SendRequestAsync(method, chatParams, ct);
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

        try
        {
            await File.WriteAllBytesAsync(tempPath, wavBytes, ct);

            var sessionKey = AgentRegistry.ActiveSessionKey ?? "main";

            var chatParams = new Dictionary<string, object?>
            {
                ["sessionKey"] = sessionKey,
                ["idempotencyKey"] = Guid.NewGuid().ToString(),
                ["message"] = $"file://{tempPath}",
            };

            return await TrySendAsync("chat.send", chatParams, ct);
        }
        finally
        {
            try { File.Delete(tempPath); } catch { /* best effort */ }
        }
    }

    /// <summary>Sends a generic event/request to the gateway and returns the response payload.</summary>
    public async Task<JsonElement> SendEventAsync(string eventName, object? parameters, CancellationToken ct)
    {
        ThrowIfDisposed();
        if (_lifecycle == null || !_lifecycle.IsConnected)
            throw new InvalidOperationException("Not connected. Call ConnectAsync first.");
        return await TrySendAsync(eventName, parameters, ct);
    }

    // ─── session history ────────────────────────────────────────────────

    /// <summary>Fetches recent chat history for a session. Returns null if unavailable.</summary>
    public async Task<List<ChatHistoryEntry>?> FetchSessionHistoryAsync(string sessionKey, int limit = 5)
    {
        ThrowIfDisposed();
        if (_lifecycle == null || !_lifecycle.IsConnected)
            return null;

        var parameters = new Dictionary<string, object?>
        {
            ["sessionKey"] = sessionKey,
            ["maxMessages"] = limit,
        };

        try
        {
            var result = await TrySendAsync("chat.history", parameters, CancellationToken.None);
            if (result.ValueKind == JsonValueKind.Undefined || result.ValueKind == JsonValueKind.Null)
                return null;

            if (!result.TryGetProperty("messages", out var messagesEl) || messagesEl.ValueKind != JsonValueKind.Array)
                return null;

            var entries = new List<ChatHistoryEntry>(limit);
            foreach (JsonElement msg in messagesEl.EnumerateArray())
            {
                var role = msg.TryGetProperty("role", out var r) ? r.GetString() ?? "" : "";
                var content = ExtractMessageContent(msg);
                var createdAt = msg.TryGetProperty("createdAt", out var c)
                    ? DateTime.TryParse(c.GetString(), out var dt) ? dt : (DateTime?)null
                    : null;

                if (string.IsNullOrWhiteSpace(content) || IsNoReply(content))
                    continue;

                entries.Add(new ChatHistoryEntry
                {
                    Role = role,
                    Content = content,
                    CreatedAt = createdAt,
                });
            }

            return entries;
        }
        catch
        {
            return null;
        }
    }

    private static string ExtractMessageContent(JsonElement msg)
    {
        if (!msg.TryGetProperty("content", out var contentEl))
            return "";

        if (contentEl.ValueKind == JsonValueKind.String)
            return contentEl.GetString() ?? "";

        if (contentEl.ValueKind == JsonValueKind.Array)
        {
            var parts = new List<string>();
            foreach (JsonElement block in contentEl.EnumerateArray())
            {
                if (block.TryGetProperty("type", out var typeEl) && typeEl.GetString() == "text"
                    && block.TryGetProperty("text", out var textEl))
                {
                    parts.Add(textEl.GetString() ?? "");
                }
            }
            return string.Join("", parts);
        }

        return "";
    }

    private static bool IsNoReply(string content)
    {
        if (string.IsNullOrEmpty(content)) return true;
        var trimmed = content.Trim();
        return trimmed.Equals("NO_REPLY", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("no_reply", StringComparison.OrdinalIgnoreCase);
    }

    // ─── recreate ───────────────────────────────────────────────────

    /// <summary>
    /// Recreates the client with a new config (disposes old, creates new).
    /// Currently a no-op — actual recreate is handled by GatewayService.
    /// </summary>
    public void RecreateWithConfig(AppConfig newConfig)
    {
        // No-op: GatewayClient is created and owned by GatewayService.
        // GatewayService.RecreateWithConfig disposes and recreates us.
    }

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
        _lifecycle = null;
    }
}
