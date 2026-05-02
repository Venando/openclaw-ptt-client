using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenClawPTT;

/// <summary>
/// Thread-safe registry of available agents and the currently active session key.
/// Accessible app-wide as a singleton.
/// </summary>
public static class AgentRegistry
{
    private static readonly object _lock = new();
    private static List<AgentInfo> _agents = new();
    private static string? _activeSessionKey;
    private static Dictionary<string, string?> _persistedHotkeys = new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, string?> _persistedEmojis = new(StringComparer.OrdinalIgnoreCase);
    private static AgentSettingsService? _settingsService;

    /// <summary>Event raised when the active session changes.</summary>
    public static event Action<string?>? ActiveSessionChanged;

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
            return _persistedHotkeys.TryGetValue(agentId, out var hk) ? hk : null;
        }
    }

    /// <summary>Set or clear per-agent hotkey override. Fires PersistedSettingsChanged.</summary>
    public static void SetPersistedHotkey(string agentId, string? hotkeyCombo)
    {
        lock (_lock)
        {
            if (hotkeyCombo != null)
                _persistedHotkeys[agentId] = hotkeyCombo;
            else
                _persistedHotkeys.Remove(agentId);

            _settingsService?.SetHotkey(agentId, hotkeyCombo);
            _settingsService?.Save();
        }
        PersistedSettingsChanged?.Invoke();
    }

    /// <summary>Get per-agent emoji override, or null for default (🤖).</summary>
    public static string? GetPersistedEmoji(string agentId)
    {
        lock (_lock)
        {
            return _persistedEmojis.TryGetValue(agentId, out var emoji) ? emoji : null;
        }
    }

    /// <summary>Set or clear per-agent emoji override. Fires PersistedSettingsChanged.</summary>
    public static void SetPersistedEmoji(string agentId, string? emoji)
    {
        lock (_lock)
        {
            if (emoji != null)
                _persistedEmojis[agentId] = emoji;
            else
                _persistedEmojis.Remove(agentId);

            _settingsService?.SetEmoji(agentId, emoji);
            _settingsService?.Save();
        }
        PersistedSettingsChanged?.Invoke();
    }

    /// <summary>All agents with their effective hotkey (override or null).</summary>
    public static System.Collections.Generic.IReadOnlyList<(AgentInfo Agent, string? Hotkey)> AllAgentsWithHotkeys
    {
        get
        {
            lock (_lock)
            {
                return _agents.Select(a => (a, _persistedHotkeys.TryGetValue(a.AgentId, out var hk) ? hk : (string?)null)).ToList().AsReadOnly();
            }
        }
    }

    /// <summary>All agents with their effective hotkey and emoji.</summary>
    public static System.Collections.Generic.IReadOnlyList<(AgentInfo Agent, string? Hotkey, string? Emoji)> AllAgentSettings
    {
        get
        {
            lock (_lock)
            {
                return _agents.Select(a => (
                    a,
                    _persistedHotkeys.TryGetValue(a.AgentId, out var hk) ? hk : (string?)null,
                    _persistedEmojis.TryGetValue(a.AgentId, out var em) ? em : (string?)null
                )).ToList().AsReadOnly();
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
                if (s.HotkeyCombination != null)
                    _persistedHotkeys[s.AgentId] = s.HotkeyCombination;
                if (s.Emoji != null)
                    _persistedEmojis[s.AgentId] = s.Emoji;
            }
        }
    }

    /// <summary>Fired when persisted settings change via SetPersistedHotkey.</summary>
    public static event System.Action? PersistedSettingsChanged;

    /// <summary>Replaces the entire agent list. Resets active session if no longer valid.</summary>
    public static void SetAgents(IReadOnlyList<AgentInfo> agents)
    {
        lock (_lock)
        {
            _agents = agents.ToList();

            // If the current active session is no longer in the list, pick the default or first
            if (_activeSessionKey != null && _agents.All(a => a.SessionKey != _activeSessionKey))
                _activeSessionKey = null;

            if (_activeSessionKey == null)
            {
                var defaultAgent = _agents.FirstOrDefault(a => a.IsDefault) ?? _agents.FirstOrDefault();
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

    /// <summary>Name of the currently active agent, or null if none.</summary>
    public static string? ActiveAgentName
    {
        get
        {
            lock (_lock)
            {
                if (_activeSessionKey == null) return null;
                return _agents.FirstOrDefault(a => a.SessionKey == _activeSessionKey)?.Name;
            }
        }
    }

    public static bool IsActiveAgentAvailable
    {
        get
        {
            lock (_lock)
            {
                return ActiveAgentName != null;
            }
        }
    }

    /// <summary>Currently active session key. Messages from other sessions are filtered out.</summary>
    public static string? ActiveSessionKey
    {
        get { lock (_lock) return _activeSessionKey; }
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

    public static void GetActiveNameAndEmoji(out object agentName, out object emoji, string defaultName = "Agent", string defaultEmoji = "🤖")
    {
        lock (_lock)
        {
            agentName = ActiveAgentName ?? defaultName;
            var activeKey = ActiveSessionKey;
            emoji = Agents
                .Where(a => a.SessionKey == activeKey)
                .Select(a => GetPersistedEmoji(a.AgentId))
                .FirstOrDefault() ?? defaultEmoji;
        }
    }
}
