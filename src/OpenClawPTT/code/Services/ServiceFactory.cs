using OpenClawPTT.Services;

namespace OpenClawPTT;

/// <summary>
/// Default implementation of IServiceFactory.
/// </summary>
public sealed class ServiceFactory : IServiceFactory
{
    private readonly IConfigurationService _configService;
    private readonly IStreamShellHost _shellHost;
    private readonly IColorConsole _colorConsole;

    public ServiceFactory(IConfigurationService configService, IStreamShellHost shellHost)
    {
        _configService = configService;
        _shellHost = shellHost;
        _colorConsole = new ColorConsole(shellHost);
        
        // Initialize the legacy ConsoleUi facade for backward compatibility
        ConsoleUi.Initialize(_colorConsole);
    }

    public IGatewayService CreateGatewayService(AppConfig cfg) => new GatewayService(cfg, _colorConsole);

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
        => new TextMessageSender(gateway, _colorConsole);

    public IDirectLlmService CreateDirectLlmService(AppConfig cfg)
        => new DirectLlmService(cfg);

    public IStreamShellHost CreateStreamShellHost() => _shellHost;

    public IColorConsole CreateColorConsole() => _colorConsole;

    public IAppLoop CreatePttLoop(
        IAudioService audioService,
        IPttController pttController,
        ITextMessageSender textSender,
        IInputHandler inputHandler,
        bool requireConfirmBeforeSend = false)
    {
        var stateMachine = new PttStateMachine();
        return new AppLoop(stateMachine, audioService, textSender, inputHandler, pttController, _colorConsole,
            requireConfirmBeforeSend);
    }
}
