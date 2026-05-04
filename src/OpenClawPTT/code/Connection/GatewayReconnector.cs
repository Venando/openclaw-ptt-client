using OpenClawPTT;
using OpenClawPTT.Services;

public class GatewayReconnector : IDisposable
{
    private readonly SemaphoreSlim _reconnectLock = new SemaphoreSlim(1, 1);
    private readonly IGatewayConnector _gatewayConnector;
    private readonly IColorConsole _console;
    private readonly AppConfig _cfg;
    private readonly CancellationToken _cancellationToken;

    private bool _isReconnecting = false;
    private Task? _reconnectTask = null;

    public SemaphoreSlim ReconnectLock => _reconnectLock;

    public GatewayReconnector(AppConfig appConfig, IColorConsole console, IGatewayConnector gatewayConnector, CancellationToken cancellationToken)
    {
        _cfg = appConfig;
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _cancellationToken = cancellationToken;
        _gatewayConnector = gatewayConnector;
    }

    public async Task ScheduleReconnectAsync(CancellationToken ct)
    {
        if (_cancellationToken.IsCancellationRequested) return;

        await _reconnectLock.WaitAsync(ct);
        try
        {
            if (_isReconnecting) return;
            _isReconnecting = true;
        }
        finally
        {
            _reconnectLock.Release();
        }
        _console.Log("gateway", "Starting reconnection loop...");
        _reconnectTask = ReconnectLoopAsync(ct);
    }

    private async Task ReconnectLoopAsync(CancellationToken ct)
    {
        var linkCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _cancellationToken);
        var linkedCt = linkCts.Token;
        try
        {
            while (!linkedCt.IsCancellationRequested)
            {
                var delayMs = (int)(_cfg.ReconnectDelaySeconds * 1000);
                _console.Log("gateway", $"Waiting {_cfg.ReconnectDelaySeconds}s before reconnection attempt...");
                await Task.Delay(delayMs, linkedCt);

                _console.Log("gateway", "Attempting to reconnect...");
                try
                {
                    await _gatewayConnector.ConnectAsync(linkedCt);
                    _console.LogOk("gateway", "Reconnected successfully.");
                    _isReconnecting = false;
                    break;
                }
                catch (OperationCanceledException) when (linkedCt.IsCancellationRequested)
                {
                    _isReconnecting = false;
                    break;
                }
                catch (Exception ex)
                {
                    _console.LogError("gateway", $"Reconnection failed: {ex.Message}");
                }
            }
        }
        finally
        {
            linkCts.Dispose();
        }
    }

    public void Dispose()
    {
        _reconnectTask?.Wait(TimeSpan.FromSeconds(5));
        _reconnectLock.Dispose();
    }

}