using OpenClawPTT.Services;

namespace OpenClawPTT;

/// <summary>
/// Creates and wires all application services. Acts as a manual DI container.
/// </summary>
public interface IServiceFactory
{
    IGatewayService CreateGatewayService(AppConfig cfg);
    IAudioService CreateAudioService(AppConfig cfg);
    IPttController CreatePttController(AppConfig cfg, IAudioService audioService);
    IInputHandler CreateInputHandler(IGatewayService gateway, IAudioService audioService, ITextMessageSender textSender);
    ITextMessageSender CreateTextMessageSender(IGatewayService gateway);
    IPttLoop CreatePttLoop(
        AppConfig cfg,
        IGatewayService gateway,
        IAudioService audioService,
        IPttController pttController,
        ITextMessageSender textSender,
        IInputHandler inputHandler);
}
