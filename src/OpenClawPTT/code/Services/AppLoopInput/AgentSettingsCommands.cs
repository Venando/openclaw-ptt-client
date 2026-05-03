using System;
using System.Collections.Generic;
using System.Linq;
using OpenClawPTT.Services;
using Spectre.Console;

namespace OpenClawPTT;

/// <summary>
/// Handles StreamShell commands for per-agent configuration:
/// hotkey overrides and emoji overrides.
/// Extracted from StreamShellInputHandler to honor Single Responsibility.
/// </summary>
public sealed class AgentSettingsCommands
{
    private readonly IStreamShellHost _host;
    private readonly IConfigurationService _configService;

    public AgentSettingsCommands(IStreamShellHost host, IConfigurationService configService)
    {
        _host = host;
        _configService = configService;
    }

    /// <summary>Handler for /crew hotkey.</summary>
    public Task HandleHotkeyCommand(string[] args)
    {
        const string settingName = "hotkey";
        var globalHotkey = _configService.Load()?.HotkeyCombination ?? "Alt+=";

        if (args.Length == 0)
        {
            ListAgentSettings(settingName, AgentSettingsPersistence.AllAgentSettings, entry =>
            {
                var hk = entry.Hotkey;
                return hk != null
                    ? Markup.Escape(hk)
                    : $"[grey](global: {Markup.Escape(globalHotkey)})[/]";
            });
            return Task.CompletedTask;
        }

        // Quick check: if first arg is a hotkey string (not agent name), set for active agent
        var firstArgIsHotkey = args[0].Contains('+');

        if (firstArgIsHotkey)
        {
            var activeAgent = AgentRegistry.Agents.FirstOrDefault(a => a.SessionKey == AgentRegistry.ActiveSessionKey);
            if (activeAgent == null)
            {
                _host.AddMessage("[yellow]  No active agent to configure.[/]");
                return Task.CompletedTask;
            }

            var combo = args[0];
            try
            {
                HotkeyMapping.Parse(combo);
                AgentSettingsPersistence.SetPersistedHotkey(activeAgent.AgentId, combo);
                _host.AddMessage($"[green]  Set hotkey for {Markup.Escape(activeAgent.Name)}: {Markup.Escape(combo)}[/]");
            }
            catch (Exception ex)
            {
                _host.AddMessage($"[red]  Invalid hotkey: {Markup.Escape(ex.Message)}[/]");
            }
            return Task.CompletedTask;
        }

        // Use generic handler for named agent + combo
        return HandleSingleAgentSetting(
            args,
            settingName,
            target =>
            {
                var hk = AgentSettingsPersistence.GetPersistedHotkey(target.AgentId);
                return hk ?? $"(global: {globalHotkey})";
            },
            target => AgentSettingsPersistence.GetPersistedHotkey(target.AgentId),
            (target, value) =>
            {
                if (value != null)
                {
                    try
                    {
                        HotkeyMapping.Parse(value);
                        AgentSettingsPersistence.SetPersistedHotkey(target.AgentId, value);
                    }
                    catch (Exception ex)
                    {
                        _host.AddMessage($"[red]  Invalid hotkey: {Markup.Escape(ex.Message)}[/]");
                    }
                }
                else
                {
                    AgentSettingsPersistence.SetPersistedHotkey(target.AgentId, null);
                }
            });
    }

    /// <summary>Handler for /crew emoji.</summary>
    public Task HandleEmojiCommand(string[] args)
    {
        const string settingName = "emoji";

        if (args.Length == 0)
        {
            ListAgentSettings(settingName, AgentSettingsPersistence.AllAgentSettings, entry =>
            {
                var emoji = entry.Emoji;
                return emoji != null ? Markup.Escape(emoji) : "[grey](default 🤖)[/]";
            });
            return Task.CompletedTask;
        }

        return HandleSingleAgentSetting(
            args,
            settingName,
            target =>
            {
                var emoji = AgentSettingsPersistence.GetPersistedEmoji(target.AgentId);
                return emoji ?? "(default 🤖)";
            },
            target => AgentSettingsPersistence.GetPersistedEmoji(target.AgentId),
            (target, value) => AgentSettingsPersistence.SetPersistedEmoji(target.AgentId, value));
    }

    /// <summary>Lists all agents with a formatted setting value.</summary>
    private void ListAgentSettings(string title, IReadOnlyList<(AgentInfo Agent, string? Hotkey, string? Emoji)> agents, Func<(AgentInfo Agent, string? Hotkey, string? Emoji), string> formatValue)
    {
        _host.AddMessage($"[cyan2]  {title}:[/]");
        foreach (var entry in agents)
        {
            var isActive = entry.Agent.SessionKey == AgentRegistry.ActiveSessionKey;
            var marker = isActive ? " ►" : "  ";
            _host.AddMessage($"  {marker} [bold]{Markup.Escape(entry.Agent.Name)}[/] [grey]({Markup.Escape(entry.Agent.AgentId)})[/] — {formatValue(entry)}");
        }
    }

    /// <summary>Resolves a target agent from args. Returns the agent and the index where the value starts.</summary>
    private (AgentInfo? Agent, int ValueStart) ResolveTargetAgent(string[] args)
    {
        var matched = ResolveAgent(args[0]);
        var nameIsAgentRef = matched != null;

        if (nameIsAgentRef && args.Length == 1)
            return (matched, -1); // show only, no value

        if (nameIsAgentRef)
            return (matched, 1);

        var activeAgent = AgentRegistry.Agents.FirstOrDefault(a => a.SessionKey == AgentRegistry.ActiveSessionKey);
        return (activeAgent, 0);
    }

    /// <summary>
    /// Handles the set/show/clear pattern for a single agent setting.
    /// For listing (args empty), use ListAgentSettings instead.
    /// </summary>
    private Task HandleSingleAgentSetting(
        string[] args,
        string settingName,
        Func<AgentInfo, string> getDisplay,
        Func<AgentInfo, string?> getValue,
        Action<AgentInfo, string?> setValue)
    {
        if (args.Length == 0)
            return Task.CompletedTask; // caller should have used ListAgentSettings

        var (target, valueStart) = ResolveTargetAgent(args);

        if (target == null)
        {
            _host.AddMessage("[yellow]  No active agent to configure.[/]");
            return Task.CompletedTask;
        }

        // Show current value (single arg that matched an agent)
        if (valueStart < 0)
        {
            _host.AddMessage($"  [bold]{Markup.Escape(target.Name)}[/] {settingName}: {Markup.Escape(getDisplay(target))}");
            return Task.CompletedTask;
        }

        var valueArg = args[valueStart];

        if (valueArg.Equals("--clear", StringComparison.OrdinalIgnoreCase))
        {
            setValue(target, null);
            _host.AddMessage($"[green]  Cleared {settingName} override for {Markup.Escape(target.Name)}[/]");
            return Task.CompletedTask;
        }

        setValue(target, valueArg);
        _host.AddMessage($"[green]  Set {settingName} for {Markup.Escape(target.Name)}: {Markup.Escape(valueArg)}[/]");
        return Task.CompletedTask;
    }

    private static AgentInfo? ResolveAgent(string nameOrId)
    {
        // Try exact name/ID match
        var matched = AgentRegistry.Agents.FirstOrDefault(a =>
            a.Name.Equals(nameOrId, StringComparison.OrdinalIgnoreCase) ||
            a.AgentId.Equals(nameOrId, StringComparison.OrdinalIgnoreCase));
        if (matched != null) return matched;

        // Fall back to active agent
        var activeKey = AgentRegistry.ActiveSessionKey;
        return AgentRegistry.Agents.FirstOrDefault(a => a.SessionKey == activeKey);
    }
}
