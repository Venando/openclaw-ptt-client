namespace OpenClawPTT;

/// <summary>Runs the keepalive tick loop, sending periodic "tick" requests via an <see cref="ISender"/>.</summary>
public sealed class KeepaliveRunner
{
    private readonly ISender _sender;
    private readonly AppConfig _cfg;
    private CancellationTokenSource? _tickCts;

    public KeepaliveRunner(ISender sender, AppConfig cfg)
    {
        _sender = sender;
        _cfg = cfg;
    }

    /// <summary>Starts the keepalive tick loop.</summary>
    public void Start(int intervalMs, CancellationToken ct)
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
                    await _sender.SendRequestAsync("tick", null, tickCt, TimeSpan.FromSeconds(5));
                }
                catch (OperationCanceledException) { break; }
                catch { /* swallow tick failures */ }
            }
        }, tickCt);
    }

    /// <summary>Stops the keepalive tick loop.</summary>
    public void Stop()
    {
        _tickCts?.Cancel();
        _tickCts?.Dispose();
        _tickCts = null;
    }
}
