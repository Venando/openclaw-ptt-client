using Moq;
using OpenClawPTT;
using Xunit;

namespace OpenClawPTT.Tests;

/// <summary>
/// Tests for DeviceIdentity using a mock IPlatformInfo.
/// </summary>
public class DeviceIdentityTests
{
    [Fact]
    public void GetPlatform_Static_ReturnsPlatformString()
    {
        // Static method should work (backward compatibility)
        var platform = DeviceIdentity.GetPlatform();
        Assert.NotNull(platform);
        Assert.True(platform == "windows" || platform == "macos" || platform == "linux");
    }

    [Fact]
    public void GetCurrentPlatform_WithMockPlatformInfo_ReturnsInjectedPlatform()
    {
        // Arrange
        var mockPlatformInfo = new Mock<IPlatformInfo>();
        mockPlatformInfo.Setup(x => x.GetPlatform()).Returns("freebsd");

        // Create temp directory for key storage
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var identity = new DeviceIdentity(tempDir, mockPlatformInfo.Object);

            // Act
            var platform = identity.GetCurrentPlatform();

            // Assert
            Assert.Equal("freebsd", platform);
            mockPlatformInfo.Verify(x => x.GetPlatform(), Times.Once);
        }
        finally
        {
            // Cleanup
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void Constructor_Default_UsesSystemPlatformInfo()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            // Act - use default constructor
            var identity = new DeviceIdentity(tempDir);

            // Assert - should not throw and platform should be valid
            var platform = identity.GetCurrentPlatform();
            Assert.NotNull(platform);
            Assert.True(platform == "windows" || platform == "macos" || platform == "linux");
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void EnsureKeypair_GeneratesKeypairSuccessfully()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var mockPlatformInfo = new Mock<IPlatformInfo>();
            mockPlatformInfo.Setup(x => x.GetPlatform()).Returns("linux");

            var identity = new DeviceIdentity(tempDir, mockPlatformInfo.Object);

            // Act
            identity.EnsureKeypair();

            // Assert
            Assert.NotEmpty(identity.DeviceId);
            Assert.NotEmpty(identity.PublicKeyBase64);
            Assert.True(identity.DeviceId.Length == 64); // SHA256 hex = 64 chars
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void Sign_ProducesNonEmptySignature()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var identity = new DeviceIdentity(tempDir);
            identity.EnsureKeypair();

            // Act
            var signature = identity.Sign("test payload");

            // Assert
            Assert.NotEmpty(signature);
            // Ed25519 signature is 64 bytes = ~88 chars in base64url
            Assert.True(signature.Length > 80);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void IPlatformInfo_CanBeMocked_ForTestIsolation()
    {
        // Prove we can substitute a fake IPlatformInfo
        var mockPlatformInfo = new Mock<IPlatformInfo>();
        mockPlatformInfo.Setup(x => x.GetPlatform()).Returns("wasm");

        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var identity = new DeviceIdentity(tempDir, mockPlatformInfo.Object);
            identity.EnsureKeypair();

            var platform = identity.GetCurrentPlatform();
            Assert.Equal("wasm", platform);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }
}
