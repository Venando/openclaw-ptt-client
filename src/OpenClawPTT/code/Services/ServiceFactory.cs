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
        var controller = new PttController(cfg, audioService, hotkeyHookFactory ?? new HotkeyHookFactory());
        controller.SetHotkey(cfg.HotkeyCombination, cfg.HoldToTalk);
        controller.Start();
        return controller;
    }

    public IInputHandler CreateInputHandler(IGatewayService gateway, IAudioService audioService, ITextMessageSender textSender)
        => new InputHandler(textSender, _configService, _console);

    public ITextMessageSender CreateTextMessageSender(IGatewayService gateway)
        => new TextMessageSender(gateway, _configService, _console, _composer);

    public IPttLoop CreatePttLoop(
        AppConfig cfg,
        IGatewayService gateway,
        IAudioService audioService,
        IPttController pttController,
        ITextMessageSender textSender,
        IInputHandler inputHandler)
    {
        var stateMachine = new PttStateMachine();
        return new PttLoop(stateMachine, audioService, textSender, _console, inputHandler, pttController, cfg);
    }
}
