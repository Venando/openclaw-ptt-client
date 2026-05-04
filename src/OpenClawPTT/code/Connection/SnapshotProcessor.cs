using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenClawPTT;

/// <summary>
/// Processes gateway snapshot data to update agent registry.
/// </summary>
public sealed class SnapshotProcessor : ISnapshotProcessor
{
    private readonly ILogger _logger;
    private readonly bool _logHello;

    /// <summary>
    /// Creates a new SnapshotProcessor.
    /// </summary>
    /// <param name="logger">The logger for diagnostic output.</param>
    /// <param name="logHello">Whether to log snapshot details.</param>
    public SnapshotProcessor(ILogger logger, bool logHello = false)
    {
        _logger = logger;
        _logHello = logHello;
    }

    /// <inheritdoc />
    public void ProcessSnapshot(JsonElement hello)
    {
        if (!hello.TryGetProperty("snapshot", out var snapshot))
            return;

        if (_logHello)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string prettySnapshot = JsonSerializer.Serialize(snapshot, options);
            var lines = $"--- SERVER SNAPSHOT PAYLOAD ---\n{prettySnapshot}\n----------------------------".Split('\n');
            foreach (var line in lines) _logger.Log("ws", line);
        }

        if (snapshot.TryGetProperty("health", out var health)
            && health.TryGetProperty("agents", out var agents))
        {
            var agentList = new List<AgentInfo>();
            foreach (JsonElement agent in agents.EnumerateArray())
            {
                string agentId = agent.GetProperty("agentId").GetString() ?? "";
                string name = agent.GetProperty("name").GetString() ?? "";
                bool isDefault = agent.GetProperty("isDefault").GetBoolean();
                string sessionKey = $"agent:{agentId}:main";

                agentList.Add(new AgentInfo
                {
                    AgentId = agentId,
                    Name = name,
                    IsDefault = isDefault,
                    SessionKey = sessionKey
                });
            }

            AgentRegistry.SetAgents(agentList);
            _logger.Log("gateway", $"Loaded {agentList.Count} agent(s). Active session: {AgentRegistry.ActiveSessionKey}");
        }
    }
}
