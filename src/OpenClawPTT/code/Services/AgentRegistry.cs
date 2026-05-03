using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenClawPTT;

/// <summary>
/// Fallback lock-free helpers for AgentRegistry.
/// </summary>
internal static class AgentRegistryHelpers
{
    /// <summary>Returns the first agent matching the given session key, or null.</summary>
    public static AgentInfo? FindBySessionKey(List<AgentInfo> agents, string? sessionKey)
    {
        return sessionKey != null
            ? agents.FirstOrDefault(a => a.SessionKey == sessionKey)
            : null;
    }

    /// <summary>Returns the default agent, or first available, or null.</summary>
    public static AgentInfo? GetDefaultOrFirst(List<AgentInfo> agents)
    {
        return agents.FirstOrDefault(a => a.IsDefault) ?? agents.FirstOrDefault();
    }

    /// <summary>Returns the active agent info, or null.</summary>
    public static AgentInfo? GetActiveAgent(List<AgentInfo> agents, string? activeSessionKey)
    {
        return FindBySessionKey(agents, activeSessionKey);
    }
}

/// <summary>
/// Thread-safe registry of available agents and the currently active session key.
/// Accessible app-wide as a singleton. Concerns: agent list management and
/// active session tracking. Persisted settings (hotkey, emoji) are managed
/// by <see cref="AgentSettingsPersistence"/>.
/// </summary>
public static partial class AgentRegistry
{
    private static readonly object _lock = new();

    // ── Agent list ──
    private static List<AgentInfo> _agents = new();

    // ── Active session ──
    private static string? _activeSessionKey;

    /// <summary>Event raised when the active session changes.</summary>
    public static event Action<string?>? ActiveSessionChanged;

    /// <summary>Replaces the entire agent list. Resets active session if no longer valid.</summary>
    public static void SetAgents(IReadOnlyList<AgentInfo> agents)
    {
        lock (_lock)
        {
            _agents = agents.ToList();

            // Validate current active session
            if (_activeSessionKey != null && _agents.All(a => a.SessionKey != _activeSessionKey))
                _activeSessionKey = null;

            // Pick default or first if none active
            if (_activeSessionKey == null)
            {
                var defaultAgent = AgentRegistryHelpers.GetDefaultOrFirst(_agents);
                _activeSessionKey = defaultAgent?.SessionKey;
            }
        }
    }

    /// <summary>All available agents.</summary>
    public static IReadOnlyList<AgentInfo> Agents
    {
        get
        {
            lock (_lock) return _agents.ToList().AsReadOnly();
        }
    }

    /// <summary>Currently active session key. Messages from other sessions are filtered out.</summary>
    public static string? ActiveSessionKey
    {
        get { lock (_lock) return _activeSessionKey; }
    }

    /// <summary>Name of the currently active agent, or null if none.</summary>
    public static string? ActiveAgentName
    {
        get
        {
            lock (_lock)
            {
                var active = AgentRegistryHelpers.FindBySessionKey(_agents, _activeSessionKey);
                return active?.Name;
            }
        }
    }

    /// <summary>AgentId of the currently active agent, or null.</summary>
    public static string? ActiveAgentId
    {
        get
        {
            lock (_lock)
            {
                var active = AgentRegistryHelpers.FindBySessionKey(_agents, _activeSessionKey);
                return active?.AgentId;
            }
        }
    }

    /// <summary>True if a valid agent is currently active.</summary>
    public static bool IsActiveAgentAvailable
    {
        get
        {
            lock (_lock)
            {
                return AgentRegistryHelpers.FindBySessionKey(_agents, _activeSessionKey) != null;
            }
        }
    }

    /// <summary>Switch active agent by agentId.</summary>
    public static bool SetActiveAgent(string agentId)
    {
        lock (_lock)
        {
            var agent = _agents.FirstOrDefault(a =>
                a.AgentId.Equals(agentId, StringComparison.OrdinalIgnoreCase));
            if (agent == null) return false;

            if (_activeSessionKey != agent.SessionKey)
            {
                _activeSessionKey = agent.SessionKey;
                ActiveSessionChanged?.Invoke(_activeSessionKey);
            }
            return true;
        }
    }

    /// <summary>Switch active agent by session key.</summary>
    public static bool SetActiveSession(string sessionKey)
    {
        lock (_lock)
        {
            if (_agents.All(a => a.SessionKey != sessionKey))
                return false;

            if (_activeSessionKey != sessionKey)
            {
                _activeSessionKey = sessionKey;
                ActiveSessionChanged?.Invoke(_activeSessionKey);
            }
            return true;
        }
    }

    /// <summary>
    /// Returns true if the message's sessionKey matches the currently active session.
    /// If no active session is set, all messages pass through.
    /// </summary>
    public static bool IsMessageForActiveSession(string? sessionKey)
    {
        if (sessionKey == null) return true;
        lock (_lock) return _activeSessionKey == null || sessionKey == _activeSessionKey;
    }

    /// <summary>Gets the active agent's display name and emoji.</summary>
    public static void GetActiveNameAndEmoji(out object agentName, out object emoji, string defaultName = "Agent", string defaultEmoji = "🤖")
    {
        lock (_lock)
        {
            var active = AgentRegistryHelpers.FindBySessionKey(_agents, _activeSessionKey);
            agentName = active?.Name ?? defaultName;
            emoji = active != null
                ? AgentSettingsPersistence.GetPersistedEmoji(active.AgentId) ?? defaultEmoji
                : defaultEmoji;
        }
    }
}

/// <summary>
/// Manages per-agent persisted settings (hotkey, emoji) persisted to agents.json.
/// This responsibility was extracted from AgentRegistry to honor Single Responsibility.
/// </summary>
public static class AgentSettingsPersistence
{
    private static readonly object _lock = new();
    private static Dictionary<string, AgentPersistedSettings> _agentSettings = new(StringComparer.OrdinalIgnoreCase);
    private static AgentSettingsService? _settingsService;

    /// <summary>Register the settings service so SetPersistedHotkey auto-saves.</summary>
    public static void RegisterSettingsService(AgentSettingsService service)
    {
        lock (_lock) { _settingsService = service; }
    }

    /// <summary>Get per-agent hotkey override, or null for global default.</summary>
    public static string? GetPersistedHotkey(string agentId)
    {
        lock (_lock)
        {
            return _agentSettings.TryGetValue(agentId, out var s) ? s.HotkeyCombination : null;
        }
    }

    /// <summary>Set or clear per-agent hotkey override. Fires PersistedSettingsChanged.</summary>
    public static void SetPersistedHotkey(string agentId, string? hotkeyCombo)
    {
        var existing = GetPersistedEmoji(agentId);
        SetPersistedField(agentId, hotkeyCombo, existing);
    }

    /// <summary>Get per-agent emoji override, or null for default (🤖).</summary>
    public static string? GetPersistedEmoji(string agentId)
    {
        lock (_lock)
        {
            return _agentSettings.TryGetValue(agentId, out var s) ? s.Emoji : null;
        }
    }

    /// <summary>Set or clear per-agent emoji override. Fires PersistedSettingsChanged.</summary>
    public static void SetPersistedEmoji(string agentId, string? emoji)
    {
        var existing = GetPersistedHotkey(agentId);
        SetPersistedField(agentId, existing, emoji);
    }

    /// <summary>All agents with their effective hotkey (override or null).</summary>
    public static IReadOnlyList<(AgentInfo Agent, string? Hotkey)> AllAgentsWithHotkeys
    {
        get
        {
            lock (_lock)
            {
                return AgentRegistry.Agents.Select(a =>
                    (a, _agentSettings.TryGetValue(a.AgentId, out var s) ? s.HotkeyCombination : (string?)null)
                ).ToList().AsReadOnly();
            }
        }
    }

    /// <summary>All agents with their effective hotkey and emoji.</summary>
    public static IReadOnlyList<(AgentInfo Agent, string? Hotkey, string? Emoji)> AllAgentSettings
    {
        get
        {
            lock (_lock)
            {
                return AgentRegistry.Agents.Select(a =>
                {
                    _agentSettings.TryGetValue(a.AgentId, out var s);
                    return (a, s?.HotkeyCombination, s?.Emoji);
                }).ToList().AsReadOnly();
            }
        }
    }

    /// <summary>Merge persisted settings from agents.json into the registry.</summary>
    public static void MergePersistedSettings(AgentsConfig persisted)
    {
        lock (_lock)
        {
            foreach (var s in persisted.Agents)
            {
                _agentSettings[s.AgentId] = s;
            }
        }
    }

    /// <summary>Fired when persisted settings change via SetPersistedHotkey/SetPersistedEmoji.</summary>
    public static event Action? PersistedSettingsChanged;

    private static void SetPersistedField(string agentId, string? hotkeyCombo, string? emoji)
    {
        lock (_lock)
        {
            if (!_agentSettings.TryGetValue(agentId, out var s))
            {
                s = new AgentPersistedSettings { AgentId = agentId };
                _agentSettings[agentId] = s;
            }
            s.HotkeyCombination = hotkeyCombo;
            s.Emoji = emoji;

            // Sync with settings service for persistence
            if (_settingsService != null)
            {
                _settingsService.SetHotkey(agentId, hotkeyCombo);
                _settingsService.SetEmoji(agentId, emoji);
                _settingsService.Save();
            }
        }
        PersistedSettingsChanged?.Invoke();
    }
}

// ─── Forwarding members from AgentRegistry for backward compatibility ──
// These keep existing callers working during the transition period.

public static partial class AgentRegistry
{
    /// <summary>Register the settings service so SetPersistedHotkey auto-saves.</summary>
    [Obsolete("Use AgentSettingsPersistence.RegisterSettingsService")]
    public static void RegisterSettingsService(AgentSettingsService service)
        => AgentSettingsPersistence.RegisterSettingsService(service);

    /// <summary>Get per-agent hotkey override.</summary>
    [Obsolete("Use AgentSettingsPersistence.GetPersistedHotkey")]
    public static string? GetPersistedHotkey(string agentId)
        => AgentSettingsPersistence.GetPersistedHotkey(agentId);

    /// <summary>Set per-agent hotkey override.</summary>
    [Obsolete("Use AgentSettingsPersistence.SetPersistedHotkey")]
    public static void SetPersistedHotkey(string agentId, string? hotkeyCombo)
        => AgentSettingsPersistence.SetPersistedHotkey(agentId, hotkeyCombo);

    /// <summary>Get per-agent emoji override.</summary>
    [Obsolete("Use AgentSettingsPersistence.GetPersistedEmoji")]
    public static string? GetPersistedEmoji(string agentId)
        => AgentSettingsPersistence.GetPersistedEmoji(agentId);

    /// <summary>Set per-agent emoji override.</summary>
    [Obsolete("Use AgentSettingsPersistence.SetPersistedEmoji")]
    public static void SetPersistedEmoji(string agentId, string? emoji)
        => AgentSettingsPersistence.SetPersistedEmoji(agentId, emoji);

    /// <summary>All agents with hotkey info.</summary>
    [Obsolete("Use AgentSettingsPersistence.AllAgentsWithHotkeys")]
    public static IReadOnlyList<(AgentInfo Agent, string? Hotkey)> AllAgentsWithHotkeys
        => AgentSettingsPersistence.AllAgentsWithHotkeys;

    /// <summary>All agents with full settings info.</summary>
    [Obsolete("Use AgentSettingsPersistence.AllAgentSettings")]
    public static IReadOnlyList<(AgentInfo Agent, string? Hotkey, string? Emoji)> AllAgentSettings
        => AgentSettingsPersistence.AllAgentSettings;

    /// <summary>Merge persisted settings.</summary>
    [Obsolete("Use AgentSettingsPersistence.MergePersistedSettings")]
    public static void MergePersistedSettings(AgentsConfig persisted)
        => AgentSettingsPersistence.MergePersistedSettings(persisted);

    /// <summary>Fired when persisted settings change.</summary>
    [Obsolete("Use AgentSettingsPersistence.PersistedSettingsChanged")]
    public static event Action? PersistedSettingsChanged
    {
        add => AgentSettingsPersistence.PersistedSettingsChanged += value;
        remove => AgentSettingsPersistence.PersistedSettingsChanged -= value;
    }
}
