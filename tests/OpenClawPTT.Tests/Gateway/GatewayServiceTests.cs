using OpenClawPTT.Services;
using Xunit;

namespace OpenClawPTT.Tests;

public class GatewayServiceTests
{
    [Fact]
    public void GatewayService_Constructs_WithoutThrowing()
    {
        // Arrange: minimal config (DataDir uses default UserProfile path)
        var cfg = new AppConfig
        {
            GatewayUrl = "wss://test.example.com",
            AuthToken = "test-token",
            AudioResponseMode = "text-only"
        };

        // Act: constructing the service should not throw
        var service = new GatewayService(cfg);

        // Assert: service was created
        Assert.NotNull(service);
        service.Dispose();
    }

    [Fact]
    public void GatewayService_ImplementsIGatewayService()
    {
        var cfg = new AppConfig
        {
            GatewayUrl = "wss://test.example.com",
            AuthToken = "test-token",
            AudioResponseMode = "text-only"
        };

        IGatewayService service = new GatewayService(cfg);

        Assert.True(service is GatewayService);
        service.Dispose();
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var cfg = new AppConfig
        {
            GatewayUrl = "wss://test.example.com",
            AuthToken = "test-token",
            AudioResponseMode = "text-only"
        };

        var service = new GatewayService(cfg);

        service.Dispose();
        service.Dispose(); // should not throw

        Assert.True(true);
    }
}
