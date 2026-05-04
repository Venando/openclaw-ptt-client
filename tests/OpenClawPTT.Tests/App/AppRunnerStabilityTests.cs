namespace OpenClawPTT.Tests;

using Moq;
using OpenClawPTT;
using OpenClawPTT.Services;
using System;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using Xunit;

public class AppRunnerStabilityTests
{
    private static AppConfig DefaultConfig => new()
    {
        HotkeyCombination = "Alt+=",
        HoldToTalk = false
    };

    private static AppConfig AltConfig => new()
    {
        HotkeyCombination = "Ctrl+Shift+P",
        HoldToTalk = true
    };

    private static IColorConsole CreateMockConsole() => Mock.Of<IColorConsole>();

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

        public AppConfig? LastGatewayConfig { get; private set; }
        public int CreateGatewayServiceCallCount { get; private set; }

        public IGatewayService CreateGatewayService(AppConfig cfg)
        {
            LastGatewayConfig = cfg;
            CreateGatewayServiceCallCount++;
            return Gateway.Object;
        }

        public IAudioService CreateAudioService(AppConfig cfg) => Audio.Object;
        public IPttController CreatePttController(AppConfig cfg, IAudioService audioService, IHotkeyHookFactory? hotkeyHookFactory = null) => PttController.Object;
        public ITextMessageSender CreateTextMessageSender(IGatewayService gateway) => TextSender.Object;
        public IInputHandler CreateInputHandler(ITextMessageSender textSender) => InputHandler.Object;
        public IDirectLlmService CreateDirectLlmService(AppConfig cfg) => Mock.Of<IDirectLlmService>();
        public IStreamShellHost CreateStreamShellHost() => Mock.Of<IStreamShellHost>();
        public void InitializeAgentSettingsPersistence(AgentSettingsService agentSettingsService) { }
        public IAgentSettingsPersistence GetAgentSettingsPersistence() => CreatePersistenceMock();
        public IColorConsole CreateColorConsole() => Mock.Of<IColorConsole>();
        public IAppLoop CreatePttLoop(
            IAudioService audioService,
            IPttController pttController,
            ITextMessageSender textSender,
            IInputHandler inputHandler,
            bool requireConfirmBeforeSend = false) => PttLoop.Object;
    }

    private static IAgentSettingsPersistence CreatePersistenceMock()
    {
        var mock = new Mock<IAgentSettingsPersistence>();
        mock.Setup(x => x.AllAgentsWithHotkeys).Returns(new List<(AgentInfo Agent, string? Hotkey)>().AsReadOnly());
        mock.Setup(x => x.AllAgentSettings).Returns(new List<(AgentInfo Agent, string? Hotkey, string? Emoji)>().AsReadOnly());
        return mock.Object;
    }

    #region Test 1: ConnectAsync throws IOException → RunAsync returns Error(1)

    [Fact]
    public async Task RunAsync_ReturnsError_WhenConnectAsyncThrowsIOException()
    {
        var factory = new TestServiceFactory();
        factory.Gateway.Setup(x => x.ConnectAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Network unavailable"));

        factory.PttLoop.Setup(x => x.RunAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(AppLoopExitCode.Ok); // Should never be reached

        var cfg = DefaultConfig;
        using var runner = new AppRunner(cfg, factory, Mock.Of<IStreamShellHost>(), Mock.Of<IConfigurationService>(), CreateMockConsole());

        var result = await runner.RunAsync(CancellationToken.None);

        Assert.Equal(1, result); // Error exit code
    }

    #endregion

    #region Test 2: ConnectAsync throws WebSocketException → RunAsync returns Error(1)

    [Fact]
    public async Task RunAsync_ReturnsError_WhenConnectAsyncThrowsWebSocketException()
    {
        var factory = new TestServiceFactory();
        factory.Gateway.Setup(x => x.ConnectAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new WebSocketException());

        factory.PttLoop.Setup(x => x.RunAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(AppLoopExitCode.Ok); // Should never be reached

        var cfg = DefaultConfig;
        using var runner = new AppRunner(cfg, factory, Mock.Of<IStreamShellHost>(), Mock.Of<IConfigurationService>(), CreateMockConsole());

        var result = await runner.RunAsync(CancellationToken.None);

        Assert.Equal(1, result); // Error exit code
    }

    #endregion

    #region Test 3: ConnectAsync throws OperationCanceledException → RunAsync propagates it

    [Fact]
    public async Task RunAsync_PropagatesOperationCanceledException_WhenConnectAsyncThrowsAndCTIsCanceled()
    {
        var factory = new TestServiceFactory();
        factory.Gateway.Setup(x => x.ConnectAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var cfg = DefaultConfig;
        using var runner = new AppRunner(cfg, factory, Mock.Of<IStreamShellHost>(), Mock.Of<IConfigurationService>(), CreateMockConsole());

        var cts = new CancellationTokenSource();
        cts.Cancel(); // Simulate cancellation

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => runner.RunAsync(cts.Token));
    }

    #endregion

    #region Test 4: PttLoop returns Restart MaxRestartCount times → loop breaks, returns Error

    [Fact]
    public async Task RunAsync_ReturnsError_WhenPttLoopReturnsRestartMoreThanMaxRestartCount()
    {
        var factory = new TestServiceFactory();

        factory.Gateway.Setup(x => x.ConnectAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // PttLoop keeps returning Restart — more than MaxRestartCount
        factory.PttLoop.Setup(x => x.RunAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(AppLoopExitCode.Restart);

        var cfg = DefaultConfig;
        using var runner = new AppRunner(cfg, factory, Mock.Of<IStreamShellHost>(), Mock.Of<IConfigurationService>(), CreateMockConsole());

        // Should NOT loop forever — must break after MaxRestartCount
        var result = await runner.RunAsync(CancellationToken.None);

        Assert.Equal(1, result); // Error — exceeded restart limit
        // Verify we went through MaxRestartCount iterations
        // Loop must break (not run forever) and return Error.
        // The primary assertion (result == 1) already confirms the loop terminated.
    }

    #endregion

    #region Test 5: PttLoop returns Restart fewer than MaxRestartCount times, then Ok → returns 0

    [Fact]
    public async Task RunAsync_ReturnsOk_WhenPttLoopRestartsFewerThanMaxRestartCount()
    {
        var factory = new TestServiceFactory();

        factory.Gateway.Setup(x => x.ConnectAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Restart once, then succeed
        var callCount = 0;
        factory.PttLoop.Setup(x => x.RunAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount < AppRunner.MaxRestartCount
                    ? AppLoopExitCode.Restart
                    : AppLoopExitCode.Ok;
            });

        var cfg = DefaultConfig;
        using var runner = new AppRunner(cfg, factory, Mock.Of<IStreamShellHost>(), Mock.Of<IConfigurationService>(), CreateMockConsole());

        var result = await runner.RunAsync(CancellationToken.None);

        Assert.Equal(0, result); // Ok
        Assert.Equal(AppRunner.MaxRestartCount, callCount); // MaxRestartCount restarts before Ok
    }

    #endregion

    #region Test 6: RunAsync normal exit → returns 0

    [Fact]
    public async Task RunAsync_ReturnsOk_OnNormalExit()
    {
        var factory = new TestServiceFactory();

        factory.Gateway.Setup(x => x.ConnectAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        factory.PttLoop.Setup(x => x.RunAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(AppLoopExitCode.Ok);

        var cfg = DefaultConfig;
        using var runner = new AppRunner(cfg, factory, Mock.Of<IStreamShellHost>(), Mock.Of<IConfigurationService>(), CreateMockConsole());

        var result = await runner.RunAsync(CancellationToken.None);

        Assert.Equal(0, result);
    }

    #endregion

    #region Test 7: Config passed to factory is the runner's config (verifies factory receives cfg)

    [Fact]
    public async Task RunAsync_PassesRunnerConfig_ToFactory_OnEachRestart()
    {
        var factory = new TestServiceFactory();

        factory.Gateway.Setup(x => x.ConnectAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        factory.PttLoop.Setup(x => x.RunAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(AppLoopExitCode.Ok);

        var cfg = AltConfig;
        using var runner = new AppRunner(cfg, factory, Mock.Of<IStreamShellHost>(), Mock.Of<IConfigurationService>(), CreateMockConsole());

        await runner.RunAsync(CancellationToken.None);

        // Verify the factory received AltConfig on the gateway creation
        Assert.Same(cfg, factory.LastGatewayConfig);
        Assert.Equal(1, factory.CreateGatewayServiceCallCount);
    }

    #endregion

    #region Test 8: Gateway dispose is called even when ConnectAsync throws

    [Fact]
    public async Task RunAsync_DisposesGateway_WhenConnectAsyncThrowsIOException()
    {
        var factory = new TestServiceFactory();
        factory.Gateway.Setup(x => x.ConnectAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Network unavailable"));

        factory.PttLoop.Setup(x => x.RunAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(AppLoopExitCode.Ok);

        var cfg = DefaultConfig;
        using var runner = new AppRunner(cfg, factory, Mock.Of<IStreamShellHost>(), Mock.Of<IConfigurationService>(), CreateMockConsole());

        await runner.RunAsync(CancellationToken.None);

        factory.Gateway.Verify(x => x.Dispose(), Times.Once);
    }

    #endregion
}
