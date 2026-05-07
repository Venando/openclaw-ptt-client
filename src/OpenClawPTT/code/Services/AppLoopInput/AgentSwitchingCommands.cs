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
    private readonly IColorConsole _console;
    private readonly IAgentSettingsPersistence _agentSettingsPersistence;
    private readonly IPttStateMachine _pttStateMachine;

    public AgentSwitchingCommands(IStreamShellHost host, ITextMessageSender textSender, IGatewayService gatewayService, AppConfig appConfig, IColorConsole console, IAgentSettingsPersistence agentSettingsPersistence, IPttStateMachine pttStateMachine)
    {
        _host = host;
        _textSender = textSender;
        _gatewayService = gatewayService;
        _appConfig = appConfig;
        _console = console;
        _agentSettingsPersistence = agentSettingsPersistence;
        _pttStateMachine = pttStateMachine;
    }

    /// <summary>Handler for /crew — lists available agents with all settings.</summary>
    public Task HandleCrew(string[] args)
    {
        // Subcommands (hotkey, emoji, color, config) are routed upstream —
        // here we only show the unified display for the bare /crew command.
        var agents = AgentRegistry.Agents;
        var activeKey = AgentRegistry.ActiveSessionKey;

        if (agents.Count == 0)
        {
            _host.AddMessage("[yellow]  No agents available. Make sure you're connected.[/]");
            return Task.CompletedTask;
        }

        var globalHotkey = _appConfig.HotkeyCombination;
        var allSettings = _agentSettingsPersistence.AllAgentSettings;

        _host.AddMessage("[cyan2]  Available agents:[/]");
        foreach (var entry in allSettings)
        {
            var isActive = entry.Agent.SessionKey == activeKey;
            var marker = isActive ? " ►" : "  ";
            var emojiDisplay = entry.Emoji ?? "🤖";
            var colorTag = entry.Color != null ? $"[{entry.Color}]" : "";
            var colorClose = entry.Color != null ? $"[/{entry.Color}]" : "";
            var nameDisplay = $"{colorTag}{Markup.Escape(entry.Agent.Name)}{colorClose}";
            var hotkeyDisplay = entry.Hotkey != null
                ? Markup.Escape(entry.Hotkey)
                : $"[grey](global: {Markup.Escape(globalHotkey)})[/]";
            var colorValueDisplay = entry.Color != null
                ? $"{colorTag}{Markup.Escape(entry.Color)}{colorClose}"
                : "[grey]default[/]";

            _host.AddMessage($"  {marker} {emojiDisplay} [bold]{nameDisplay}[/] [grey]({Markup.Escape(entry.Agent.AgentId)})[/] — hotkey: {hotkeyDisplay}, emoji: {Markup.Escape(emojiDisplay)}, color: {colorValueDisplay}");
        }
        _host.AddMessage("[grey]  Use /crew config for interactive setup, or /crew hotkey, /crew emoji, /crew color to change individual settings[/]");
        return Task.CompletedTask;
    }

    /// <summary>Handler for /crew config — interactive agent configuration wizard.</summary>
    public Task HandleConfigCommand(string[] args)
    {
        if (args.Length == 0)
        {
            // Show list and prompt to type agent name
            var agents = AgentRegistry.Agents;
            _host.AddMessage("[cyan2]  Select an agent to configure:[/]");
            foreach (var agent in agents)
            {
                var emoji = _agentSettingsPersistence.GetPersistedEmoji(agent.AgentId) ?? "🤖";
                var color = _agentSettingsPersistence.GetPersistedColor(agent.AgentId);
                var nameStr = color != null ? $"[{color}]{Markup.Escape(agent.Name)}[/{color}]" : Markup.Escape(agent.Name);
                _host.AddMessage($"  {emoji} {nameStr} [grey]({Markup.Escape(agent.AgentId)})[/]");
            }
            _host.AddMessage("[grey]  Type /crew config <agent-name> to start the setup wizard[/]");
            return Task.CompletedTask;
        }

        // Agent specified — start config wizard
        var agentName = string.Join(" ", args);
        var matched = AgentRegistry.Agents.FirstOrDefault(a =>
            a.Name.Equals(agentName, StringComparison.OrdinalIgnoreCase) ||
            a.AgentId.Equals(agentName, StringComparison.OrdinalIgnoreCase));

        if (matched == null)
        {
            _host.AddMessage($"[red]  Agent not found: {Markup.Escape(agentName)}[/]");
            return Task.CompletedTask;
        }

        // Start the agent config wizard
        var wizard = new AgentConfigWizard(_host, _agentSettingsPersistence);
        _ = wizard.RunAsync(matched); // Fire and forget — wizard uses events
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
            _console.PrintAgentIntroduction(_appConfig);
        }
        else
        {
            _host.AddMessage("[yellow]  That agent is already active.[/]");
        }
    }

    /// <summary>Fetches and displays recent session history.</summary>
    public async Task PrintSessionHistory(string sessionKey)
    {
        var history = await _gatewayService.FetchSessionHistoryAsync(sessionKey, limit: _appConfig.HistoryDisplayCount);
        if (history == null || history.Count == 0)
            return;


        // Suppress TTS during history replay
        _pttStateMachine.DuringReplay = true;
        try
        {
            _host.AddMessage("  [grey]── previous messages ──[/]");
            foreach (var entry in history)
            {
                if (entry.Role.Equals("user", StringComparison.OrdinalIgnoreCase))
                    _console.PrintUserMessage(entry.Content);
                else
                    _gatewayService.DisplayHistoryEntry(entry);
            }
        }
        finally
        {
            _pttStateMachine.DuringReplay = false;
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
            _console.PrintMarkupedUserMessage($"[blue on gray15]⚡ {Markup.Escape(commandText)} [/]");
        }
        catch (Exception ex)
        {
            _host.AddMessage($"[red]  Failed to send command: {Markup.Escape(ex.Message)}[/]");
        }
    }
}