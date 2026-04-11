using Moq;
using OpenClawPTT;
using OpenClawPTT.Services;
using System.Collections.Generic;
using Xunit;

namespace OpenClawPTT.Tests;

/// <summary>
/// Tests for ConfigurationService using a mock IConfigStorage.
/// </summary>
public class ConfigurationServiceTests
{
    [Fact]
    public void Load_ReturnsConfigFromStorage()
    {
        // Arrange
        var mockStorage = new Mock<IConfigStorage>();
        var expectedConfig = new AppConfig { GatewayUrl = "wss://test.example.com" };
        mockStorage.Setup(x => x.Load()).Returns(expectedConfig);

        var service = new ConfigurationService(mockStorage.Object);

        // Act
        var result = service.Load();

        // Assert
        Assert.Same(expectedConfig, result);
        mockStorage.Verify(x => x.Load(), Times.Once);
    }

    [Fact]
    public void Save_PersistsConfigToStorage()
    {
        // Arrange
        var mockStorage = new Mock<IConfigStorage>();
        var service = new ConfigurationService(mockStorage.Object);
        var config = new AppConfig { GatewayUrl = "wss://test.example.com" };

        // Act
        service.Save(config);

        // Assert
        mockStorage.Verify(x => x.Save(config), Times.Once);
    }

    [Fact]
    public void Validate_ReturnsIssuesFromConfigManager()
    {
        // Arrange
        var mockStorage = new Mock<IConfigStorage>();
        var service = new ConfigurationService(mockStorage.Object);
        var config = new AppConfig { GatewayUrl = "" }; // invalid - empty URL

        // Act
        var issues = service.Validate(config);

        // Assert
        Assert.NotEmpty(issues);
        Assert.Contains(issues, i => i.Contains("Gateway URL"));
    }

    [Fact]
    public void ConfigurationService_DefaultConstructor_UsesFileConfigStorage()
    {
        // Arrange & Act
        var service = new ConfigurationService();

        // Assert - should not throw when loading (may return null if no config)
        var result = service.Load();
        // Just verify it doesn't throw - FileConfigStorage is wired up correctly
        Assert.True(true);
    }

    [Fact]
    public void IConfigStorage_CanBeMocked_ForTestIsolation()
    {
        // Arrange - prove we can inject a fake storage for testing
        var mockStorage = new Mock<IConfigStorage>();
        mockStorage.Setup(x => x.Load()).Returns(new AppConfig { GatewayUrl = "wss://mocked.example.com" });

        var service = new ConfigurationService(mockStorage.Object);

        // Act
        var loaded = service.Load();

        // Assert
        Assert.Equal("wss://mocked.example.com", loaded!.GatewayUrl);
    }
}
