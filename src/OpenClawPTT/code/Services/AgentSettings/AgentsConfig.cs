using System.Collections.Generic;

namespace OpenClawPTT;

/// <summary>Top-level container for agents.json.</summary>
public sealed class AgentsConfig
{
    public List<AgentPersistedSettings> Agents { get; set; } = new();
}
