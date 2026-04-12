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

    [Fact]
    public async Task SendAsync_ConfigLoadReturnsNull_ThrowsInvalidOperationException()
    {
        var mockGateway = new Mock<IGatewayService>();
        var mockConfig = new Mock<IConfigurationService>();
        var mockConsole = new Mock<IConsoleOutput>();

        mockConfig.Setup(x => x.Load()).Returns((AppConfig?)null);

        var sender = new TextMessageSender(mockGateway.Object, mockConfig.Object, mockConsole.Object, _composer);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sender.SendAsync("hello", CancellationToken.None));

        Assert.Contains("Configuration not loaded", ex.Message);
        mockGateway.Verify(x => x.SendTextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendAsync_EmptyMessage_SendsWithoutCrashing()
    {
        var mockGateway = new Mock<IGatewayService>();
        var mockConfig = new Mock<IConfigurationService>();
        var mockConsole = new Mock<IConsoleOutput>();

        mockConfig.Setup(x => x.Load()).Returns(new AppConfig());
        mockGateway.Setup(x => x.SendTextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sender = new TextMessageSender(mockGateway.Object, mockConfig.Object, mockConsole.Object, _composer);

        // Should not throw — empty string is passed through as-is
        await sender.SendAsync(string.Empty, CancellationToken.None);

        mockGateway.Verify(x => x.SendTextAsync(string.Empty, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendAsync_WhitespaceOnlyMessage_SendsWithoutCrashing()
    {
        var mockGateway = new Mock<IGatewayService>();
        var mockConfig = new Mock<IConfigurationService>();
        var mockConsole = new Mock<IConsoleOutput>();

        mockConfig.Setup(x => x.Load()).Returns(new AppConfig());
        mockGateway.Setup(x => x.SendTextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);


        var sender = new TextMessageSender(mockGateway.Object, mockConfig.Object, mockConsole.Object, _composer);


        await sender.SendAsync("   \t\n   ", CancellationToken.None);

        mockGateway.Verify(x => x.SendTextAsync("   \t\n   ", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendAsync_VeryLongMessage_SentWithoutTruncation()
    {
        var mockGateway = new Mock<IGatewayService>();
        var mockConfig = new Mock<IConfigurationService>();
        var mockConsole = new Mock<IConsoleOutput>();

        mockConfig.Setup(x => x.Load()).Returns(new AppConfig());

        string capturedText = null!;
        mockGateway.Setup(x => x.SendTextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((text, _) => capturedText = text)
            .Returns(Task.CompletedTask);

        var sender = new TextMessageSender(mockGateway.Object, mockConfig.Object, mockConsole.Object, _composer);
        var longMessage = new string('x', 50_000);

        await sender.SendAsync(longMessage, CancellationToken.None);


        Assert.Equal(longMessage.Length, capturedText.Length);
        Assert.Equal(longMessage, capturedText);
    }

    [Fact]
    public async Task SendAsync_ConcurrentCalls_BothCompleteWithoutInterference()
    {
        var mockGateway = new Mock<IGatewayService>();
        var mockConfig = new Mock<IConfigurationService>();
        var mockConsole = new Mock<IConsoleOutput>();

        mockConfig.Setup(x => x.Load()).Returns(new AppConfig());

        var sentTexts = new System.Collections.Concurrent.ConcurrentBag<string>();
        mockGateway.Setup(x => x.SendTextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((text, _) => sentTexts.Add(text))
            .Returns(Task.CompletedTask);

        var sender = new TextMessageSender(mockGateway.Object, mockConfig.Object, mockConsole.Object, _composer);

        var task1 = sender.SendAsync("message one", CancellationToken.None);
        var task2 = sender.SendAsync("message two", CancellationToken.None);
        var task3 = sender.SendAsync("message three", CancellationToken.None);

        await Task.WhenAll(task1, task2, task3);

        Assert.Equal(3, sentTexts.Count);
        Assert.Contains("message one", sentTexts);
        Assert.Contains("message two", sentTexts);
        Assert.Contains("message three", sentTexts);
    }
}
