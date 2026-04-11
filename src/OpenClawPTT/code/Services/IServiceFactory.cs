using OpenClawPTT.Services;

namespace OpenClawPTT;

/// <summary>
/// Creates and wires all application services. Acts as a manual DI container.
/// </summary>
public interface IServiceFactory
{
    GatewayService CreateGatewayService(AppConfig cfg);
    AudioService CreateAudioService(AppConfig cfg);
    IPttController CreatePttController(AppConfig cfg, IAudioService audioService);
    IInputHandler CreateInputHandler(IGatewayService gateway, IAudioService audioService, ITextMessageSender textSender);
    ITextMessageSender CreateTextMessageSender(IGatewayService gateway);
    PttLoop CreatePttLoop(
        AppConfig cfg,
        GatewayService gateway,
        AudioService audioService,
        IPttController pttController,
        ITextMessageSender textSender,
        IInputHandler inputHandler);
}
