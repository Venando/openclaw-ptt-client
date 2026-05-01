using System.Collections.Generic;

namespace OpenClawPTT;

/// <summary>
/// Known OpenClaw slash commands that can be sent as text messages.
/// The gateway interprets these to control AI sessions, models, and tools.
/// </summary>
public static class OpenClawCommands
{
    /// <summary>All known OpenClaw slash command names (without leading /).</summary>
    /// <remarks>Source: https://github.com/openclaw/openclaw/blob/main/docs/tools/slash-commands.md</remarks>
    public static readonly IReadOnlySet<string> Names = new HashSet<string>
    {
        // Session management
        "new", "reset", "compact", "stop", "session",
        "export-session", "export", "export-trajectory", "trajectory",
        // Model & reasoning
        "model", "models", "think", "thinking", "reason", "reasoning",
        "verbose", "v", "trace", "fast", "elevated", "elev", "exec", "queue",
        // Diagnostics & info
        "status", "diagnostics", "help", "commands", "tools",
        "whoami", "id", "usage", "context", "tasks", "crestodian",
        // Interaction
        "skill", "allowlist", "approve", "btw",
        // Sub-agents
        "subagents", "acp",
    };
}
