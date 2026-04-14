using System.Text.Json;
using Xunit;

namespace OpenClawPTT.Tests.Gateway;

public class KeepaliveRunnerTests
{
    [Fact]
    public void Start_CallsSendAfterInterval()
    {
        var callCount = 0;
        Func<string, object?, CancellationToken, TimeSpan?, Task<JsonElement>> sender = async (_, _, _, _) =>
        {
            Interlocked.Increment(ref callCount);
            await Task.CompletedTask;
            return JsonDocument.Parse("{}").RootElement;
        };

        using var runner = new KeepaliveRunner(sender, 30);
        using var cts = new CancellationTokenSource();

        runner.Start(cts.Token);
        Thread.Sleep(120); // wait for at least 2 ticks
        cts.Cancel();
        runner.Dispose();

        Assert.True(callCount >= 2, $"Expected >= 2 ticks, got {callCount}");
    }

    [Fact]
    public void Stop_PreventsFurtherTicks()
    {
        var callCount = 0;
        Func<string, object?, CancellationToken, TimeSpan?, Task<JsonElement>> sender = async (_, _, _, _) =>
        {
            Interlocked.Increment(ref callCount);
            await Task.CompletedTask;
            return JsonDocument.Parse("{}").RootElement;
        };

        using var runner = new KeepaliveRunner(sender, 20);
        using var cts = new CancellationTokenSource();

        runner.Start(cts.Token);
        Thread.Sleep(80);
        var countAtStop = callCount;
        cts.Cancel();
        runner.Dispose();
        Thread.Sleep(80);
        var countAfterStop = callCount;

        Assert.Equal(countAtStop, countAfterStop);
    }
}
