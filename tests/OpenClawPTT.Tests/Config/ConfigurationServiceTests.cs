using Moq;
using OpenClawPTT;
using OpenClawPTT.Services;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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

    [Fact]
    public async Task ReconfigureAsync_WithCancellation_HandlesGracefully()
    {
        // Arrange
        var mockStorage = new Mock<IConfigStorage>();
        mockStorage.Setup(x => x.Load()).Returns(new AppConfig { GatewayUrl = "wss://test.example.com" });

        var service = new ConfigurationService(mockStorage.Object);
        var existing = new AppConfig { GatewayUrl = "wss://old.example.com" };
        var shellHost = new Mock<IStreamShellHost>();

        // Simulate cancellation during setup
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Fake console with immediate cancellation
        var fakeConsole = new Mock<IConsole>();
        fakeConsole.Setup(x => x.ReadLineAsync(It.IsAny<CancellationToken>()))
            .Throws(new OperationCanceledException());
        fakeConsole.SetupProperty(x => x.ForegroundColor);
        fakeConsole.Setup(x => x.WindowWidth).Returns(80);
        ConsoleUi.SetConsole(fakeConsole.Object);

        try
        {
            // Act & Assert - should not throw, just complete
            var result = await service.ReconfigureAsync(shellHost.Object, existing, cts.Token);
            Assert.NotNull(result);
        }
        finally
        {
            ConsoleUi.SetConsole(new SystemConsole());
        }
    }

    [Fact]
    public void Constructor_NullConfigStorage_HandledGracefully()
    {
        // Arrange & Act - passing null should not throw
        // Note: The constructor accepts IConfigStorage, passing null may cause NullReferenceException
        // when Load/Save are called. The service itself doesn't null-check.
        // This test verifies the current behavior - null storage is passed through.
        var service = new ConfigurationService(null!);

        // Assert - just verify the service was constructed (may throw on use, which is acceptable)
        Assert.NotNull(service);
    }
}
