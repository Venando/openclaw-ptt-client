using Moq;
using OpenClawPTT;
using Xunit;

namespace OpenClawPTT.Tests.Gateway;

public class GatewayClientTests : IDisposable
{
    [Fact]
    public void GatewayClient_WithMockLifecycle_ConstructsWithoutThrowing()
    {
        var mockLifecycle = new Mock<IGatewayConnectionLifecycle>();
        var cfg = new AppConfig { CustomDataDir = Path.GetTempPath(), GatewayUrl = "wss://test", AuthToken = "test" };
        var dev = new DeviceIdentity(cfg.DataDir);
        dev.EnsureKeypair();
        var events = new GatewayEventSource();

        var client = new GatewayClient(cfg, dev, events, () => mockLifecycle.Object);

        Assert.NotNull(client);
        Assert.False(client.IsDisposed);
        client.Dispose();
    }

    [Fact]
    public void GatewayClient_WithMockLifecycle_IsConnected_DelegatesToLifecycle()
    {
        var mockLifecycle = new Mock<IGatewayConnectionLifecycle>();
        mockLifecycle.Setup(x => x.IsConnected).Returns(true);

        var cfg = new AppConfig { CustomDataDir = Path.GetTempPath(), GatewayUrl = "wss://test", AuthToken = "test" };
        var dev = new DeviceIdentity(cfg.DataDir);
        dev.EnsureKeypair();
        var events = new GatewayEventSource();

        var client = new GatewayClient(cfg, dev, events, () => mockLifecycle.Object);

        Assert.True(client.IsConnected);
        client.Dispose();
    }

    [Fact]
    public void Dispose_CallsLifecycle_Dispose()
    {
        var mockLifecycle = new Mock<IGatewayConnectionLifecycle>();
        var cfg = new AppConfig { CustomDataDir = Path.GetTempPath(), GatewayUrl = "wss://test", AuthToken = "test" };
        var dev = new DeviceIdentity(cfg.DataDir);
        dev.EnsureKeypair();
        var events = new GatewayEventSource();

        var client = new GatewayClient(cfg, dev, events, () => mockLifecycle.Object);
        client.Dispose();

        mockLifecycle.Verify(x => x.Dispose(), Times.Once);
    }

    [Fact]
    public void ConnectAsync_ThrowsObjectDisposed_WhenDisposed()
    {
        var mockLifecycle = new Mock<IGatewayConnectionLifecycle>();
        var cfg = new AppConfig { CustomDataDir = Path.GetTempPath(), GatewayUrl = "wss://test", AuthToken = "test" };
        var dev = new DeviceIdentity(cfg.DataDir);
        dev.EnsureKeypair();
        var events = new GatewayEventSource();

        var client = new GatewayClient(cfg, dev, events, () => mockLifecycle.Object);
        client.Dispose();

        Assert.Throws<ObjectDisposedException>(() => client.ConnectAsync(CancellationToken.None).GetAwaiter().GetResult());
    }

    public void Dispose() { }
}
