using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Moq;
using OpenClawPTT;
using Xunit;

namespace OpenClawPTT.Tests.Gateway;

public class MessageFramingAsyncTests
{
    private static Mock<IClientWebSocket> CreateMockSocket(
        WebSocketState state = WebSocketState.Open,
        Func<ArraySegment<byte>, WebSocketMessageType, bool, CancellationToken, Task>? sendHandler = null)
    {
        var mock = new Mock<IClientWebSocket>();
        mock.Setup(x => x.State).Returns(state);
        mock.Setup(x => x.Options).Returns(Mock.Of<ClientWebSocketOptions>());

        if (sendHandler != null)
        {
            mock.Setup(x => x.SendAsync(
                It.IsAny<ArraySegment<byte>>(),
                It.IsAny<WebSocketMessageType>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
                .Returns((ArraySegment<byte> buf, WebSocketMessageType mt, bool eom, CancellationToken ct) =>
                    sendHandler(buf, mt, eom, ct));
        }
        else
        {
            mock.Setup(x => x.SendAsync(
                It.IsAny<ArraySegment<byte>>(),
                It.IsAny<WebSocketMessageType>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }

        return mock;
    }

    private static ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> GetPendingField(MessageFraming framing)
    {
        var field = typeof(MessageFraming).GetField("_pending", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (ConcurrentDictionary<string, TaskCompletionSource<JsonElement>>)field.GetValue(framing)!;
    }

    private static ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> GetEventWaitersField(MessageFraming framing)
    {
        var field = typeof(MessageFraming).GetField("_eventWaiters", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (ConcurrentDictionary<string, TaskCompletionSource<JsonElement>>)field.GetValue(framing)!;
    }

    [Fact]
    public async Task SendRequestAsync_OpenSocket_CallsSendAsync()
    {
        var mockWs = CreateMockSocket();
        var cfg = new AppConfig { CustomDataDir = Path.GetTempPath() };
        var framing = new MessageFraming(mockWs.Object, cfg);

        // Start the request — it will wait for a response that never comes (timeout)
        // but we can verify SendAsync was called
        var sendCallCount = 0;
        mockWs
            .Setup(x => x.SendAsync(
                It.IsAny<ArraySegment<byte>>(),
                WebSocketMessageType.Text,
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => sendCallCount++)
            .Returns(Task.CompletedTask);

        try {
            await framing.SendRequestAsync("test.method", new { foo = "bar" }, CancellationToken.None, TimeSpan.FromMilliseconds(50));
        } catch (TimeoutException) { /* expected */ }

        Assert.Equal(1, sendCallCount);
    }

    [Fact]
    public async Task SendRequestAsync_ClosedSocket_ThrowsInvalidOperationException()
    {
        var mockWs = CreateMockSocket(WebSocketState.Closed);
        var cfg = new AppConfig { CustomDataDir = Path.GetTempPath() };
        var framing = new MessageFraming(mockWs.Object, cfg);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await framing.SendRequestAsync("test.method", null, CancellationToken.None));
    }

    [Fact]
    public async Task SendRequestAsync_Timeout_ThrowsTimeoutException()
    {
        var mockWs = CreateMockSocket();
        var cfg = new AppConfig { CustomDataDir = Path.GetTempPath() };
        var framing = new MessageFraming(mockWs.Object, cfg);

        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await framing.SendRequestAsync("test.method", null, CancellationToken.None, TimeSpan.FromMilliseconds(50)));
    }

    [Fact]
    public async Task SendRequestAsync_CancelledToken_ThrowsOperationCancelledException()
    {
        var mockWs = CreateMockSocket();
        var cfg = new AppConfig { CustomDataDir = Path.GetTempPath() };
        var framing = new MessageFraming(mockWs.Object, cfg);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await framing.SendRequestAsync("test.method", null, cts.Token));
    }

    [Fact]
    public async Task WaitForEventAsync_CancelledToken_ThrowsOperationCancelledException()
    {
        var mockWs = CreateMockSocket();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var cfg = new AppConfig { CustomDataDir = Path.GetTempPath() };
        var framing = new MessageFraming(mockWs.Object, cfg);

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await framing.WaitForEventAsync("some.event", TimeSpan.FromSeconds(5), cts.Token));
    }

    [Fact]
    public async Task WaitForEventAsync_Timeout_ThrowsTimeoutException()
    {
        var mockWs = CreateMockSocket();
        var cfg = new AppConfig { CustomDataDir = Path.GetTempPath() };
        var framing = new MessageFraming(mockWs.Object, cfg);

        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await framing.WaitForEventAsync("some.event", TimeSpan.FromMilliseconds(50), CancellationToken.None));
    }

    [Fact]
    public async Task WaitForEventAsync_ZeroTimeout_ThrowsTimeoutException()
    {
        var mockWs = CreateMockSocket();
        var cfg = new AppConfig { CustomDataDir = Path.GetTempPath() };
        var framing = new MessageFraming(mockWs.Object, cfg);

        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await framing.WaitForEventAsync("some.event", TimeSpan.Zero, CancellationToken.None));
    }

    [Fact]
    public void ClearPendingRequests_ClearsAllPending()
    {
        var mockWs = CreateMockSocket();
        var cfg = new AppConfig { CustomDataDir = Path.GetTempPath() };
        var framing = new MessageFraming(mockWs.Object, cfg);

        // Add a pending request via reflection
        var pending = GetPendingField(framing);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending.TryAdd("test-id", tcs);

        framing.ClearPendingRequests();

        Assert.False(pending.TryGetValue("test-id", out _));
    }

    [Fact]
    public void ClearEventWaiters_ClearsAllWaiters()
    {
        var mockWs = CreateMockSocket();
        var cfg = new AppConfig { CustomDataDir = Path.GetTempPath() };
        var framing = new MessageFraming(mockWs.Object, cfg);

        // Add an event waiter via reflection
        var waiters = GetEventWaitersField(framing);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        waiters.TryAdd("some.event", tcs);

        framing.ClearEventWaiters();

        Assert.False(waiters.TryGetValue("some.event", out _));
    }

    [Fact]
    public void TryRemovePending_CalledTwice_ReturnsFalseSecondTime()
    {
        var mockWs = CreateMockSocket();
        var cfg = new AppConfig { CustomDataDir = Path.GetTempPath() };
        var framing = new MessageFraming(mockWs.Object, cfg);

        var pending = GetPendingField(framing);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending.TryAdd("test-id", tcs);

        var first = framing.TryRemovePending("test-id", out _);
        var second = framing.TryRemovePending("test-id", out _);

        Assert.True(first);
        Assert.False(second);
    }

    [Fact]
    public async Task WaitForEventAsync_Resolved_ReturnsPayload()
    {
        var mockWs = CreateMockSocket();
        var cfg = new AppConfig { CustomDataDir = Path.GetTempPath() };
        var framing = new MessageFraming(mockWs.Object, cfg);

        var waiters = GetEventWaitersField(framing);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        waiters.TryAdd("my.event", tcs);

        var resultTask = framing.WaitForEventAsync("my.event", TimeSpan.FromSeconds(2), CancellationToken.None);

        tcs.TrySetResult(JsonDocument.Parse("{\"value\":42}").RootElement);

        var result = await resultTask;
        Assert.Equal(42, result.GetProperty("value").GetInt32());
    }

    [Fact]
    public void ResolveEventWaiter_UnregistersWaiter()
    {
        var mockWs = CreateMockSocket();
        var cfg = new AppConfig { CustomDataDir = Path.GetTempPath() };
        var framing = new MessageFraming(mockWs.Object, cfg);

        var waiters = GetEventWaitersField(framing);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        waiters.TryAdd("e1", tcs);
        waiters.TryAdd("e2", tcs);

        framing.ResolveEventWaiter("e1", JsonDocument.Parse("{}").RootElement);
        framing.ResolveEventWaiter("e2", JsonDocument.Parse("{}").RootElement);

        Assert.False(waiters.TryGetValue("e1", out _));
        Assert.False(waiters.TryGetValue("e2", out _));
    }
}
