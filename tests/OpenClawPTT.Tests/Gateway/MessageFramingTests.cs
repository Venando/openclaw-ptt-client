using System.Net.WebSockets;
using Moq;
using OpenClawPTT;
using Xunit;

namespace OpenClawPTT.Tests.Gateway;

public class MessageFramingTests
{
    private static Mock<IClientWebSocket> CreateMockSocket(WebSocketState state = WebSocketState.Open)
    {
        var mock = new Mock<IClientWebSocket>();
        mock.Setup(x => x.State).Returns(state);
        return mock;
    }

    [Fact]
    public void NextId_ReturnsIncrementalIds()
    {
        var mockWs = CreateMockSocket();
        var cfg = new AppConfig();
        var framing = new MessageFraming(mockWs.Object, cfg);

        var id1 = framing.NextId();
        var id2 = framing.NextId();
        var id3 = framing.NextId();

        Assert.NotEqual(id1, id2);
        Assert.NotEqual(id2, id3);
        Assert.NotEqual(id1, id3);
    }

    [Fact]
    public void NextId_IdIncreasesByOne()
    {
        var mockWs = CreateMockSocket();
        var cfg = new AppConfig();
        var framing = new MessageFraming(mockWs.Object, cfg);

        var id1 = framing.NextId();
        var id2 = framing.NextId();

        // IDs are "ptt-NNNNNN" format — parse the numeric suffix
        var n1 = int.Parse(id1.Split('-')[1]);
        var n2 = int.Parse(id2.Split('-')[1]);
        Assert.Equal(1, n2 - n1);
    }

    [Fact]
    public void ClearPendingRequests_DoesNotThrow()
    {
        var mockWs = CreateMockSocket();
        var cfg = new AppConfig();
        var framing = new MessageFraming(mockWs.Object, cfg);

        var exception = Record.Exception(() => framing.ClearPendingRequests());

        Assert.Null(exception);
    }

    [Fact]
    public void ClearEventWaiters_DoesNotThrow()
    {
        var mockWs = CreateMockSocket();
        var cfg = new AppConfig();
        var framing = new MessageFraming(mockWs.Object, cfg);

        var exception = Record.Exception(() => framing.ClearEventWaiters());

        Assert.Null(exception);
    }

    [Fact]
    public void TryRemovePending_MissingId_ReturnsFalse()
    {
        var mockWs = CreateMockSocket();
        var cfg = new AppConfig();
        var framing = new MessageFraming(mockWs.Object, cfg);

        var found = framing.TryRemovePending("nonexistent", out var tcs);

        Assert.False(found);
    }

    [Fact]
    public void MessageFraming_ClosedSocket_StillConstructs()
    {
        var mockWs = CreateMockSocket(WebSocketState.Closed);
        var cfg = new AppConfig();
        var framing = new MessageFraming(mockWs.Object, cfg);

        Assert.NotNull(framing);
    }
}