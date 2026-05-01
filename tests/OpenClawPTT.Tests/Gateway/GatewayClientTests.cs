using System.Text.Json;
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

    [Fact]
    public async Task SendTextAsync_NotConnected_ThrowsInvalidOperationException()
    {
        var mockLifecycle = new Mock<IGatewayConnectionLifecycle>();
        mockLifecycle.Setup(x => x.IsConnected).Returns(false);

        var cfg = new AppConfig { CustomDataDir = Path.GetTempPath(), GatewayUrl = "wss://test", AuthToken = "test" };
        var dev = new DeviceIdentity(cfg.DataDir);
        dev.EnsureKeypair();
        var events = new GatewayEventSource();

        var client = new GatewayClient(cfg, dev, events, () => mockLifecycle.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await client.SendTextAsync("hello", CancellationToken.None));

        client.Dispose();
    }

    [Fact]
    public async Task SendTextAsync_WhenConnected_CallsFramingSendRequestAsync()
    {
        var mockFraming = new Mock<IMessageFraming>();
        mockFraming.Setup(x => x.SendRequestAsync(
            "chat.send",
            It.IsAny<object?>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<TimeSpan?>()))
            .ReturnsAsync(JsonDocument.Parse(@"{""ok"":true}").RootElement);

        var mockLifecycle = new Mock<IGatewayConnectionLifecycle>();
        mockLifecycle.Setup(x => x.IsConnected).Returns(true);
        mockLifecycle.Setup(x => x.GetFraming()).Returns(mockFraming.Object);

        var cfg = new AppConfig { CustomDataDir = Path.GetTempPath(), GatewayUrl = "wss://test", AuthToken = "test" };
        var dev = new DeviceIdentity(cfg.DataDir);
        dev.EnsureKeypair();
        var events = new GatewayEventSource();

        var client = new GatewayClient(cfg, dev, events, () => mockLifecycle.Object);
        await client.SendTextAsync("hello", CancellationToken.None);

        mockFraming.Verify(x => x.SendRequestAsync(
            "chat.send",
            It.IsAny<object?>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<TimeSpan?>()), Times.Once);

        client.Dispose();
    }

    [Fact]
    public async Task SendAudioAsync_NotConnected_ThrowsInvalidOperationException()
    {
        var mockLifecycle = new Mock<IGatewayConnectionLifecycle>();
        mockLifecycle.Setup(x => x.IsConnected).Returns(false);

        var cfg = new AppConfig { CustomDataDir = Path.GetTempPath(), GatewayUrl = "wss://test", AuthToken = "test" };
        var dev = new DeviceIdentity(cfg.DataDir);
        dev.EnsureKeypair();
        var events = new GatewayEventSource();

        var client = new GatewayClient(cfg, dev, events, () => mockLifecycle.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await client.SendAudioAsync(new byte[] { 0 }, CancellationToken.None));

        client.Dispose();
    }

    [Fact]
    public async Task SendEventAsync_NotConnected_ThrowsInvalidOperationException()
    {
        var mockLifecycle = new Mock<IGatewayConnectionLifecycle>();
        mockLifecycle.Setup(x => x.IsConnected).Returns(false);

        var cfg = new AppConfig { CustomDataDir = Path.GetTempPath(), GatewayUrl = "wss://test", AuthToken = "test" };
        var dev = new DeviceIdentity(cfg.DataDir);
        dev.EnsureKeypair();
        var events = new GatewayEventSource();

        var client = new GatewayClient(cfg, dev, events, () => mockLifecycle.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await client.SendEventAsync("test.event", null, CancellationToken.None));

        client.Dispose();
    }

    [Fact]
    public void GetEventSource_ReturnsInjectedEventSource()
    {
        var mockLifecycle = new Mock<IGatewayConnectionLifecycle>();
        var cfg = new AppConfig { CustomDataDir = Path.GetTempPath(), GatewayUrl = "wss://test", AuthToken = "test" };
        var dev = new DeviceIdentity(cfg.DataDir);
        dev.EnsureKeypair();
        var events = new GatewayEventSource();

        var client = new GatewayClient(cfg, dev, events, () => mockLifecycle.Object);
        var result = client.GetEventSource();

        Assert.Same(events, result);
        client.Dispose();
    }

    [Fact]
    public void SessionKey_ReturnsActiveSessionKey()
    {
        var mockLifecycle = new Mock<IGatewayConnectionLifecycle>();
        var cfg = new AppConfig { CustomDataDir = Path.GetTempPath(), GatewayUrl = "wss://test", AuthToken = "test" };
        var dev = new DeviceIdentity(cfg.DataDir);
        dev.EnsureKeypair();
        var events = new GatewayEventSource();

        AgentRegistry.SetAgents(new[] { new AgentInfo { AgentId = "main", Name = "Main", SessionKey = "agent:main:main", IsDefault = true } });

        var client = new GatewayClient(cfg, dev, events, () => mockLifecycle.Object);
        Assert.Equal("agent:main:main", client.SessionKey);
        client.Dispose();
    }

    public void Dispose() { }
}
