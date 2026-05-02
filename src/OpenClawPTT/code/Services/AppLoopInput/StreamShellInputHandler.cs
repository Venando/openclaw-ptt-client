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
            var agents = AgentRegistry.AllAgentsWithHotkeys;
            _host.AddMessage("[cyan2]  Agent hotkey settings:[/]");
            foreach (var (agent, hotkey) in agents)
            {
                var displayHk = hotkey != null
                    ? Markup.Escape(hotkey)
                    : $"[grey](global: {Markup.Escape(globalHotkey)})[/]";
                var isActive = agent.SessionKey == AgentRegistry.ActiveSessionKey;
                var marker = isActive ? " ►" : "  ";
                var emoji = AgentRegistry.GetPersistedEmoji(agent.AgentId);
                var emojiStr = emoji != null ? $"{emoji} " : "";
                _host.AddMessage($"  {marker} {emojiStr}[bold]{Markup.Escape(agent.Name)}[/] [grey]({Markup.Escape(agent.AgentId)})[/] — {displayHk}");
            }
            return Task.CompletedTask;
        }

        // Resolve agent: if args[0] matches a name/id, use that agent; otherwise use active
        var matched = ResolveAgent(args[0]);
        bool nameIsAgentRef = matched != null && !IsHotkeyString(args[0]);
        AgentInfo target;
        int comboStart;

        if (nameIsAgentRef && args.Length == 1)
        {
            // Show current hotkey for this agent
            var hk = AgentRegistry.GetPersistedHotkey(matched.AgentId);
            var display = hk ?? $"(global: {globalHotkey})";
            _host.AddMessage($"  [bold]{Markup.Escape(matched.Name)}[/] hotkey: {Markup.Escape(display)}");
            return Task.CompletedTask;
        }

        if (nameIsAgentRef && args.Length >= 2)
        {
            target = matched;
            comboStart = 1;
        }
        else
        {
            // Use active agent
            var activeAgent = AgentRegistry.Agents.FirstOrDefault(a => a.SessionKey == AgentRegistry.ActiveSessionKey);
            if (activeAgent == null)
            {
                _host.AddMessage("[yellow]  No active agent to configure.[/]");
                return Task.CompletedTask;
            }
            target = activeAgent;
            comboStart = 0;
        }

        var comboArgs = args.Skip(comboStart).ToArray();

        if (comboArgs.Length > 0 && comboArgs[0].Equals("--clear", StringComparison.OrdinalIgnoreCase))
        {
            AgentRegistry.SetPersistedHotkey(target.AgentId, null);
            _host.AddMessage($"[green]  Cleared hotkey override for {Markup.Escape(target.Name)}[/]");
            return Task.CompletedTask;
        }

        var combo = string.Join(" ", comboArgs);
        try
        {
            HotkeyMapping.Parse(combo);
            AgentRegistry.SetPersistedHotkey(target.AgentId, combo);
            _host.AddMessage($"[green]  Set hotkey for {Markup.Escape(target.Name)}: {Markup.Escape(combo)}[/]");
        }
        catch (Exception ex)
        {
            _host.AddMessage($"[red]  Invalid hotkey: {Markup.Escape(ex.Message)}[/]");
        }

        return Task.CompletedTask;
    }

    private static bool IsHotkeyString(string s)
    {
        return s.Contains('+') || s.Equals("--clear", System.StringComparison.OrdinalIgnoreCase);
    }

    private Task HandleEmojiCommand(string[] args)
    {
        if (args.Length == 0)
        {
            var agents = AgentRegistry.AllAgentSettings;
            _host.AddMessage("[cyan2]  Agent emoji settings:[/]");
            foreach (var (agent, _, emoji) in agents)
            {
                var isActive = agent.SessionKey == AgentRegistry.ActiveSessionKey;
                var marker = isActive ? " ►" : "  ";
                var display = emoji != null ? Markup.Escape(emoji) : "[grey](default 🤖)[/]";
                _host.AddMessage($"  {marker} [bold]{Markup.Escape(agent.Name)}[/] [grey]({Markup.Escape(agent.AgentId)})[/] — {display}");
            }
            return Task.CompletedTask;
        }

        // Resolve agent
        var matched = ResolveAgent(args[0]);
        bool nameIsAgentRef = matched != null;
        AgentInfo target;
        int valueArgPos;

        if (nameIsAgentRef && args.Length == 1)
        {
            // Show current emoji for this agent
            var emoji = AgentRegistry.GetPersistedEmoji(matched.AgentId);
            var display = emoji != null ? Markup.Escape(emoji) : "(default 🤖)";
            _host.AddMessage($"  [bold]{Markup.Escape(matched.Name)}[/] emoji: {display}");
            return Task.CompletedTask;
        }

        if (nameIsAgentRef && args.Length >= 2)
        {
            target = matched;
            valueArgPos = 1;
        }
        else
        {
            var activeAgent = AgentRegistry.Agents.FirstOrDefault(a => a.SessionKey == AgentRegistry.ActiveSessionKey);
            if (activeAgent == null)
            {
                _host.AddMessage("[yellow]  No active agent to configure.[/]");
                return Task.CompletedTask;
            }
            target = activeAgent;
            valueArgPos = 0;
        }

        var valueArg = args[valueArgPos];

        if (valueArg.Equals("--clear", StringComparison.OrdinalIgnoreCase))
        {
            AgentRegistry.SetPersistedEmoji(target.AgentId, null);
            _host.AddMessage($"[green]  Cleared emoji override for {Markup.Escape(target.Name)}[/]");
            return Task.CompletedTask;
        }

        AgentRegistry.SetPersistedEmoji(target.AgentId, valueArg);
        _host.AddMessage($"[green]  Set emoji for {Markup.Escape(target.Name)}: {Markup.Escape(valueArg)}[/]");

        return Task.CompletedTask;
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
            // _host.AddMessage($"[green]  Switched to agent: {Markup.Escape(matched.Name)}[/]");
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
