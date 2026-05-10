using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.Services;

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
    private readonly IColorConsole _console;
    private readonly IAgentStatusTracker? _agentStatusTracker;

    private IGatewayConnectionLifecycle? _lifecycle;

    private bool _isDisposed;

    public GatewayClient(AppConfig cfg, DeviceIdentity dev, IGatewayEventSource eventSource, IColorConsole console,
        Func<IGatewayConnectionLifecycle>? lifecycleFactory = null, IAgentStatusTracker? agentStatusTracker = null)
    {
        _cfg = cfg;
        _dev = dev;
        _eventSource = eventSource;
        _console = console;
        _lifecycleFactory = lifecycleFactory ?? (() => new GatewayConnectionLifecycle(_cfg, _dev, _eventSource, console, agentStatusTracker: agentStatusTracker));
        _lifecycle = _lifecycleFactory();
        _agentStatusTracker = agentStatusTracker;
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
        if (_lifecycle == null)
            throw new InvalidOperationException("Lifecycle not initialized.");
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
        {
            return null;
        }


        var parameters = new Dictionary<string, object?>
        {
            ["sessionKey"] = sessionKey,
        };

        try
        {
            // Try chat.history first (primary RPC for chat history)
            var result = await TrySendAsync("chat.history", parameters, CancellationToken.None);


            if (result.ValueKind == JsonValueKind.Undefined || result.ValueKind == JsonValueKind.Null)
            {
                // Fallback: try sessions.preview
                result = await TrySendAsync("sessions.preview", new Dictionary<string, object?>
                {
                    ["sessionKey"] = sessionKey,
                }, CancellationToken.None);

                if (result.ValueKind == JsonValueKind.Undefined || result.ValueKind == JsonValueKind.Null)
                    return null;
            }


            // Try different response shapes:
            // 1. chat.history returns { messages: [...] }
            // 2. sessions.preview might return { preview: [...] } or just an array
            // Try to find the messages array
            JsonElement messagesEl = default;
            bool found = false;

            if (result.TryGetProperty("messages", out var msgs) && msgs.ValueKind == JsonValueKind.Array)
            {
                messagesEl = msgs;
                found = true;
            }
            else if (result.TryGetProperty("preview", out var prev) && prev.ValueKind == JsonValueKind.Array)
            {
                messagesEl = prev;
                found = true;
            }
            else if (result.ValueKind == JsonValueKind.Array)
            {
                messagesEl = result;
                found = true;
            }

            if (!found)
            {
                return null;
            }


            var entries = new List<ChatHistoryEntry>(limit);
            var totalEntries = messagesEl.GetArrayLength();

            // Iterate backwards — most recent messages are at the end
            for (int i = totalEntries - 1; i >= 0; i--)
            {
                if (entries.Count >= limit)
                {
                    break;
                }

                // Chat history messages have a different schema from gateway events:
                // they carry model, provider, token usage, and cost info but NOT
                // session-level metadata (status, phase, etc.).
                // Extract what we can and let the tracker merge with existing
                // snapshots via MergeSnapshots (preserving richer prior data).
                var snapshot = BuildSnapshotFromHistoryMessage(messagesEl[i], sessionKey);
                if (snapshot != null)
                {
                    _agentStatusTracker?.Update(snapshot);
                }

                if (!UserMessageHelper.TryGetChatHistoryEntry(messagesEl[i], out var entry))
                {
                    continue;
                }

                entries.Add(entry!);
            }

            // Reverse so oldest-to-newest for display (newest last)
            entries.Reverse();

            return entries;
        }
        catch (Exception)
        {
            return null;
        }
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

    /// <summary>
    /// Builds a partial <see cref="AgentStatusSnapshot"/> from a chat history
    /// message (which follows a different schema from gateway events).
    /// Chat messages carry model, provider, stopReason, timestamp, and usage
    /// (tokens + cost) — but NOT session-level metadata like status/phase.
    /// The tracker&#39;s <see cref="AgentStatusTracker.Update"/> will merge this
    /// with any existing snapshot for the session via <c>MergeSnapshots</c>.
    /// </summary>
    private static AgentStatusSnapshot? BuildSnapshotFromHistoryMessage(
        JsonElement msg, string sessionKey)
    {
        if (msg.ValueKind != JsonValueKind.Object)
            return null;

        if (string.IsNullOrEmpty(sessionKey))
            return null;

        // ── Top-level fields ───────────────────────────────────────────
        string? model = null;
        string? modelProvider = null;
        string? stopReason = null;
        long? timestamp = null;

        if (msg.TryGetProperty("model", out var mEl) && mEl.ValueKind == JsonValueKind.String)
            model = mEl.GetString();

        // Chat messages use "provider" but AgentStatusSnapshot uses ModelProvider
        if (msg.TryGetProperty("provider", out var pEl) && pEl.ValueKind == JsonValueKind.String)
            modelProvider = pEl.GetString();

        if (msg.TryGetProperty("stopReason", out var srEl) && srEl.ValueKind == JsonValueKind.String)
            stopReason = srEl.GetString();

        if (msg.TryGetProperty("timestamp", out var tsEl) && tsEl.ValueKind == JsonValueKind.Number)
        {
            if (tsEl.TryGetInt64(out var tVal)) timestamp = tVal;
        }

        // ── Nested usage ────────────────────────────────────────────────
        long? inputTokens = null;
        long? outputTokens = null;
        long? totalTokens = null;
        decimal? costUsd = null;

        if (msg.TryGetProperty("usage", out var usageEl) && usageEl.ValueKind == JsonValueKind.Object)
        {
            if (usageEl.TryGetProperty("input", out var inp) && inp.ValueKind == JsonValueKind.Number)
            {
                if (inp.TryGetInt64(out var v)) inputTokens = v;
            }

            if (usageEl.TryGetProperty("output", out var outp) && outp.ValueKind == JsonValueKind.Number)
            {
                if (outp.TryGetInt64(out var v)) outputTokens = v;
            }

            if (usageEl.TryGetProperty("totalTokens", out var tt) && tt.ValueKind == JsonValueKind.Number)
            {
                if (tt.TryGetInt64(out var v)) totalTokens = v;
            }

            // usage.cost.total
            if (usageEl.TryGetProperty("cost", out var costEl) && costEl.ValueKind == JsonValueKind.Object)
            {
                if (costEl.TryGetProperty("total", out var ct) && ct.ValueKind == JsonValueKind.Number)
                {
                    if (ct.TryGetDecimal(out var d)) costUsd = d;
                }
            }
        }

        return new AgentStatusSnapshot
        {
            SessionKey = sessionKey,
            Model = model,
            ModelProvider = modelProvider,
            StopReason = stopReason,
            UpdatedAt = timestamp,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            TotalTokens = totalTokens,
            EstimatedCostUsd = costUsd,
        };
    }

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
