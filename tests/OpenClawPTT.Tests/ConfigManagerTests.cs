using Xunit;

namespace OpenClawPTT.Tests;

public class ConfigManagerTests
{
    private readonly ConfigManager _manager;

    public ConfigManagerTests()
    {
        _manager = new ConfigManager();
    }

    [Fact]
    public void Validate_MissingGatewayUrl_ReturnsIssues()
    {
        var cfg = new AppConfig
        {
            GatewayUrl = "",
            AuthToken = "some-token"
        };

        var issues = _manager.Validate(cfg);

        Assert.Contains(issues, i => i.Contains("Gateway URL"));
    }

    [Fact]
    public void Validate_ValidConfig_ReturnsNoIssues()
    {
        var cfg = new AppConfig
        {
            GatewayUrl = "wss://valid.example.com",
            AuthToken = "valid-token",
            SampleRate = 16000,
            ReconnectDelaySeconds = 5,
            VisualMode = VisualMode.SolidDot
        };

        var issues = _manager.Validate(cfg);

        Assert.Empty(issues);
    }

    [Fact]
    public void Validate_InvalidSampleRate_ReturnsIssues()
    {
        var cfg = new AppConfig
        {
            GatewayUrl = "wss://valid.example.com",
            AuthToken = "valid-token",
            SampleRate = 96000 // out of range
        };

        var issues = _manager.Validate(cfg);

        Assert.Contains(issues, i => i.Contains("Sample rate"));
    }

    [Fact]
    public void Validate_NonPositiveReconnectDelay_ReturnsIssues()
    {
        var cfg = new AppConfig
        {
            GatewayUrl = "wss://valid.example.com",
            AuthToken = "valid-token",
            ReconnectDelaySeconds = 0
        };

        var issues = _manager.Validate(cfg);

        Assert.Contains(issues, i => i.Contains("Reconnect delay"));
    }

    [Fact]
    public void Validate_InvalidGatewayUrlScheme_ReturnsIssues()
    {
        var cfg = new AppConfig
        {
            GatewayUrl = "http://not-ws.example.com",
            AuthToken = "valid-token"
        };

        var issues = _manager.Validate(cfg);

        Assert.Contains(issues, i => i.Contains("Gateway URL"));
    }

    [Fact]
    public void Validate_MissingAuthAndDeviceToken_ReturnsIssues()
    {
        var cfg = new AppConfig
        {
            GatewayUrl = "wss://valid.example.com",
            AuthToken = "",
            DeviceToken = ""
        };

        var issues = _manager.Validate(cfg);

        Assert.Contains(issues, i => i.Contains("Auth token"));
    }
}
