using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Reflection;
using System.Text.Json;
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

    [Fact]
    public void MessageFraming_OpenSocket_ConstructsSuccessfully()
    {
        var mockWs = CreateMockSocket(WebSocketState.Open);
        var cfg = new AppConfig();
        var framing = new MessageFraming(mockWs.Object, cfg);
        Assert.NotNull(framing);
    }

    // ─── TryRemovePending tests ────────────────────────────────────

    [Fact]
    public void TryRemovePending_ExistingId_ReturnsTrueAndRemovesTcs()
    {
        var mockWs = CreateMockSocket();
        var cfg = new AppConfig();
        var framing = new MessageFraming(mockWs.Object, cfg);

        // Use reflection to populate _pending directly
        var pendingField = typeof(MessageFraming).GetField("_pending",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var pending = (ConcurrentDictionary<string, TaskCompletionSource<JsonElement>>)pendingField!.GetValue(framing)!;
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending["ptt-000001"] = tcs;

        var found = framing.TryRemovePending("ptt-000001", out var removed);

        Assert.True(found);
        Assert.Same(tcs, removed);
        Assert.False(pending.ContainsKey("ptt-000001"));
    }

    // ─── ResolveEventWaiter tests ──────────────────────────────────

    [Fact]
    public void ResolveEventWaiter_WithWaitingTask_ResolvesTaskWithPayload()
    {
        var mockWs = CreateMockSocket();
        var cfg = new AppConfig();
        var framing = new MessageFraming(mockWs.Object, cfg);

        var eventField = typeof(MessageFraming).GetField("_eventWaiters",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var eventWaiters = (ConcurrentDictionary<string, TaskCompletionSource<JsonElement>>)eventField!.GetValue(framing)!;
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        eventWaiters["test.event"] = tcs;

        var payload = JsonDocument.Parse("{\"key\":\"value\"}").RootElement;
        framing.ResolveEventWaiter("test.event", payload);

        Assert.True(tcs.Task.IsCompletedSuccessfully);
        Assert.Equal("value", tcs.Task.Result.GetProperty("key").GetString());
    }

    [Fact]
    public void ResolveEventWaiter_NoWaitingTask_DoesNotThrow()
    {
        var mockWs = CreateMockSocket();
        var cfg = new AppConfig();
        var framing = new MessageFraming(mockWs.Object, cfg);

        var payload = JsonDocument.Parse("{}").RootElement;
        var exception = Record.Exception(() => framing.ResolveEventWaiter("nonexistent.event", payload));

        Assert.Null(exception);
    }

    // ─── Clear with actual pending items ──────────────────────────

    [Fact]
    public void ClearPendingRequests_WithPendingItems_CancelsAllAndClears()
    {
        var mockWs = CreateMockSocket();
        var cfg = new AppConfig();
        var framing = new MessageFraming(mockWs.Object, cfg);

        var pendingField = typeof(MessageFraming).GetField("_pending",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var pending = (ConcurrentDictionary<string, TaskCompletionSource<JsonElement>>)pendingField!.GetValue(framing)!;
        var tcs1 = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcs2 = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending["req-1"] = tcs1;
        pending["req-2"] = tcs2;

        framing.ClearPendingRequests();

        Assert.Empty(pending);
        Assert.True(tcs1.Task.IsCanceled);
        Assert.True(tcs2.Task.IsCanceled);
    }

    [Fact]
    public void ClearEventWaiters_WithWaitingTasks_CancelsAllAndClears()
    {
        var mockWs = CreateMockSocket();
        var cfg = new AppConfig();
        var framing = new MessageFraming(mockWs.Object, cfg);

        var eventField = typeof(MessageFraming).GetField("_eventWaiters",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var eventWaiters = (ConcurrentDictionary<string, TaskCompletionSource<JsonElement>>)eventField!.GetValue(framing)!;
        var tcs1 = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcs2 = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        eventWaiters["event.1"] = tcs1;
        eventWaiters["event.2"] = tcs2;

        framing.ClearEventWaiters();

        Assert.Empty(eventWaiters);
        Assert.True(tcs1.Task.IsCanceled);
        Assert.True(tcs2.Task.IsCanceled);
    }

    // ─── NextId format ─────────────────────────────────────────────

    [Fact]
    public void NextId_Format_MatchesPttSequencePattern()
    {
        var mockWs = CreateMockSocket();
        var cfg = new AppConfig();
        var framing = new MessageFraming(mockWs.Object, cfg);

        var id = framing.NextId();

        Assert.Matches(@"^ptt-\d{6}$", id);
    }
}