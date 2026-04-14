using System.Text.Json;

namespace OpenClawPTT;

/// <summary>
/// Runs the keepalive tick loop on a background task, driven by a configurable interval.
/// Disposed via <see cref="IDisposable"/> so callers can cleanly stop and release.
/// </summary>
public sealed class KeepaliveRunner : IDisposable
{
    private readonly Func<string, object?, CancellationToken, TimeSpan?, Task<JsonElement>> _sendRequestAsync;
    private readonly int _intervalMs;
    private CancellationTokenSource? _cts;

    public KeepaliveRunner(
        Func<string, object?, CancellationToken, TimeSpan?, Task<JsonElement>> sendRequestAsync,
        int intervalMs)
    {
        _sendRequestAsync = sendRequestAsync;
        _intervalMs = intervalMs;
    }

    public void Start(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task.Run(() => RunAsync(_cts.Token), _cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(_intervalMs, ct);
            try
            {
                await _sendRequestAsync("tick", null, ct, TimeSpan.FromSeconds(5));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // swallow tick failures
            }
        }
    }

    public void Dispose() => Stop();
}
