using System.Collections.Generic;
using Xunit;

namespace OpenClawPTT.Tests.AgentSettings;

public class AgentRegistrySettingsTests
{
    public AgentRegistrySettingsTests()
    {
        // Reset between tests
        AgentRegistry.SetAgents(new List<AgentInfo>());
    }

    [Fact]
    public void GetPersistedHotkey_NoSettings_ReturnsNull()
    {
        Assert.Null(AgentRegistry.GetPersistedHotkey("agent-1"));
    }

    [Fact]
    public void SetPersistedHotkey_ReturnsValue()
    {
        AgentRegistry.SetPersistedHotkey("agent-1", "Alt+1");
        Assert.Equal("Alt+1", AgentRegistry.GetPersistedHotkey("agent-1"));
    }

    [Fact]
    public void SetPersistedHotkey_NullClearsOverride()
    {
        AgentRegistry.SetPersistedHotkey("agent-1", "Alt+1");
        AgentRegistry.SetPersistedHotkey("agent-1", null);
        Assert.Null(AgentRegistry.GetPersistedHotkey("agent-1"));
    }

    [Fact]
    public void AllAgentsWithHotkeys_ReturnsList()
    {
        var agents = new List<AgentInfo>
        {
            new() { AgentId = "a1", Name = "Alpha", SessionKey = "agent:a1:main", IsDefault = true },
            new() { AgentId = "a2", Name = "Beta", SessionKey = "agent:a2:main" },
        };
        AgentRegistry.SetAgents(agents);
        AgentRegistry.SetPersistedHotkey("a1", "Alt+1");

        var result = AgentRegistry.AllAgentsWithHotkeys;
        Assert.Equal(2, result.Count);
        Assert.Equal("Alt+1", result[0].Hotkey);
    }

    [Fact]
    public void MergePersistedSettings_OverlaysHotkeys()
    {
        var agents = new List<AgentInfo>
        {
            new() { AgentId = "a1", Name = "Alpha", SessionKey = "agent:a1:main", IsDefault = true },
        };
        AgentRegistry.SetAgents(agents);

        var persisted = new AgentsConfig
        {
            Agents = new List<AgentPersistedSettings>
            {
                new() { AgentId = "a1", HotkeyCombination = "Ctrl+Shift+A" }
            }
        };
        AgentRegistry.MergePersistedSettings(persisted);

        Assert.Equal("Ctrl+Shift+A", AgentRegistry.GetPersistedHotkey("a1"));
    }

    [Fact]
    public void SetPersistedHotkey_FiresPersistedSettingsChanged()
    {
        bool fired = false;
        AgentRegistry.PersistedSettingsChanged += () => fired = true;
        AgentRegistry.SetPersistedHotkey("a1", "Alt+1");
        Assert.True(fired);
    }
}
