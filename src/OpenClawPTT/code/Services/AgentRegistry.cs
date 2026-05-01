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

    /// <summary>Event raised when the active session changes.</summary>
    public static event Action<string?>? ActiveSessionChanged;

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
}
