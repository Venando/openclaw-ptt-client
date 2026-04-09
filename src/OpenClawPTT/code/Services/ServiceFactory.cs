using OpenClawPTT.Services;

namespace OpenClawPTT;

/// <summary>
/// Default implementation of IServiceFactory.
/// </summary>
public sealed class ServiceFactory : IServiceFactory
{
    private readonly IConfigurationService _configService;
    private readonly IConsoleOutput _console;

    public ServiceFactory(IConfigurationService configService, IConsoleOutput console)
    {
        _configService = configService;
        _console = console;
    }

    public GatewayService CreateGatewayService(AppConfig cfg) => new(cfg);

    public AudioService CreateAudioService(AppConfig cfg) => new(cfg);

    public IPttController CreatePttController(AppConfig cfg, IAudioService audioService)
    {
        var controller = new PttController(cfg, audioService);
        controller.SetHotkey(cfg.HotkeyCombination, cfg.HoldToTalk);
        controller.Start();
        return controller;
    }

    public IInputHandler CreateInputHandler(IGatewayService gateway, IAudioService audioService, TextMessageSender textSender)
        => new InputHandler(textSender, _configService, _console);

    public TextMessageSender CreateTextMessageSender(IGatewayService gateway)
        => new(gateway, _configService, _console);

    public PttLoop CreatePttLoop(
        AppConfig cfg,
        GatewayService gateway,
        AudioService audioService,
        IPttController pttController,
        TextMessageSender textSender,
        IInputHandler inputHandler)
    {
        var stateMachine = new PttStateMachine();
        return new PttLoop(stateMachine, audioService, textSender, _console, inputHandler, pttController, cfg);
    }
}
