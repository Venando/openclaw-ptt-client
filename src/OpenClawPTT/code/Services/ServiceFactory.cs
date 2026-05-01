using OpenClawPTT.Services;

namespace OpenClawPTT;

/// <summary>
/// Default implementation of IServiceFactory.
/// </summary>
public sealed class ServiceFactory : IServiceFactory
{
    private readonly IConfigurationService _configService;
    private readonly IConsoleOutput _console;
    private readonly IMessageComposer _composer;
    private readonly IStreamShellHost _shellHost;

    public ServiceFactory(IConfigurationService configService)
        : this(configService, new StreamShellConsoleOutput(new StreamShellHost()), new MessageComposer(), new StreamShellHost())
    {
    }

    public ServiceFactory(IConfigurationService configService, IConsoleOutput console, IMessageComposer composer, IStreamShellHost shellHost)
    {
        _configService = configService;
        _console = console;
        _composer = composer;
        _shellHost = shellHost;
    }

    public IGatewayService CreateGatewayService(AppConfig cfg) => new GatewayService(cfg, _console);

    public IAudioService CreateAudioService(AppConfig cfg) => new AudioService(cfg);

    public IPttController CreatePttController(AppConfig cfg, IAudioService audioService, IHotkeyHookFactory? hotkeyHookFactory = null)
    {
        var controller = new PttController(hotkeyHookFactory ?? new HotkeyHookFactory());
        controller.SetHotkey(cfg.HotkeyCombination, cfg.HoldToTalk);
        return controller;
    }

    public IInputHandler CreateInputHandler(ITextMessageSender textSender)
        => new InputHandler(textSender);

    public ITextMessageSender CreateTextMessageSender(IGatewayService gateway)
        => new TextMessageSender(gateway, _configService, _console, _composer);

    public IStreamShellHost CreateStreamShellHost() => _shellHost;

    public IAppLoop CreatePttLoop(
        IAudioService audioService,
        IPttController pttController,
        ITextMessageSender textSender,
        IInputHandler inputHandler)
    {
        var stateMachine = new PttStateMachine();
        return new AppLoop(stateMachine, audioService, textSender, inputHandler, pttController);
    }
}
