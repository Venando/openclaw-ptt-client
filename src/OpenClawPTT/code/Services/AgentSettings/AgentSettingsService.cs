using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenClawPTT.Services;

namespace OpenClawPTT;

/// <summary>Loads, saves, and queries per-agent settings from agents.json.</summary>
public sealed class AgentSettingsService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly IColorConsole _console;
    private List<AgentPersistedSettings> _settings = new();

    public AgentSettingsService(string dataDir, IColorConsole console)
    {
        _filePath = Path.Combine(dataDir, "agents.json");
        _console = console;
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
        catch (JsonException ex)
        {
            _console.LogError("settings", $"Failed to parse agents.json: {ex.Message}. Starting with empty settings.");
            _settings = new List<AgentPersistedSettings>();
        }
        catch (IOException ex)
        {
            _console.LogError("settings", $"Failed to read agents.json: {ex.Message}. Starting with empty settings.");
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
        => SetField(agentId, s => s.HotkeyCombination = hotkeyCombo);

    public string? GetEmoji(string agentId)
    {
        var entry = _settings.FirstOrDefault(s =>
            s.AgentId.Equals(agentId, System.StringComparison.OrdinalIgnoreCase));
        return entry?.Emoji;
    }

    public void SetEmoji(string agentId, string? emoji)
        => SetField(agentId, s => s.Emoji = emoji);

    private void SetField(string agentId, System.Action<AgentPersistedSettings> setter)
    {
        var entry = _settings.FirstOrDefault(s =>
            s.AgentId.Equals(agentId, System.StringComparison.OrdinalIgnoreCase));
        if (entry != null)
        {
            setter(entry);
        }
        else
        {
            entry = new AgentPersistedSettings { AgentId = agentId };
            setter(entry);
            _settings.Add(entry);
        }
    }

    public AgentsConfig ToConfig() => new AgentsConfig { Agents = new List<AgentPersistedSettings>(_settings) };
}
