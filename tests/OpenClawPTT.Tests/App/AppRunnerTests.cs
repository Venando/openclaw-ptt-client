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

    /// <summary>
    /// Test-double IServiceFactory that records which methods were called
    /// and returns Moq-controlled mock instances.
    /// </summary>
    private sealed class TestServiceFactory : IServiceFactory
    {
        public Mock<IGatewayService> Gateway { get; } = new();
        public Mock<IAudioService> Audio { get; } = new();
        public Mock<IPttController> PttController { get; } = new();
        public Mock<ITextMessageSender> TextSender { get; } = new();
        public Mock<IInputHandler> InputHandler { get; } = new();
        public Mock<IAppLoop> PttLoop { get; } = new();

        public IGatewayService CreateGatewayService(AppConfig cfg) => Gateway.Object;
        public IAudioService CreateAudioService(AppConfig cfg) => Audio.Object;
        public IPttController CreatePttController(AppConfig cfg, IAudioService audioService, IHotkeyHookFactory? hotkeyHookFactory = null) => PttController.Object;
        public ITextMessageSender CreateTextMessageSender(IGatewayService gateway) => TextSender.Object;
        public IInputHandler CreateInputHandler(ITextMessageSender textSender) => InputHandler.Object;
        public IAppLoop CreatePttLoop(
            IAudioService audioService,
            IPttController pttController,
            ITextMessageSender textSender,
            IInputHandler inputHandler) => PttLoop.Object;
    }

    #region Test 1: AppRunner_Constructs_WithValidDeps

    [Fact]
    public void AppRunner_Constructs_WithValidDeps()
    {
        var factory = new TestServiceFactory();
        var cfg = DefaultConfig;

        var runner = new AppRunner(cfg, factory);

        Assert.NotNull(runner);
    }

    #endregion

    #region Test 2: AppRunner_RunAsync_ThrowsOperationCanceledException_WhenCTIsCanceled

    [Fact]
    public async Task AppRunner_RunAsync_ThrowsOperationCanceledException_WhenCTIsCanceled()
    {
        var factory = new TestServiceFactory();
        factory.Gateway.Setup(x => x.ConnectAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var cfg = DefaultConfig;
        using var runner = new AppRunner(cfg, factory);

        var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel before RunAsync starts

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => runner.RunAsync(cts.Token));
    }

    #endregion

    #region Test 3: AppRunner_RunAsync_Returns0_OnNormalExit

    [Fact]
    public async Task AppRunner_RunAsync_Returns0_OnNormalExit()
    {
        var factory = new TestServiceFactory();

        factory.Gateway.Setup(x => x.ConnectAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        factory.PttLoop.Setup(x => x.RunAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(AppLoopExitCode.Ok);

        var cfg = DefaultConfig;
        using var runner = new AppRunner(cfg, factory);

        var result = await runner.RunAsync(CancellationToken.None);

        Assert.Equal(0, result);
    }

    #endregion

    #region Test 4: AppRunner_RunAsync_Returns100_OnRestart

    [Fact]
    public async Task AppRunner_RunAsync_Returns100_OnRestart()
    {
        var factory = new TestServiceFactory();

        factory.Gateway.Setup(x => x.ConnectAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // First call returns Restart, second call returns Ok — app should loop and return 0
        var callCount = 0;
        factory.PttLoop.Setup(x => x.RunAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount == 1 ? AppLoopExitCode.Restart : AppLoopExitCode.Ok;
            });

        var cfg = DefaultConfig;
        using var runner = new AppRunner(cfg, factory);

        var result = await runner.RunAsync(CancellationToken.None);

        // After one restart loop, returns 0 (because second RunAsync returns Ok)
        Assert.Equal(0, result);
        Assert.Equal(2, callCount);
    }

    #endregion

    #region Test 5: AppRunner_Disposes_OwnedResources

    [Fact]
    public async Task AppRunner_Disposes_OwnedResources()
    {
        var factory = new TestServiceFactory();

        factory.Gateway.Setup(x => x.ConnectAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        factory.PttLoop.Setup(x => x.RunAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(AppLoopExitCode.Ok);

        var cfg = DefaultConfig;
        using var runner = new AppRunner(cfg, factory);

        await runner.RunAsync(CancellationToken.None);

        factory.Gateway.Verify(x => x.Dispose(), Times.Once);
        factory.Audio.Verify(x => x.Dispose(), Times.Once);
        factory.PttLoop.Verify(x => x.Dispose(), Times.Once);
    }

    #endregion
}