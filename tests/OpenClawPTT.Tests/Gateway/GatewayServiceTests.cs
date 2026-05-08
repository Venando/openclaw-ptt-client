using OpenClawPTT.Services;
using Xunit;
using Moq;

namespace OpenClawPTT.Tests;

public class GatewayServiceTests
{
    private static IColorConsole CreateMockConsole() => new Mock<IColorConsole>().Object;

    private static AppConfig CreateConfig() => new()
    {
        GatewayUrl = "wss://test.example.com",
        AuthToken = "test-token",
        AudioResponseMode = "text-only"
    };

    [Fact]
    public void GatewayService_Constructs_WithoutThrowing()
    {
        var service = new GatewayService(CreateConfig(), CreateMockConsole());
        Assert.NotNull(service);
        service.Dispose();
    }

    [Fact]
    public void GatewayService_ImplementsIGatewayService()
    {
        IGatewayService service = new GatewayService(CreateConfig(), CreateMockConsole());
        Assert.IsType<GatewayService>(service);
        service.Dispose();
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var service = new GatewayService(CreateConfig(), CreateMockConsole());

        service.Dispose();
        var exception = Record.Exception(() => service.Dispose());
        Assert.Null(exception);
    }
}
