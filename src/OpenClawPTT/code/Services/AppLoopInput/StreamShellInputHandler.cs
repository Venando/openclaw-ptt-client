using System.Linq;
using OpenClawPTT.Services;
using Spectre.Console;
using StreamShell;

namespace OpenClawPTT;

/// <summary>
/// Integrates StreamShell with the PTT client — registers StreamShell commands
/// (/quit, /reconfigure, OpenClaw slash commands) and wires up non-command
/// user text input to the gateway text sender.
/// </summary>
public sealed class StreamShellInputHandler : IDisposable
{
    private readonly IStreamShellHost _host;
    private readonly ITextMessageSender _textSender;
    private readonly IConfigurationService _configService;
    private readonly AppConfig _appConfig;
    private readonly Action _onQuit;

    public StreamShellInputHandler(
        IStreamShellHost host,
        ITextMessageSender textSender,
        IConfigurationService configService,
        AppConfig appConfig,
        Action onQuit)
    {
        _host = host;
        _textSender = textSender;
        _configService = configService;
        _onQuit = onQuit;
        _appConfig = appConfig;
    }

    /// <summary>Register all commands and the UserInputSubmitted handler.</summary>
    public void Register()
    {
        // OpenClawPTT commands (StreamShell auto-executes these)
        _host.AddCommand(new Command("quit", "Exit the application", QuitHandler));
        _host.AddCommand(new Command("reconfigure", "Run reconfiguration wizard", ReconfigureHandler));
        _host.AddCommand(new Command("crew", "List available agents", CrewHandler));
        _host.AddCommand(new Command("chat", "<name|id> Switch active agent by name or ID", ChatHandler));

        // OpenClaw tool commands (for StreamShell hint support)
        foreach (var name in OpenClawCommands.Names)
        {
            var cmdName = name; // Capture for closure
            _host.AddCommand(new Command(name, Markup.Escape(OpenClawCommands.Descriptions[name]),
                (args, named) => OpenClawCommandHandler(cmdName, args, named)));
        }

        _host.UserInputSubmitted += OnUserInput;
    }

    public void Dispose()
    {
        _host.UserInputSubmitted -= OnUserInput;
    }

    private Task QuitHandler(string[] args, Dictionary<string, string> named)
    {
        _host.AddMessage("[green]  Bye![/]");
        _onQuit();
        return Task.CompletedTask;
    }

    private async Task ReconfigureHandler(string[] args, Dictionary<string, string> named)
    {
        var currentCfg = _configService.Load();
        if (currentCfg == null)
        {
            _host.AddMessage("[yellow]  No configuration found. Run first-time setup instead.[/]");
            return;
        }

        _host.AddMessage("[cyan2]  Starting reconfiguration wizard...[/]");
        try
        {
            var newCfg = await _configService.ReconfigureAsync(_host, currentCfg, CancellationToken.None);
            _host.AddMessage("[green]  Configuration updated.[/]");
        }
        catch (OperationCanceledException)
        {
            _host.AddMessage("[grey]  Reconfiguration cancelled.[/]");
        }
    }

    /// <summary>
    /// Handles user input from StreamShell.
    /// For plain text, sends it as a message. For commands, StreamShell auto-executes
    /// via command registration — nothing to do here.
    /// Attachments (e.g. file paste) are included at the beginning of the message.
    /// </summary>
    private async void OnUserInput(string input, InputType type, IReadOnlyList<Attachment> attachments)
    {
        // Commands are auto-executed by StreamShell — skip
        if (type == InputType.Command)
            return;

        // Prepend attachment content to the message text
        var message = input;
        if (attachments != null && attachments.Count > 0)
        {
            var attachmentTexts = new List<string>();
            foreach (var attachment in attachments)
            {
                var text = attachment.Content;
                // Truncate to first 6 lines or 600 chars, whichever is less
                var lines = text.Split('\n');
                if (lines.Length > 6)
                    text = string.Join("\n", lines.Take(6)) + "\n...";
                if (text.Length > 600)
                    text = text[..600] + "...";
                attachmentTexts.Add(text);
            }
            var attachmentPrefix = string.Join("\n", attachmentTexts);
            message = string.IsNullOrWhiteSpace(message)
                ? attachmentPrefix
                : attachmentPrefix + "\n" + message;
        }

        if (string.IsNullOrWhiteSpace(message))
            return;

        message = message.Trim();

        try
        {
            await _textSender.SendAsync(message, CancellationToken.None);
            ConsoleUi.PrintUserMessage(message);
        }
        catch (Exception ex)
        {
            _host.AddMessage($"[red]  Failed to send message: {Markup.Escape(ex.Message)}[/]");
        }
    }

    /// <summary>
    /// Handler for registered OpenClaw tool commands.
    /// Reconstructs the command text from parsed args and sends it to the gateway.
    /// </summary>
    private Task CrewHandler(string[] args, Dictionary<string, string> named)
    {
        if (args.Length > 0)
        {
            if (args[0].Equals("hotkey", System.StringComparison.OrdinalIgnoreCase))
                return HandleHotkeyCommand(args.Skip(1).ToArray());
            if (args[0].Equals("emoji", System.StringComparison.OrdinalIgnoreCase))
                return HandleEmojiCommand(args.Skip(1).ToArray());
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
            var emoji = AgentRegistry.GetPersistedEmoji(agent.AgentId);
            var emojiStr = emoji != null ? $"{emoji} " : "";
            _host.AddMessage($"  {marker} {emojiStr}[bold]{Markup.Escape(agent.Name)}[/] [grey]({Markup.Escape(agent.AgentId)})[/]");
        }
        _host.AddMessage("[grey]  Use /chat <name|id> to switch, /crew hotkey or /crew emoji to manage settings[/]");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Lists all agents with a formatted setting value.
    /// </summary>
    private void ListAgentSettings(string title, Func<(AgentInfo Agent, string? Hotkey, string? Emoji), string> formatValue)
    {
        var agents = AgentRegistry.AllAgentSettings;
        _host.AddMessage($"[cyan2]  {title}:[/]");
        foreach (var entry in agents)
        {
            var isActive = entry.Agent.SessionKey == AgentRegistry.ActiveSessionKey;
            var marker = isActive ? " ►" : "  ";
            _host.AddMessage($"  {marker} [bold]{Markup.Escape(entry.Agent.Name)}[/] [grey]({Markup.Escape(entry.Agent.AgentId)})[/] — {formatValue(entry)}");
        }
    }

    /// <summary>
    /// Resolves a target agent from args. Returns the agent and the index where the value starts.
    /// If args[0] matches an agent name/id, returns that agent with valueStart=1.
    /// Otherwise returns the active agent with valueStart=0.
    /// </summary>
    private (AgentInfo? Agent, int ValueStart) ResolveTargetAgent(string[] args)
    {
        var matched = ResolveAgent(args[0]);
        bool nameIsAgentRef = matched != null;

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

    private AgentInfo? ResolveAgent(string nameOrId)
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

    private Task HandleHotkeyCommand(string[] args)
    {
        var globalHotkey = _configService.Load()?.HotkeyCombination ?? "Alt+=";

        if (args.Length == 0)
        {
            ListAgentSettings("Agent hotkey settings", entry =>
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
                AgentRegistry.SetPersistedHotkey(activeAgent.AgentId, combo);
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
            "hotkey",
            target =>
            {
                var hk = AgentRegistry.GetPersistedHotkey(target.AgentId);
                return hk ?? $"(global: {globalHotkey})";
            },
            target => AgentRegistry.GetPersistedHotkey(target.AgentId),
            (target, value) =>
            {
                if (value != null)
                {
                    try
                    {
                        HotkeyMapping.Parse(value);
                        AgentRegistry.SetPersistedHotkey(target.AgentId, value);
                    }
                    catch (Exception ex)
                    {
                        _host.AddMessage($"[red]  Invalid hotkey: {Markup.Escape(ex.Message)}[/]");
                    }
                }
                else
                {
                    AgentRegistry.SetPersistedHotkey(target.AgentId, null);
                }
            });
    }

    private Task HandleEmojiCommand(string[] args)
    {
        if (args.Length == 0)
        {
            ListAgentSettings("Agent emoji settings", entry =>
            {
                var emoji = entry.Emoji;
                return emoji != null ? Markup.Escape(emoji) : "[grey](default 🤖)[/]";
            });
            return Task.CompletedTask;
        }

        return HandleSingleAgentSetting(
            args,
            "emoji",
            target =>
            {
                var emoji = AgentRegistry.GetPersistedEmoji(target.AgentId);
                return emoji ?? "(default 🤖)";
            },
            target => AgentRegistry.GetPersistedEmoji(target.AgentId),
            (target, value) => AgentRegistry.SetPersistedEmoji(target.AgentId, value));
    }

    private Task ChatHandler(string[] args, Dictionary<string, string> named)
    {
        if (args.Length == 0)
        {
            _host.AddMessage("[yellow]  Usage: /chat <name|id>[/]");
            return Task.CompletedTask;
        }

        var search = string.Join(" ", args);
        var matched = AgentRegistry.Agents.FirstOrDefault(a =>
            a.Name.Equals(search, StringComparison.OrdinalIgnoreCase) ||
            a.AgentId.Equals(search, StringComparison.OrdinalIgnoreCase));

        if (matched == null)
        {
            _host.AddMessage($"[red]  Agent not found: {Markup.Escape(search)}[/]");
            return Task.CompletedTask;
        }

        if (AgentRegistry.SetActiveAgent(matched.AgentId))
        {
            ConsoleUi.PrintAgentIntroduction(_appConfig);
        }
        else
            _host.AddMessage("[yellow]  That agent is already active.[/]");

        return Task.CompletedTask;
    }

    private async Task OpenClawCommandHandler(string commandName, string[] args, Dictionary<string, string> named)
    {
        // Reconstruct the command text
        var parts = new List<string> { "/" + commandName };
        parts.AddRange(args);
        foreach (var kvp in named)
            parts.Add($"{kvp.Key}={kvp.Value}");

        var commandText = string.Join(" ", parts);

        try
        {
            await _textSender.SendAsync(commandText, CancellationToken.None, printMessage: false);

            ConsoleUi.PrintMarkupedUserMessage($"[blue on gray15]⚡ {Markup.Escape(commandText)} [/]");

        }
        catch (Exception ex)
        {
            _host.AddMessage($"[red]  Failed to send command: {Markup.Escape(ex.Message)}[/]");
        }
    }
}
