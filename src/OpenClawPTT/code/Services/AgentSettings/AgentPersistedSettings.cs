namespace OpenClawPTT;

/// <summary>Per-agent settings persisted to agents.json.</summary>
public sealed class AgentPersistedSettings
{
    public string AgentId { get; set; } = "";
    /// <summary>Hotkey override, e.g. "Alt+1". Null means "use global from config.json".</summary>
    public string? HotkeyCombination { get; set; }
    /// <summary>Emoji override for agent prefix and listings. Null means use default (🤖).</summary>
    public string? Emoji { get; set; }
}
