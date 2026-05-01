using Moq;
using OpenClawPTT.Services;

namespace OpenClawPTT.Tests;

/// <summary>
/// Tests for ConsoleUi.PrintUserMessage — verifies both raw console and
/// StreamShell routing paths.
/// ConsoleUi is a static class with shared state, so tests are not parallelizable.
/// </summary>
[Collection("ConsoleUi")]
public sealed class PrintUserMessageTests : IDisposable
{
    private readonly Mock<IConsole> _mockConsole;
    private readonly Mock<IStreamShellHost> _mockShellHost;

    public PrintUserMessageTests()
    {
        _mockConsole = new Mock<IConsole>(MockBehavior.Strict);
        _mockShellHost = new Mock<IStreamShellHost>(MockBehavior.Strict);

        ConsoleUi.SetConsole(_mockConsole.Object);
        ConsoleUi.SetStreamShellHost(null);
    }

    [Fact]
    public void WithoutShell_WritesToRawConsole()
    {
        // Arrange
        var sequence = new MockSequence();
        _mockConsole.InSequence(sequence).SetupSet(c => c.ForegroundColor = ConsoleColor.Green);
        _mockConsole.InSequence(sequence).Setup(c => c.Write("  You: "));
        _mockConsole.InSequence(sequence).Setup(c => c.ResetColor());
        _mockConsole.InSequence(sequence).Setup(c => c.WriteLine("hello world"));

        // Act
        ConsoleUi.PrintUserMessage("hello world");

        // Assert — Moq strict mock verifies all setups were called
        _mockConsole.VerifyAll();
    }

    [Fact]
    public void WithoutShell_ResetsColorAfterWrite()
    {
        // Arrange
        _mockConsole.SetupSet(c => c.ForegroundColor = ConsoleColor.Green);
        _mockConsole.Setup(c => c.Write(It.IsAny<string>()));
        _mockConsole.Setup(c => c.WriteLine(It.IsAny<string>()));
        _mockConsole.Setup(c => c.ResetColor());

        // Act
        ConsoleUi.PrintUserMessage("hello world");

        // Assert
        _mockConsole.Verify(c => c.ResetColor(), Times.Once);
    }

    [Fact]
    public void WithShell_AddsMessageToShellHost()
    {
        // Arrange
        ConsoleUi.SetStreamShellHost(_mockShellHost.Object);
        _mockShellHost.Setup(h => h.AddMessage("[green]  You:[/] hello world"));

        // Act
        ConsoleUi.PrintUserMessage("hello world");

        // Assert
        _mockShellHost.Verify(h => h.AddMessage("[green]  You:[/] hello world"), Times.Once);
    }

    [Fact]
    public void WithShell_UsesMarkupEscape()
    {
        // Arrange
        ConsoleUi.SetStreamShellHost(_mockShellHost.Object);
        _mockShellHost.Setup(h => h.AddMessage(It.IsAny<string>()));

        // Act — text containing Spectre markup brackets should be escaped
        ConsoleUi.PrintUserMessage("hello [bold]world[/]");

        // Assert — Markup.Escape turns [ into [[ and ] into ]],
        // so escaped brackets appear as literal text in the message
        _mockShellHost.Verify(h => h.AddMessage(
            It.Is<string>(s => s.Contains("[bold]") && s.Contains("[/]"))),
            Times.Once);
    }

    public void Dispose()
    {
        ConsoleUi.SetStreamShellHost(null);
        ConsoleUi.SetConsole(new SystemConsole());
    }
}
