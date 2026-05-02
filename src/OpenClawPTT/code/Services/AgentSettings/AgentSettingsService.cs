using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OpenClawPTT;

/// <summary>Loads, saves, and queries per-agent settings from agents.json.</summary>
public sealed class AgentSettingsService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly string _filePath;
    private List<AgentPersistedSettings> _settings = new();

    public AgentSettingsService(string dataDir)
    {
        _filePath = Path.Combine(dataDir, "agents.json");
    }

    public void Load()
    {
        if (!File.Exists(_filePath))
        {
            _settings = new List<AgentPersistedSettings>();
            return;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var config = JsonSerializer.Deserialize<AgentsConfig>(json, JsonOpts);
            _settings = config?.Agents ?? new List<AgentPersistedSettings>();
        }
        catch
        {
            _settings = new List<AgentPersistedSettings>();
        }
    }

    public void Save()
    {
        var config = new AgentsConfig { Agents = _settings };
        var dir = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(config, JsonOpts));
    }

    public string? GetHotkey(string agentId)
    {
        var entry = _settings.FirstOrDefault(s =>
            s.AgentId.Equals(agentId, System.StringComparison.OrdinalIgnoreCase));
        return entry?.HotkeyCombination;
    }

    public void SetHotkey(string agentId, string? hotkeyCombo)
    {
        var entry = _settings.FirstOrDefault(s =>
            s.AgentId.Equals(agentId, System.StringComparison.OrdinalIgnoreCase));
        if (entry != null)
        {
            entry.HotkeyCombination = hotkeyCombo;
        }
        else
        {
            _settings.Add(new AgentPersistedSettings
            {
                AgentId = agentId,
                HotkeyCombination = hotkeyCombo
            });
        }
    }

    public AgentsConfig ToConfig() => new AgentsConfig { Agents = new List<AgentPersistedSettings>(_settings) };
}
