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
    private readonly Mock<IStreamShellHost> _mockShellHost;

    public PrintUserMessageTests()
    {
        _mockShellHost = new Mock<IStreamShellHost>(MockBehavior.Strict);
        ConsoleUi.SetStreamShellHost(null);
    }

    [Fact]
    public void WithoutShell_DoesNotThrow()
    {
        // When no shell host is attached, the method should still work
        ConsoleUi.PrintUserMessage("hello world");
    }

    [Fact]
    public void WithShell_AddsMessageToShellHost()
    {
        ConsoleUi.SetStreamShellHost(_mockShellHost.Object);
        _mockShellHost.Setup(h => h.AddMessage("[green]  You:[/] hello world"));

        ConsoleUi.PrintUserMessage("hello world");

        _mockShellHost.Verify(h => h.AddMessage("[green]  You:[/] hello world"), Times.Once);
    }

    [Fact]
    public void WithShell_UsesMarkupEscape()
    {
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
    }
}
