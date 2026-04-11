using Moq;
using OpenClawPTT;
using OpenClawPTT.Services;
using System.Threading;
using Xunit;

namespace OpenClawPTT.Tests;

public class TextMessageSenderTests
{
    private readonly MessageComposer _composer = new();

    [Fact]
    public async Task SendAsync_ComposesAndSendsText()
    {
        var mockGateway = new Mock<IGatewayService>();
        var mockConfig = new Mock<IConfigurationService>();
        var mockConsole = new Mock<IConsoleOutput>();

        mockConfig.Setup(x => x.Load()).Returns(new AppConfig());

        var sender = new TextMessageSender(mockGateway.Object, mockConfig.Object, mockConsole.Object, _composer);
        await sender.SendAsync("hello world", CancellationToken.None);

        mockGateway.Verify(x => x.SendTextAsync("hello world", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendAsync_AudioEnabledBoth_AppliesAudioWrapPrompt()
    {
        var mockGateway = new Mock<IGatewayService>();
        var mockConfig = new Mock<IConfigurationService>();
        var mockConsole = new Mock<IConsoleOutput>();

        mockConfig.Setup(x => x.Load()).Returns(new AppConfig
        {
            AudioResponseMode = "both",
            AudioWrapPrompt = "[Speak this]"
        });

        var sender = new TextMessageSender(mockGateway.Object, mockConfig.Object, mockConsole.Object, _composer);
        await sender.SendAsync("hello", CancellationToken.None);

        mockGateway.Verify(x => x.SendTextAsync(
            "[Speak this]\n\nhello",
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SendAsync_TextOnly_DoesNotApplyWrapPrompt()
    {
        var mockGateway = new Mock<IGatewayService>();
        var mockConfig = new Mock<IConfigurationService>();
        var mockConsole = new Mock<IConsoleOutput>();

        mockConfig.Setup(x => x.Load()).Returns(new AppConfig
        {
            AudioResponseMode = "text-only",
            AudioWrapPrompt = "[Should not appear]"
        });

        var sender = new TextMessageSender(mockGateway.Object, mockConfig.Object, mockConsole.Object, _composer);
        await sender.SendAsync("hello", CancellationToken.None);

        mockGateway.Verify(x => x.SendTextAsync("hello", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendAsync_GatewayThrows_DoesNotRethrow()
    {
        var mockGateway = new Mock<IGatewayService>();
        var mockConfig = new Mock<IConfigurationService>();
        var mockConsole = new Mock<IConsoleOutput>();

        mockConfig.Setup(x => x.Load()).Returns(new AppConfig());
        mockGateway.Setup(x => x.SendTextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new System.IO.IOException("network error"));

        var sender = new TextMessageSender(mockGateway.Object, mockConfig.Object, mockConsole.Object, _composer);

        // Should not throw — error is swallowed and printed to console
        await sender.SendAsync("hello", CancellationToken.None);

        mockConsole.Verify(x => x.PrintError(It.Is<string>(s => s.Contains("network error"))), Times.Once);
    }

    [Fact]
    public async Task SendAsync_AudioEnabledEmptyWrapPrompt_DoesNotPrepend()
    {
        var mockGateway = new Mock<IGatewayService>();
        var mockConfig = new Mock<IConfigurationService>();
        var mockConsole = new Mock<IConsoleOutput>();

        mockConfig.Setup(x => x.Load()).Returns(new AppConfig
        {
            AudioResponseMode = "both",
            AudioWrapPrompt = ""
        });

        var sender = new TextMessageSender(mockGateway.Object, mockConfig.Object, mockConsole.Object, _composer);
        await sender.SendAsync("hello", CancellationToken.None);

        mockGateway.Verify(x => x.SendTextAsync("hello", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendAsync_UsesInjectedMessageComposer()
    {
        // Test that the injected IMessageComposer is used, enabling mocking
        var mockGateway = new Mock<IGatewayService>();
        var mockConfig = new Mock<IConfigurationService>();
        var mockConsole = new Mock<IConsoleOutput>();
        var mockComposer = new Mock<IMessageComposer>();

        mockConfig.Setup(x => x.Load()).Returns(new AppConfig { AudioResponseMode = "text-only" });
        mockComposer.Setup(x => x.ComposeOutgoing(It.IsAny<string>(), It.IsAny<AppConfig>()))
            .Returns<string, AppConfig>((text, _) => "[MOCKED]" + text);

        var sender = new TextMessageSender(mockGateway.Object, mockConfig.Object, mockConsole.Object, mockComposer.Object);
        await sender.SendAsync("hello", CancellationToken.None);

        mockGateway.Verify(x => x.SendTextAsync("[MOCKED]hello", It.IsAny<CancellationToken>()), Times.Once);
    }
}
