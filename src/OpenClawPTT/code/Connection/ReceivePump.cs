using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace OpenClawPTT;

public sealed class ReceivePump : IDisposable
{
    private readonly IClientWebSocket _ws;
    private readonly MessageFraming _framing;
    private readonly SessionMessageHandler _handler;
    private CancellationTokenSource? _cts;

    public event Action? DisconnectionRequested;

    public Action<string, JsonElement>? OnEvent { get; set; }

    public ReceivePump(IClientWebSocket ws, MessageFraming framing, SessionMessageHandler handler)
    {
        _ws = ws;
        _framing = framing;
        _handler = handler;
    }

    public void Start(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task.Run(() => ReceiveLoop(_cts.Token), _cts.Token);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
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
                        ConsoleUi.Log("gateway", "Server closed connection.");
                        DisconnectionRequested?.Invoke();
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
        catch (OperationCanceledException)
        {
            // Cancellation is orchestrated from above (via _disposeCts linked token).
            // No reconnection needed — dispose path handles cleanup.
        }
        catch (WebSocketException ex)
        {
            ConsoleUi.LogError("gateway", $"WebSocket error: {ex.Message}");
            DisconnectionRequested?.Invoke();
        }
        catch (Exception ex)
        {
            ConsoleUi.LogError("gateway", $"ReceiveLoop unexpected error: {ex.GetType().Name}: {ex.Message}");
            DisconnectionRequested?.Invoke();
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
                var name = root.GetProperty("event").GetString()!;
                var payload = root.TryGetProperty("payload", out var p) ? p.Clone() : default;
                _framing.ResolveEventWaiter(name, payload);
                OnEvent?.Invoke(name, payload);
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
}
