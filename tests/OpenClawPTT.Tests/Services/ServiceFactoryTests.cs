namespace OpenClawPTT.Tests;

using Moq;
using OpenClawPTT;
using OpenClawPTT.Services;
using System;
using Xunit;

public class ServiceFactoryTests
{
    private static AppConfig DefaultConfig => new()
    {
        HotkeyCombination = "Alt+=",
        HoldToTalk = false
    };

    private static AppConfig DefaultConfigWithAudio => new()
    {
        HotkeyCombination = "Alt+=",
        HoldToTalk = false,
        AudioResponseMode = "both"
    };

    private static ServiceFactory CreateFactory()
    {
        var configService = new ConfigurationService();
        return new ServiceFactory(configService, new MessageComposer(), new StreamShellHost());
    }

    /// <summary>
    /// Minimal test-double for IGlobalHotkeyHook that does nothing and never touches /dev/input.
    /// </summary>
    private sealed class NoOpHotkeyHook : IGlobalHotkeyHook
    {
        public event Action? HotkeyPressed { add { } remove { } }
        public event Action? HotkeyReleased { add { } remove { } }
        public void SetHotkey(Hotkey mapping) { }
        public void Start() { }
        public void Dispose() { }
    }

    /// <summary>
    /// Test-double IHotkeyHookFactory that returns a NoOpHotkeyHook,
    /// preventing real /dev/input access in tests.
    /// </summary>
    private sealed class NoOpHotkeyHookFactory : IHotkeyHookFactory
    {
        public IGlobalHotkeyHook Create(Hotkey mapping) => new NoOpHotkeyHook();
    }

    #region Test 1: CreatePttController_Returns_IPttController

    [Fact]
    public void CreatePttController_Returns_IPttController()
    {
        var factory = CreateFactory();
        var cfg = DefaultConfigWithAudio;
        var mockAudio = new Mock<IAudioService>();
        var noOpFactory = new NoOpHotkeyHookFactory();

        var controller = factory.CreatePttController(cfg, mockAudio.Object, noOpFactory);

        Assert.NotNull(controller);
        Assert.IsAssignableFrom<IPttController>(controller);

        controller.Dispose();
    }

    #endregion

    #region Test 2: CreatePttLoop_Returns_IPttLoop

    [Fact]
    public void CreatePttLoop_Returns_IPttLoop()
    {
        var factory = CreateFactory();

        var mockAudio = new Mock<IAudioService>();
        var mockPttController = new Mock<IPttController>();
        var mockTextSender = new Mock<ITextMessageSender>();
        var mockInputHandler = new Mock<IInputHandler>();

        var loop = factory.CreatePttLoop(
            mockAudio.Object,
            mockPttController.Object,
            mockTextSender.Object,
            mockInputHandler.Object);

        Assert.NotNull(loop);
        Assert.IsAssignableFrom<IAppLoop>(loop);

        loop.Dispose();
    }

    #endregion

    #region Test 3: CreatePttController_ThenDispose_NoDoubleDisposeCrash

    [Fact]
    public void CreatePttController_ThenDispose_NoDoubleDisposeCrash()
    {
        var factory = CreateFactory();
        var cfg = DefaultConfigWithAudio;
        var mockAudio = new Mock<IAudioService>();
        var noOpFactory = new NoOpHotkeyHookFactory();

        var controller = factory.CreatePttController(cfg, mockAudio.Object, noOpFactory);

        controller.Dispose(); // First dispose — should succeed

        // Second dispose — should not throw
        var ex = Record.Exception(() => controller.Dispose());
        Assert.Null(ex);
    }

    #endregion

    #region Test 4: CreateAudioService_Returns_IAudioService

    [Fact]
    public void CreateAudioService_Returns_IAudioService()
    {
        var factory = CreateFactory();
        var cfg = DefaultConfigWithAudio;

        var audio = factory.CreateAudioService(cfg);

        Assert.NotNull(audio);
        Assert.IsType<AudioService>(audio);

        audio.Dispose();
    }

    #endregion

    #region Test 5: CreateGatewayService_Returns_IGatewayService

    [Fact]
    public void CreateGatewayService_Returns_IGatewayService()
    {
        var factory = CreateFactory();
        var cfg = DefaultConfig;

        var gateway = factory.CreateGatewayService(cfg);

        Assert.NotNull(gateway);
        Assert.IsType<GatewayService>(gateway);

        gateway.Dispose();
    }

    #endregion

    #region Test 6: ServiceFactory_WithDefaultConfig_DoesNotThrow

    [Fact]
    public void ServiceFactory_WithDefaultConfig_DoesNotThrow()
    {
        var cfg = new AppConfig(); // all defaults

        var ex = Record.Exception(() =>
        {
            var factory = CreateFactory();
            using var gateway = factory.CreateGatewayService(cfg);
            using var audio = factory.CreateAudioService(cfg);
        });

        Assert.Null(ex);
    }

    #endregion
}
