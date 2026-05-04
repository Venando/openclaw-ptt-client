using OpenClawPTT.Services;

namespace OpenClawPTT;

/// <summary>
/// Creates and wires all application services. Acts as a manual DI container.
/// </summary>
public interface IServiceFactory
{
    /// <summary>
    /// Initialize the agent settings persistence with the settings service.
    /// Called by AppBootstrapper after loading the config.
    /// </summary>
    void InitializeAgentSettingsPersistence(AgentSettingsService agentSettingsService);

    /// <summary>
    /// Get the agent settings persistence instance.
    /// Throws if InitializeAgentSettingsPersistence has not been called yet.
    /// </summary>
    IAgentSettingsPersistence GetAgentSettingsPersistence();

    IGatewayService CreateGatewayService(AppConfig cfg);
    IAudioService CreateAudioService(AppConfig cfg);
    IPttController CreatePttController(AppConfig cfg, IAudioService audioService, IHotkeyHookFactory? hotkeyHookFactory = null);
    IInputHandler CreateInputHandler(ITextMessageSender textSender);
    ITextMessageSender CreateTextMessageSender(IGatewayService gateway);
    IDirectLlmService CreateDirectLlmService(AppConfig cfg);
    IStreamShellHost CreateStreamShellHost();
    IColorConsole CreateColorConsole();
    IAppLoop CreatePttLoop(
        IAudioService audioService,
        IPttController pttController,
        ITextMessageSender textSender,
        IInputHandler inputHandler,
        bool requireConfirmBeforeSend = false);
}
