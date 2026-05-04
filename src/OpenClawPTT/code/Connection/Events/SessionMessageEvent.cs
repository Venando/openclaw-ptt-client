using System.Text.Json;

namespace OpenClawPTT;

/// <summary>
/// Event raised when a session-related message arrives (session.message, agent, chat events).
/// Contains the original gateway event name and the full payload element.
/// </summary>
public record SessionMessageEvent(string EventName, JsonElement Payload);
