using System;

namespace OpenClawPTT;

/// <summary>
/// A single message entry from a session's chat history.
/// </summary>
public class ChatHistoryEntry
{
    public string Role { get; init; } = "";      // "user" or "assistant"
    public string Content { get; init; } = "";
    public DateTime? CreatedAt { get; init; }
}
