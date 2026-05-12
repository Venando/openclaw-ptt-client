namespace OpenClawPTT.Services;

/// <summary>
/// Tracks application component status (gateway, TTS, direct LLM) and reflects it
/// in the StreamShell top separator.
/// </summary>
public interface IStatusService
{
    /// <summary>Update the gateway connection status displayed on the separator bar.</summary>
    void SetGatewayStatus(string label, StatusColor color);

    /// <summary>Update the TTS service status displayed on the separator bar.</summary>
    void SetTtsStatus(string label, StatusColor color);

    /// <summary>Update the direct LLM status displayed on the separator bar.</summary>
    void SetDirectLlmStatus(string label, StatusColor color);

    /// <summary>
    /// Record the last time the direct LLM was called.
    /// Pass null to clear the timestamp.
    /// </summary>
    void SetDirectLlmLastCalled(DateTime? timestamp);

    /// <summary>
    /// Provide an <see cref="IAgentStatusTracker"/> for rendering active agent
    /// status on the left side of the top separator. Safe to call after construction.
    /// </summary>
    void SetAgentStatusTracker(IAgentStatusTracker tracker);

    /// <summary>
    /// Sets the conversation name displayed in the top separator.
    /// Pass null to clear the conversation name.
    /// </summary>
    void SetConversationName(string? name);
}
