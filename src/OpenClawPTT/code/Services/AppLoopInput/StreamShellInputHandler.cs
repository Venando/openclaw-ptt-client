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
    private readonly Action _onQuit;

    public StreamShellInputHandler(
        IStreamShellHost host,
        ITextMessageSender textSender,
        IConfigurationService configService,
        Action onQuit)
    {
        _host = host;
        _textSender = textSender;
        _configService = configService;
        _onQuit = onQuit;
    }

    /// <summary>Register all commands and the UserInputSubmitted handler.</summary>
    public void Register()
    {
        // OpenClawPTT commands (StreamShell auto-executes these)
        _host.AddCommand(new Command("quit", "Exit the application", QuitHandler));
        _host.AddCommand(new Command("reconfigure", "Run reconfiguration wizard", ReconfigureHandler));
        _host.AddCommand(new Command("agents", "List available agents", AgentsHandler));
        _host.AddCommand(new Command("agent", "Switch active agent by name or ID: /agent <name|id>", AgentHandler));

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
    private Task AgentsHandler(string[] args, Dictionary<string, string> named)
    {
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
            _host.AddMessage($"  {marker} [bold]{Markup.Escape(agent.Name)}[/] [grey]({Markup.Escape(agent.AgentId)})[/]");
        }
        _host.AddMessage("[grey]  Use /agent &lt;name|id&gt; to switch[/]");
        return Task.CompletedTask;
    }

    private Task AgentHandler(string[] args, Dictionary<string, string> named)
    {
        if (args.Length == 0)
        {
            _host.AddMessage("[yellow]  Usage: /agent &lt;name|id&gt;[/]");
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
            _host.AddMessage($"[green]  Switched to agent: {Markup.Escape(matched.Name)}[/]");
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
            await _textSender.SendAsync(commandText, CancellationToken.None);

            ConsoleUi.PrintMarkupedUserMessage($"[blue on gray15]⚡ {Markup.Escape(commandText)} [/]");

        }
        catch (Exception ex)
        {
            _host.AddMessage($"[red]  Failed to send command: {Markup.Escape(ex.Message)}[/]");
        }
    }
}
