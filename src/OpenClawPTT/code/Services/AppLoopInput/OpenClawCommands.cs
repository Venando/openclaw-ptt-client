using System.Collections.Generic;

namespace OpenClawPTT;

/// <summary>
/// Known OpenClaw slash commands that can be sent as text messages.
/// The gateway interprets these to control AI sessions, models, and tools.
/// </summary>
public static class OpenClawCommands
{
    /// <summary>All known OpenClaw slash command names (without leading /).</summary>
    public static readonly IReadOnlySet<string> Names = new HashSet<string>
    {
        // Session management
        "new", "reset", "compact", "btw",
        // Model & reasoning
        "model", "models", "reason", "reasoning", "think", "fast",
        // Diagnostics & settings
        "status", "whoami", "id", "usage", "trace", "verbose",
        // Interaction & tools
        "skill", "tasks", "allowlist", "pair", "voice",
    };
}
