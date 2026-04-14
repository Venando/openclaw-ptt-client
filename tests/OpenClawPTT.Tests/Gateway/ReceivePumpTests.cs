using System.Net.WebSockets;
using Moq;
using OpenClawPTT;

namespace OpenClawPTT.Tests.Gateway;

public class ReceivePumpTests
{
    [Fact]
    public void Start_ValidSocket_DoesNotThrow()
    {
        var mockWs = new Mock<IClientWebSocket>();
        mockWs.Setup(x => x.State).Returns(WebSocketState.Open);
        
        var cfg = new AppConfig();
        var ws = mockWs.Object;
        var framing = new MessageFraming(ws, cfg);
        var handler = new SessionMessageHandler(cfg);
        var pump = new ReceivePump(ws, framing, handler);
        
        pump.Start(CancellationToken.None);
        pump.Dispose();
    }

    [Fact]
    public void Dispose_AfterStart_DoesNotThrow()
    {
        var mockWs = new Mock<IClientWebSocket>();
        mockWs.Setup(x => x.State).Returns(WebSocketState.Open);
        mockWs.Setup(x => x.ReceiveAsync(It.IsAny<ArraySegment<byte>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Text, true)));
        
        var cfg = new AppConfig();
        var ws = mockWs.Object;
        var framing = new MessageFraming(ws, cfg);
        var handler = new SessionMessageHandler(cfg);
        var pump = new ReceivePump(ws, framing, handler);
        
        pump.Start(CancellationToken.None);
        pump.Dispose(); // should not throw
    }
}
