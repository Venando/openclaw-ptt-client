namespace OpenClawPTT.Tests;

using Moq;
using OpenClawPTT;
using OpenClawPTT.Services;
using System;
using System.Threading;
using Xunit;

public class AppRunnerTests
{
    private static AppConfig DefaultConfig => new()
    {
        HotkeyCombination = "Alt+=",
        HoldToTalk = false
    };

    #region Test 1: AppRunner_Constructs_WithValidDeps

    [Fact]
    public void AppRunner_Constructs_WithValidDeps()
    {
        var mockFactory = new Mock<IServiceFactory>();
        var runner = new AppRunner(DefaultConfig, mockFactory.Object);
        Assert.NotNull(runner);
    }

    #endregion

    #region Test 2: AppRunner_RunAsync_ThrowsOperationCanceledException_WhenCTIsCanceled

    [Fact]
    public async Task AppRunner_RunAsync_ThrowsOperationCanceledException_WhenCTIsCanceled()
    {
        var mockFactory = new Mock<IServiceFactory>();
        var mockGateway = new Mock<IGatewayService>();
        mockGateway.Setup(x => x.ConnectAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        mockFactory.Setup(x => x.CreateGatewayService(It.IsAny<AppConfig>()))
            .Returns(mockGateway.Object);

        using var runner = new AppRunner(DefaultConfig, mockFactory.Object);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => runner.RunAsync(cts.Token));
    }

    #endregion

    #region Test 3: AppRunner_RunAsync_Returns0_OnNormalExit

    [Fact]
    public async Task AppRunner_RunAsync_Returns0_OnNormalExit()
    {
        var mockFactory = new Mock<IServiceFactory>();
        var mockGateway = new Mock<IGatewayService>();
        var mockAudio = new Mock<IAudioService>();
        var mockPttLoop = new Mock<IPttLoop>();

        mockGateway.Setup(x => x.ConnectAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mockFactory.Setup(x => x.CreateGatewayService(It.IsAny<AppConfig>()))
            .Returns(mockGateway.Object);
        mockFactory.Setup(x => x.CreateAudioService(It.IsAny<AppConfig>()))
            .Returns(mockAudio.Object);
        mockFactory.Setup(x => x.CreatePttController(It.IsAny<AppConfig>(), It.IsAny<IAudioService>()))
            .Returns(new Mock<IPttController>().Object);
        mockFactory.Setup(x => x.CreateTextMessageSender(It.IsAny<IGatewayService>()))
            .Returns(new Mock<ITextMessageSender>().Object);
        mockFactory.Setup(x => x.CreateInputHandler(It.IsAny<IGatewayService>(), It.IsAny<IAudioService>(), It.IsAny<ITextMessageSender>()))
            .Returns(new Mock<IInputHandler>().Object);

        mockPttLoop.Setup(x => x.RunAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(PttLoopExitCode.Ok);
        mockFactory.Setup(x => x.CreatePttLoop(
            It.IsAny<AppConfig>(),
            It.IsAny<IGatewayService>(),
            It.IsAny<IAudioService>(),
            It.IsAny<IPttController>(),
            It.IsAny<ITextMessageSender>(),
            It.IsAny<IInputHandler>()))
            .Returns(mockPttLoop.Object);

        using var runner = new AppRunner(DefaultConfig, mockFactory.Object);
        var result = await runner.RunAsync(CancellationToken.None);

        Assert.Equal(0, result);
    }

    #endregion

    #region Test 4: AppRunner_RunAsync_RestartsAndReturns0

    [Fact]
    public async Task AppRunner_RunAsync_RestartsAndReturns0()
    {
        var mockFactory = new Mock<IServiceFactory>();
        var mockGateway = new Mock<IGatewayService>();
        var mockAudio = new Mock<IAudioService>();
        var mockPttLoop = new Mock<IPttLoop>();

        mockGateway.Setup(x => x.ConnectAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mockFactory.Setup(x => x.CreateGatewayService(It.IsAny<AppConfig>()))
            .Returns(mockGateway.Object);
        mockFactory.Setup(x => x.CreateAudioService(It.IsAny<AppConfig>()))
            .Returns(mockAudio.Object);
        mockFactory.Setup(x => x.CreatePttController(It.IsAny<AppConfig>(), It.IsAny<IAudioService>()))
            .Returns(new Mock<IPttController>().Object);
        mockFactory.Setup(x => x.CreateTextMessageSender(It.IsAny<IGatewayService>()))
            .Returns(new Mock<ITextMessageSender>().Object);
        mockFactory.Setup(x => x.CreateInputHandler(It.IsAny<IGatewayService>(), It.IsAny<IAudioService>(), It.IsAny<ITextMessageSender>()))
            .Returns(new Mock<IInputHandler>().Object);

        var callCount = 0;
        mockPttLoop.Setup(x => x.RunAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount == 1 ? PttLoopExitCode.Restart : PttLoopExitCode.Ok;
            });
        mockFactory.Setup(x => x.CreatePttLoop(
            It.IsAny<AppConfig>(),
            It.IsAny<IGatewayService>(),
            It.IsAny<IAudioService>(),
            It.IsAny<IPttController>(),
            It.IsAny<ITextMessageSender>(),
            It.IsAny<IInputHandler>()))
            .Returns(mockPttLoop.Object);

        using var runner = new AppRunner(DefaultConfig, mockFactory.Object);
        var result = await runner.RunAsync(CancellationToken.None);

        Assert.Equal(0, result);
        Assert.Equal(2, callCount); // first → restart, second → ok → exit
    }

    #endregion

    #region Test 5: AppRunner_Disposes_OwnedResources

    [Fact]
    public async Task AppRunner_Disposes_OwnedResources()
    {
        var mockFactory = new Mock<IServiceFactory>();
        var mockGateway = new Mock<IGatewayService>();
        var mockAudio = new Mock<IAudioService>();
        var mockPttLoop = new Mock<IPttLoop>();

        mockGateway.Setup(x => x.ConnectAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mockFactory.Setup(x => x.CreateGatewayService(It.IsAny<AppConfig>()))
            .Returns(mockGateway.Object);
        mockFactory.Setup(x => x.CreateAudioService(It.IsAny<AppConfig>()))
            .Returns(mockAudio.Object);
        mockFactory.Setup(x => x.CreatePttController(It.IsAny<AppConfig>(), It.IsAny<IAudioService>()))
            .Returns(new Mock<IPttController>().Object);
        mockFactory.Setup(x => x.CreateTextMessageSender(It.IsAny<IGatewayService>()))
            .Returns(new Mock<ITextMessageSender>().Object);
        mockFactory.Setup(x => x.CreateInputHandler(It.IsAny<IGatewayService>(), It.IsAny<IAudioService>(), It.IsAny<ITextMessageSender>()))
            .Returns(new Mock<IInputHandler>().Object);

        mockPttLoop.Setup(x => x.RunAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(PttLoopExitCode.Ok);
        mockFactory.Setup(x => x.CreatePttLoop(
            It.IsAny<AppConfig>(),
            It.IsAny<IGatewayService>(),
            It.IsAny<IAudioService>(),
            It.IsAny<IPttController>(),
            It.IsAny<ITextMessageSender>(),
            It.IsAny<IInputHandler>()))
            .Returns(mockPttLoop.Object);

        using var runner = new AppRunner(DefaultConfig, mockFactory.Object);
        await runner.RunAsync(CancellationToken.None);

        mockGateway.Verify(x => x.Dispose(), Times.Once);
        mockAudio.Verify(x => x.Dispose(), Times.Once);
        mockPttLoop.Verify(x => x.Dispose(), Times.Once);
    }

    #endregion
}
