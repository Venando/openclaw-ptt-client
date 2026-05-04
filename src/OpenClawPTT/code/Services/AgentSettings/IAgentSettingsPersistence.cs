using System;
using System.Collections.Generic;

namespace OpenClawPTT;

/// <summary>
/// Interface for managing per-agent persisted settings (hotkey, emoji) persisted to agents.json.
/// </summary>
public interface IAgentSettingsPersistence
{
    /// <summary>Set or clear per-agent hotkey override. Fires PersistedSettingsChanged.</summary>
    void SetPersistedHotkey(string agentId, string? hotkeyCombo);

    /// <summary>Get per-agent hotkey override, or null for global default.</summary>
    string? GetPersistedHotkey(string agentId);

    /// <summary>Set or clear per-agent emoji override. Fires PersistedSettingsChanged.</summary>
    void SetPersistedEmoji(string agentId, string? emoji);

    /// <summary>Get per-agent emoji override, or null for default (🤖).</summary>
    string? GetPersistedEmoji(string agentId);

    /// <summary>All agents with their effective hotkey (override or null).</summary>
    IReadOnlyList<(AgentInfo Agent, string? Hotkey)> AllAgentsWithHotkeys { get; }

    /// <summary>All agents with their effective hotkey and emoji.</summary>
    IReadOnlyList<(AgentInfo Agent, string? Hotkey, string? Emoji)> AllAgentSettings { get; }

    /// <summary>Merge persisted settings from agents.json into the registry.</summary>
    void MergePersistedSettings(AgentsConfig persisted);

    /// <summary>Fired when persisted settings change via SetPersistedHotkey/SetPersistedEmoji.</summary>
    event Action? PersistedSettingsChanged;
}
