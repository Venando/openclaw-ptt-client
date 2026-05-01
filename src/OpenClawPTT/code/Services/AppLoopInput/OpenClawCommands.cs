using System.Collections.Generic;

namespace OpenClawPTT;

/// <summary>
/// Known OpenClaw tool commands that can be sent as text messages.
/// The gateway interprets these as tool calls.
/// </summary>
public static class OpenClawCommands
{
    /// <summary>All known OpenClaw command names (without leading /).</summary>
    public static readonly IReadOnlySet<string> Names = new HashSet<string>
    {
        "read", "write", "edit", "exec", "process",
        "sessions_list", "sessions_spawn", "sessions_history", "sessions_send",
        "session_status", "subagents",
        "web_search", "web_fetch",
        "memory_search", "memory_get",
        "image_generate",
    };
}
