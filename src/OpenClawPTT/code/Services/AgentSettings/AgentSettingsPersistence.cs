using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenClawPTT;

/// <summary>
/// Manages per-agent persisted settings (hotkey, emoji) persisted to agents.json.
/// This is the non-static DI-based implementation.
/// </summary>
public sealed class AgentSettingsPersistence : IAgentSettingsPersistence
{
    private readonly object _lock = new();
    private readonly Dictionary<string, AgentPersistedSettings> _agentSettings = new(StringComparer.OrdinalIgnoreCase);
    private readonly AgentSettingsService _settingsService;

    public AgentSettingsPersistence(AgentSettingsService settingsService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        // Load existing settings from the service into the local cache
        MergePersistedSettings(settingsService.ToConfig());
    }

    /// <inheritdoc />
    public string? GetPersistedHotkey(string agentId)
    {
        lock (_lock)
        {
            return _agentSettings.TryGetValue(agentId, out var s) ? s.HotkeyCombination : null;
        }
    }

    /// <inheritdoc />
    public void SetPersistedHotkey(string agentId, string? hotkeyCombo)
    {
        var existing = GetPersistedEmoji(agentId);
        SetPersistedField(agentId, hotkeyCombo, existing);
    }

    /// <inheritdoc />
    public string? GetPersistedEmoji(string agentId)
    {
        lock (_lock)
        {
            return _agentSettings.TryGetValue(agentId, out var s) ? s.Emoji : null;
        }
    }

    /// <inheritdoc />
    public void SetPersistedEmoji(string agentId, string? emoji)
    {
        var existing = GetPersistedHotkey(agentId);
        SetPersistedField(agentId, existing, emoji);
    }

    /// <inheritdoc />
    public IReadOnlyList<(AgentInfo Agent, string? Hotkey)> AllAgentsWithHotkeys
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

    /// <inheritdoc />
    public IReadOnlyList<(AgentInfo Agent, string? Hotkey, string? Emoji)> AllAgentSettings
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

    /// <inheritdoc />
    public void MergePersistedSettings(AgentsConfig persisted)
    {
        lock (_lock)
        {
            foreach (var s in persisted.Agents)
            {
                _agentSettings[s.AgentId] = s;
            }
        }
    }

    /// <inheritdoc />
    public event Action? PersistedSettingsChanged;

    private void SetPersistedField(string agentId, string? hotkeyCombo, string? emoji)
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
            _settingsService.SetHotkey(agentId, hotkeyCombo);
            _settingsService.SetEmoji(agentId, emoji);
            _settingsService.Save();
        }
        PersistedSettingsChanged?.Invoke();
    }
}
