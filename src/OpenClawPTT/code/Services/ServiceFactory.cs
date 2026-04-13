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

    public ServiceFactory(IConfigurationService configService)
        : this(configService, new ConsoleUiOutput(), new MessageComposer())
    {
    }

    public ServiceFactory(IConfigurationService configService, IConsoleOutput console, IMessageComposer composer)
    {
        _configService = configService;
        _console = console;
        _composer = composer;
    }

    public IGatewayService CreateGatewayService(AppConfig cfg) => new GatewayService(cfg);

    public IAudioService CreateAudioService(AppConfig cfg) => new AudioService(cfg);

    public IPttController CreatePttController(AppConfig cfg, IAudioService audioService, IHotkeyHookFactory? hotkeyHookFactory = null)
    {
        var controller = new PttController(hotkeyHookFactory ?? new HotkeyHookFactory());
        controller.SetHotkey(cfg.HotkeyCombination, cfg.HoldToTalk);
        return controller;
    }

    public IInputHandler CreateInputHandler(ITextMessageSender textSender)
        => new InputHandler(textSender, _configService, _console);

    public ITextMessageSender CreateTextMessageSender(IGatewayService gateway)
        => new TextMessageSender(gateway, _configService, _console, _composer);

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
