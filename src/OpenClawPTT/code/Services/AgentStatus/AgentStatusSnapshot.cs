using System.Text.Json;

namespace OpenClawPTT.Services;

/// <summary>
/// Immutable snapshot of an agent or subagent's status.
/// Stores all extractable data from gateway payloads for future use.
/// </summary>
public sealed record AgentStatusSnapshot
{
    public string SessionKey { get; init; } = string.Empty;
    public string? ParentSessionKey { get; init; }
    public string? DisplayName { get; init; }
    public string? Status { get; init; }
    public string? StopReason { get; init; }
    public string? Model { get; init; }
    public string? ModelProvider { get; init; }
    public long? InputTokens { get; init; }
    public long? OutputTokens { get; init; }
    public long? TotalTokens { get; init; }
    public long? StartedAt { get; init; }
    public long? EndedAt { get; init; }
    public long? RuntimeMs { get; init; }
    public string? SubagentRunState { get; init; }
    public bool? HasActiveSubagentRun { get; init; }
    public IReadOnlyList<string> ChildSessions { get; init; } = Array.Empty<string>();
    public string? SubagentRole { get; init; }
    public int? SpawnDepth { get; init; }
    public long? ContextTokens { get; init; }
    public string? LastChannel { get; init; }
    public bool? AbortedLastRun { get; init; }
    public long? UpdatedAt { get; init; }

    /// <summary>
    /// True if this snapshot represents a subagent.
    /// Detects via explicit parentSessionKey OR the sessionKey pattern (agent:*:subagent:*).
    /// </summary>
    public bool IsSubagent => !string.IsNullOrEmpty(ParentSessionKey)
        || SessionKey.Contains(":subagent:", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True if the agent appears to be actively running.
    /// </summary>
    public bool IsRunning =>
        Status?.Equals("running", StringComparison.OrdinalIgnoreCase) == true
        || (IsSubagent && HasActiveSubagentRun == true);

    /// <summary>
    /// True if the agent has finished its current task.
    /// </summary>
    public bool IsFinished =>
        StopReason is "stop" or "aborted"
        || (IsSubagent && SubagentRunState == "historical" && HasActiveSubagentRun != true);

    /// <summary>
    /// Returns a status emoji based on the agent/subagent state.
    /// </summary>
    public string GetStatusEmoji()
    {
        // Subagent states
        if (IsSubagent)
        {
            if (StopReason == "toolUse") return "🛠️";
            if (StopReason == "aborted") return "🔴";
            if (SubagentRunState == "historical" || IsFinished) return "✅";
            if (HasActiveSubagentRun == true || Status == "running") return "🟢";
            return "⏳";
        }

        // Main agent states
        if (StopReason == "toolUse") return "🛠️";
        if (StopReason == "aborted") return "🔴";
        if (Status == "running") return "🟢";
        if (Status == "done") return "⏳";
        return "⚪";
    }
}
