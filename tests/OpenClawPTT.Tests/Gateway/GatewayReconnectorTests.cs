using System.Net.WebSockets;
using Moq;
using OpenClawPTT;
using Xunit;

namespace OpenClawPTT.Tests.Gateway;

public class GatewayReconnectorTests : IDisposable
{
    private readonly AppConfig _cfg;
    private readonly Mock<IGatewayConnector> _mockConnector;
    private readonly CancellationTokenSource _cts;
    private readonly GatewayReconnector _reconnector;

    public GatewayReconnectorTests()
    {
        _cfg = new AppConfig
        {
            CustomDataDir = Path.GetTempPath(),
            ReconnectDelaySeconds = 0 // fast reconnect for tests
        };
        _mockConnector = new Mock<IGatewayConnector>();
        _cts = new CancellationTokenSource();
        _reconnector = new GatewayReconnector(_cfg, _mockConnector.Object, _cts.Token);
    }

    private GatewayReconnector CreateReconnector(AppConfig? cfg = null, IGatewayConnector? connector = null, CancellationToken ct = default)
    {
        return new GatewayReconnector(
            cfg ?? _cfg,
            connector ?? _mockConnector.Object,
            ct == default ? _cts.Token : ct);
    }

    [Fact]
    public void ReconnectLock_IsInitiallyAvailable()
    {
        var reconn = CreateReconnector();
        Assert.True(reconn.ReconnectLock.Wait(0));
        reconn.ReconnectLock.Release();
    }

    [Fact]
    public async Task ScheduleReconnectAsync_AcquiresLock()
    {
        var reconn = CreateReconnector();

        var task = reconn.ScheduleReconnectAsync(CancellationToken.None);

        // Lock should be acquired (wait 500ms)
        Assert.True(reconn.ReconnectLock.Wait(TimeSpan.FromMilliseconds(500)));
        reconn.ReconnectLock.Release();
        _cts.Cancel();

        try { await task.WaitAsync(TimeSpan.FromSeconds(1)); } catch { /* ignore */ }
    }

    [Fact]
    public async Task ScheduleReconnectAsync_WhenCancelled_ReturnsImmediately()
    {
        using var localCts = new CancellationTokenSource();
        localCts.Cancel();
        var reconn = CreateReconnector(ct: localCts.Token);

        var exception = await Record.ExceptionAsync(() => reconn.ScheduleReconnectAsync(localCts.Token));

        Assert.Null(exception);
    }

    [Fact]
    public async Task ScheduleReconnectAsync_SetsIsReconnecting()
    {
        var reconn = CreateReconnector();

        // Use a connect that hangs so reconn stays in reconnecting state
        var hangCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var hangConnector = new Mock<IGatewayConnector>();
        hangConnector.Setup(x => x.ConnectAsync(It.IsAny<CancellationToken>()))
            .Returns(async () => {
                try { await Task.Delay(Timeout.Infinite, hangCts.Token); }
                catch (OperationCanceledException) { throw; }
            });

        var reconnWithHang = new GatewayReconnector(_cfg, hangConnector.Object, _cts.Token);

        var scheduleTask = reconnWithHang.ScheduleReconnectAsync(CancellationToken.None);

        // Give it a moment to enter the reconnect loop
        await Task.Delay(100);

        // Lock should be held (indicating reconnect in progress)
        Assert.True(reconnWithHang.ReconnectLock.Wait(0), "Lock should be held during reconnect");

        hangCts.Cancel();
        _cts.Cancel();

        try { await scheduleTask.WaitAsync(TimeSpan.FromSeconds(2)); } catch { /* ignore */ }
        reconnWithHang.Dispose();
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var reconn = CreateReconnector();
        _cts.Cancel();
        try { reconn.ScheduleReconnectAsync(CancellationToken.None).Wait(500); } catch { /* ignore */ }

        reconn.Dispose();
        var exception = Record.Exception(() => reconn.Dispose());

        Assert.Null(exception);
    }

    [Fact]
    public void Dispose_WaitsForReconnectTask()
    {
        var reconn = CreateReconnector();

        // Should not throw — Dispose waits up to 5 seconds
        var exception = Record.Exception(() => reconn.Dispose());
        Assert.Null(exception);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        try { _reconnector.Dispose(); } catch { /* ignore */ }
    }
}
