using System.Text.Json;
using Moq;
using OpenClawPTT.Services;

namespace OpenClawPTT.Tests.Gateway;

public class ModelFallbackEventTests
{
    [Fact]
    public void FailedProvider_ReadsFromProvider()
    {
        var payload = JsonDocument.Parse("{\"fromProvider\":\"kimi\"}").RootElement.Clone();
        var evt = new ModelFallbackEvent("model.failover", payload);

        Assert.Equal("kimi", evt.FailedProvider);
    }

    [Fact]
    public void FailedModel_ReadsFromModel()
    {
        var payload = JsonDocument.Parse("{\"fromModel\":\"kimi-k2.6\"}").RootElement.Clone();
        var evt = new ModelFallbackEvent("model.failover", payload);

        Assert.Equal("kimi-k2.6", evt.FailedModel);
    }

    [Fact]
    public void FallbackProvider_ReadsToProvider()
    {
        var payload = JsonDocument.Parse("{\"toProvider\":\"deepseek\"}").RootElement.Clone();
        var evt = new ModelFallbackEvent("model.failover", payload);

        Assert.Equal("deepseek", evt.FallbackProvider);
    }

    [Fact]
    public void FallbackModel_ReadsToModel()
    {
        var payload = JsonDocument.Parse("{\"toModel\":\"deepseek-v4-flash\"}").RootElement.Clone();
        var evt = new ModelFallbackEvent("model.failover", payload);

        Assert.Equal("deepseek-v4-flash", evt.FallbackModel);
    }

    [Fact]
    public void ErrorMessage_ReadsReason()
    {
        var payload = JsonDocument.Parse("{\"reason\":\"rate_limit\"}").RootElement.Clone();
        var evt = new ModelFallbackEvent("model.failover", payload);

        Assert.Equal("rate_limit", evt.ErrorMessage);
    }

    [Fact]
    public void Succeeded_AlwaysTrue()
    {
        var payload = JsonDocument.Parse("{}").RootElement.Clone();
        var evt = new ModelFallbackEvent("model.failover", payload);

        Assert.True(evt.Succeeded);
    }

    [Fact]
    public void TryGet_NonStringProperty_ReturnsNull()
    {
        var payload = JsonDocument.Parse("{\"fromProvider\":42}").RootElement.Clone();
        var evt = new ModelFallbackEvent("model.failover", payload);

        Assert.Null(evt.FailedProvider);
    }

    [Fact]
    public void TryGet_NonObjectPayload_ReturnsNull()
    {
        var payload = JsonDocument.Parse("\"string\"").RootElement.Clone();
        var evt = new ModelFallbackEvent("model.failover", payload);

        Assert.Null(evt.FailedProvider);
        Assert.Null(evt.FailedModel);
        Assert.Null(evt.FallbackProvider);
        Assert.Null(evt.FallbackModel);
        Assert.Null(evt.ErrorMessage);
    }

    [Fact]
    public void TryGet_MissingProperty_ReturnsNull()
    {
        var payload = JsonDocument.Parse("{\"unrelated\":\"value\"}").RootElement.Clone();
        var evt = new ModelFallbackEvent("model.failover", payload);

        Assert.Null(evt.FailedProvider);
    }
}

public class ModelFallbackHandlerTests
{
    private readonly Mock<IColorConsole> _mockConsole;
    private readonly ModelFallbackHandler _handler;

    public ModelFallbackHandlerTests()
    {
        _mockConsole = new Mock<IColorConsole>();
        _handler = new ModelFallbackHandler(_mockConsole.Object);
    }

    [Fact]
    public async Task HandleAsync_Success_PrintsModelFallback()
    {
        var payload = JsonDocument.Parse("{\"fromProvider\":\"kimi\",\"fromModel\":\"kimi-k2.6\",\"toProvider\":\"deepseek\",\"toModel\":\"deepseek-v4-flash\",\"reason\":\"rate_limit\"}").RootElement.Clone();
        var evt = new ModelFallbackEvent("model.failover", payload);

        await _handler.HandleAsync(evt);

        _mockConsole.Verify(c => c.PrintModelFallback("kimi", "kimi-k2.6", "deepseek", "deepseek-v4-flash", false), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_UnknownProvider_FallsBackToUnknown()
    {
        var payload = JsonDocument.Parse("{}").RootElement.Clone();
        var evt = new ModelFallbackEvent("model.failover", payload);

        await _handler.HandleAsync(evt);

        _mockConsole.Verify(c => c.PrintModelFallback("Unknown", "Unknown", "Unknown", "Unknown", false), Times.Once);
    }

    [Fact]
    public void Constructor_NullConsole_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ModelFallbackHandler(null!));
    }
}
