using System;
using System.Linq;
using System.Threading.Tasks;
using OpenClawPTT.Services;
using Spectre.Console;

namespace OpenClawPTT;

/// <summary>
/// Handles StreamShell commands for agent listing and switching:
/// /crew, /chat, session history, and OpenClaw tool command forwarding.
/// Extracted from StreamShellInputHandler to honor Single Responsibility.
/// </summary>
public sealed class AgentSwitchingCommands
{
    private readonly IStreamShellHost _host;
    private readonly ITextMessageSender _textSender;
    private readonly IGatewayService _gatewayService;
    private readonly AppConfig _appConfig;

    public AgentSwitchingCommands(IStreamShellHost host, ITextMessageSender textSender, IGatewayService gatewayService, AppConfig appConfig)
    {
        _host = host;
        _textSender = textSender;
        _gatewayService = gatewayService;
        _appConfig = appConfig;
    }

    /// <summary>Handler for /crew — lists available agents.</summary>
    public Task HandleCrew(string[] args)
    {
        if (args.Length > 0)
        {
            if (args[0].Equals("hotkey", StringComparison.OrdinalIgnoreCase))
                return Task.CompletedTask; // handled by AgentSettingsCommands
            if (args[0].Equals("emoji", StringComparison.OrdinalIgnoreCase))
                return Task.CompletedTask; // handled by AgentSettingsCommands
        }

        var agents = AgentRegistry.Agents;
        var activeKey = AgentRegistry.ActiveSessionKey;

        if (agents.Count == 0)
        {
            _host.AddMessage("[yellow]  No agents available. Make sure you're connected.[/]");
            return Task.CompletedTask;
        }

        _host.AddMessage("[cyan2]  Available agents:[/]");
        foreach (var agent in agents)
        {
            var isActive = agent.SessionKey == activeKey;
            var marker = isActive ? " ►" : "  ";
            var emoji = AgentSettingsPersistence.GetPersistedEmoji(agent.AgentId);
            var emojiStr = emoji != null ? $"{emoji} " : "";
            _host.AddMessage($"  {marker} {emojiStr}[bold]{Markup.Escape(agent.Name)}[/] [grey]({Markup.Escape(agent.AgentId)})[/]");
        }
        _host.AddMessage("[grey]  Use /chat <name|id> to switch, /crew hotkey or /crew emoji to manage settings[/]");
        return Task.CompletedTask;
    }

    /// <summary>Handler for /chat — switches active agent and prints recent history.</summary>
    public async Task HandleChat(string[] args)
    {
        if (args.Length == 0)
        {
            _host.AddMessage("[yellow]  Usage: /chat <name|id>[/]");
            return;
        }

        var search = string.Join(" ", args);
        var matched = AgentRegistry.Agents.FirstOrDefault(a =>
            a.Name.Equals(search, StringComparison.OrdinalIgnoreCase) ||
            a.AgentId.Equals(search, StringComparison.OrdinalIgnoreCase));

        if (matched == null)
        {
            _host.AddMessage($"[red]  Agent not found: {Markup.Escape(search)}[/]");
            return;
        }

        if (AgentRegistry.SetActiveAgent(matched.AgentId))
        {
            await PrintSessionHistory(matched.SessionKey);
            ConsoleUi.PrintAgentIntroduction(_appConfig);
        }
        else
        {
            _host.AddMessage("[yellow]  That agent is already active.[/]");
        }
    }

    /// <summary>Fetches and displays recent session history.</summary>
    public async Task PrintSessionHistory(string sessionKey)
    {
        var history = await _gatewayService.FetchSessionHistoryAsync(sessionKey, limit: 5);
        if (history == null || history.Count == 0)
            return;

        _host.AddMessage("  [grey]── previous messages ──[/]");
        foreach (var entry in history)
        {
            if (entry.Role.Equals("user", StringComparison.OrdinalIgnoreCase))
                ConsoleUi.PrintUserMessage(entry.Content);
            else
                _gatewayService.DisplayHistoryEntry(entry);
        }
    }

    /// <summary>
    /// Forwards an OpenClaw slash command (e.g. /new, /stop) to the gateway.
    /// </summary>
    public async Task HandleOpenClawCommand(string commandName, string[] args, System.Collections.Generic.Dictionary<string, string> named)
    {
        // Reconstruct the command text
        var parts = new System.Collections.Generic.List<string> { "/" + commandName };
        parts.AddRange(args);
        foreach (var kvp in named)
            parts.Add($"{kvp.Key}={kvp.Value}");

        var commandText = string.Join(" ", parts);

        try
        {
            await _textSender.SendAsync(commandText, System.Threading.CancellationToken.None, printMessage: false);
            ConsoleUi.PrintMarkupedUserMessage($"[blue on gray15]⚡ {Markup.Escape(commandText)} [/]");
        }
        catch (Exception ex)
        {
            _host.AddMessage($"[red]  Failed to send command: {Markup.Escape(ex.Message)}[/]");
        }
    }
}
