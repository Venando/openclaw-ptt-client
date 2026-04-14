using System.Net.WebSockets;
using Moq;
using OpenClawPTT;
using Xunit;

namespace OpenClawPTT.Tests.Gateway;

/// <summary>
/// Note: MessageFraming is sealed and cannot be mocked with Moq.
/// These tests use a real MessageFraming instance as the factory result to validate
/// that factory injection works correctly (GetFraming returns the provided instance).
/// </summary>
public class GatewayMessagerTests : IDisposable
{
    private readonly AppConfig _cfg;
    private readonly Mock<IClientWebSocket> _mockWs;
    private readonly Mock<IGatewayEventSource> _mockEvents;
    private readonly MessageFraming _realFraming;
    private readonly GatewayMessager _messager;

    public GatewayMessagerTests()
    {
        _cfg = new AppConfig
        {
            CustomDataDir = Path.GetTempPath(),
            GatewayUrl = "wss://127.0.0.1:9999/test",
            AuthToken = "test-token",
            RealTimeReplyOutput = false
        };
        _mockWs = new Mock<IClientWebSocket>();
        _mockWs.Setup(x => x.State).Returns(WebSocketState.Open);
        _mockEvents = new Mock<IGatewayEventSource>();
        _realFraming = new MessageFraming(_mockWs.Object, _cfg);

        _messager = new GatewayMessager(_mockWs.Object, _mockEvents.Object, _cfg, null, () => _realFraming);
    }

    [Fact]
    public void GetFraming_ReturnsProvidedFraming()
    {
        var framing = _messager.GetFraming();
        Assert.Same(_realFraming, framing);
    }

    [Fact]
    public void ClearFraming_DoesNotThrow()
    {
        var exception = Record.Exception(() => _messager.ClearFraming());
        Assert.Null(exception);
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        _messager.Dispose();
        var exception = Record.Exception(() => _messager.Dispose());
        Assert.Null(exception);
    }

    [Fact]
    public void Construct_WithNullFramingFactory_CreatesRealFraming()
    {
        // Use null factory — should fall back to real MessageFraming (backwards compat)
        var messager = new GatewayMessager(_mockWs.Object, _mockEvents.Object, _cfg);
        Assert.NotNull(messager.GetFraming());
        Assert.IsType<MessageFraming>(messager.GetFraming());
        messager.Dispose();
    }

    public void Dispose()
    {
        _messager.Dispose();
    }
}
