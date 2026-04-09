using Moq;
using OpenClawPTT.Services;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace OpenClawPTT.Tests;

public class InputHandlerTests
{
    private readonly Mock<ITextMessageSender> _mockSender;
    private readonly Mock<IConfigurationService> _mockConfig;
    private readonly Mock<IConsoleOutput> _mockConsole;
    private readonly InputHandler _handler;

    public InputHandlerTests()
    {
        _mockSender = new Mock<ITextMessageSender>();
        _mockConfig = new Mock<IConfigurationService>();
        _mockConsole = new Mock<IConsoleOutput>();
        _handler = new InputHandler(_mockSender.Object, _mockConfig.Object, _mockConsole.Object);
    }

    [Fact(Skip = "Requires console - Console.KeyAvailable throws in test environment")]
    public async Task HandleInputAsync_NoKey_ReturnsZero()
    {
        var ct = new CancellationTokenSource(100).Token;
        var result = await _handler.HandleInputAsync(ct);
        Assert.Equal(0, result);
    }
}
