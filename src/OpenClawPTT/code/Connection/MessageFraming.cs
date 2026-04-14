using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace OpenClawPTT;

/// <summary>
/// Manages message framing, request/response correlation, and one-shot event waiters.
/// Thread-safe. Used by GatewayClient to handle the WebSocket protocol.
/// </summary>
public sealed class MessageFraming : ISender
{
    private readonly IClientWebSocket _ws;
    private readonly AppConfig _cfg;

    // pending request → response futures
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new();

    // one-shot event waiters (event name → future)
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _eventWaiters = new();

    private int _idCounter;

    public MessageFraming(IClientWebSocket ws, AppConfig cfg)
    {
        _ws = ws;
        _cfg = cfg;
    }

    /// <summary>Returns the ISender interface for use by the client.</summary>
    public ISender GetSender() => this;

    /// <summary>Generates the next unique request ID.</summary>
    public string NextId() =>
        $"ptt-{Interlocked.Increment(ref _idCounter):D6}";

    // ─── ISender ────────────────────────────────────────────────────

    /// <summary>Sends a request and waits for the response.</summary>
    public async Task<JsonElement> SendRequestAsync(
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

    // ─── event waiting ──────────────────────────────────────────────

    /// <summary>Waits for a single inbound event with the given name.</summary>
    public async Task<JsonElement> WaitForEventAsync(
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

    // ─── frame processing ───────────────────────────────────────────

    /// <summary>Processes an inbound JSON frame from the WebSocket.</summary>
    internal void ProcessFrame(string json)
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
    }

    // ─── cleanup ────────────────────────────────────────────────────

    /// <summary>Cancels and clears all pending request waiters.</summary>
    public void ClearPendingRequests()
    {
        foreach (var kvp in _pending)
        {
            kvp.Value.TrySetCanceled();
        }
        _pending.Clear();
    }

    /// <summary>Cancels and clears all event waiters.</summary>
    public void ClearEventWaiters()
    {
        foreach (var kvp in _eventWaiters)
        {
            kvp.Value.TrySetCanceled();
        }
        _eventWaiters.Clear();
    }
}
