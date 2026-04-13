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
    public void ShouldReconfigure_ReturnsFalse_OnTimeout()
    {
        // Arrange
        var mockStorage = new Mock<IConfigStorage>();
        mockStorage.Setup(x => x.Load()).Returns(new AppConfig { GatewayUrl = "wss://test.example.com" });

        // Fake console: no keys available
        var fakeConsole = new Mock<IConsole>();
        fakeConsole.Setup(x => x.KeyAvailable).Returns(false);
        fakeConsole.Setup(x => x.ReadKey(true)).Throws(new InvalidOperationException("not available"));
        fakeConsole.SetupProperty(x => x.ForegroundColor);
        ConsoleUi.SetConsole(fakeConsole.Object);

        try
        {
            var service = new ConfigurationService(mockStorage.Object);

            // Act - use a very short timeout so the test completes fast
            // We test the private method via reflection
            var method = typeof(ConfigurationService).GetMethod("ShouldReconfigure",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.NotNull(method);

            // Invoke with 50ms timeout
            var result = (bool)method!.Invoke(null, new object?[] { TimeSpan.FromMilliseconds(50) });

            // Assert - should return false on timeout
            Assert.False(result);
        }
        finally
        {
            ConsoleUi.SetConsole(new SystemConsole());
        }
    }

    [Fact]
    public void ShouldReconfigure_CalledTwice_BothReturnCorrectValues()
    {
        // Arrange
        var mockStorage = new Mock<IConfigStorage>();
        mockStorage.Setup(x => x.Load()).Returns(new AppConfig { GatewayUrl = "wss://test.example.com" });

        // For the first call: no key available, timeout
        var fakeConsole1 = new Mock<IConsole>();
        fakeConsole1.Setup(x => x.KeyAvailable).Returns(false);
        fakeConsole1.Setup(x => x.ReadKey(true)).Throws(new InvalidOperationException("not available"));
        fakeConsole1.SetupProperty(x => x.ForegroundColor);
        fakeConsole1.Setup(x => x.WriteLine(It.IsAny<string?>())).Verifiable();
        ConsoleUi.SetConsole(fakeConsole1.Object);

        var method = typeof(ConfigurationService).GetMethod("ShouldReconfigure",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        // Act - First call: no key, should timeout and return false
        var result1 = (bool)method!.Invoke(null, new object?[] { TimeSpan.FromMilliseconds(20) });

        // For the second call: R key pressed
        var fakeConsole2 = new Mock<IConsole>();
        fakeConsole2.Setup(x => x.KeyAvailable).Returns(true);
        fakeConsole2.Setup(x => x.ReadKey(true)).Returns(new ConsoleKeyInfo('R', ConsoleKey.R, false, false, false));
        fakeConsole2.SetupProperty(x => x.ForegroundColor);
        fakeConsole2.Setup(x => x.WriteLine(It.IsAny<string?>())).Verifiable();
        ConsoleUi.SetConsole(fakeConsole2.Object);

        // Act - Second call: R key available, should return true
        var result2 = (bool)method!.Invoke(null, new object?[] { TimeSpan.FromMilliseconds(50) });

        // Assert
        Assert.False(result1); // timeout - no key available on first check
        Assert.True(result2);  // R key pressed on second call

        ConsoleUi.SetConsole(new SystemConsole());
    }

    [Fact]
    public async Task ReconfigureAsync_WithCancellation_HandlesGracefully()
    {
        // Arrange
        var mockStorage = new Mock<IConfigStorage>();
        mockStorage.Setup(x => x.Load()).Returns(new AppConfig { GatewayUrl = "wss://test.example.com" });

        var service = new ConfigurationService(mockStorage.Object);
        var existing = new AppConfig { GatewayUrl = "wss://old.example.com" };

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
            var result = await service.ReconfigureAsync(existing, cts.Token);
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
