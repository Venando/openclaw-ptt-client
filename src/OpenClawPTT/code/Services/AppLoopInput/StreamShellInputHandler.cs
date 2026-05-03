using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.Services;
using Spectre.Console;
using StreamShell;

namespace OpenClawPTT;

/// <summary>
/// Integrates StreamShell with the PTT client — registers StreamShell commands
/// (/quit, /reconfigure, /crew, /chat, OpenClash slash commands) and wires up
/// non-command user text input to the gateway text sender.
/// Agent settings commands (hotkey, emoji) and text composition are delegated
/// to separate classes for Single Responsibility.
/// </summary>
public sealed class StreamShellInputHandler : IDisposable
{
    private readonly IStreamShellHost _host;
    private readonly ITextMessageSender _textSender;
    private readonly IConfigurationService _configService;
    private readonly AppConfig _appConfig;
    private readonly Action _onQuit;
    private readonly AgentSettingsCommands _agentSettings;
    private readonly AgentSwitchingCommands _agentSwitching;
    private readonly TextMessageComposer _messageComposer;

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
        _agentSettings = new AgentSettingsCommands(host, configService);
        _agentSwitching = new AgentSwitchingCommands(host, textSender, appConfig);
        _messageComposer = new TextMessageComposer(host, textSender);
    }

    /// <summary>Register all commands and the UserInputSubmitted handler.</summary>
    public void Register()
    {
        // Application commands
        _host.AddCommand(new Command("quit", "Exit the application", QuitHandler));
        _host.AddCommand(new Command("reconfigure", "Run reconfiguration wizard", ReconfigureHandler));
        _host.AddCommand(new Command("crew", "List available agents", CrewHandler));
        _host.AddCommand(new Command("chat", "<name|id> Switch active agent by name or ID", ChatHandler));

        // OpenClaw tool commands (for StreamShell hint support)
        foreach (var name in OpenClawCommands.Names)
        {
            var cmdName = name; // Capture for closure
            _host.AddCommand(new Command(name, Markup.Escape(OpenClawCommands.Descriptions[name]),
                (args, named) => OpenClawCommandForwarder(cmdName, args, named)));
        }

        _host.UserInputSubmitted += OnUserInput;
    }

    public void Dispose()
    {
        _host.UserInputSubmitted -= OnUserInput;
    }

    private Task QuitHandler(string[] args, System.Collections.Generic.Dictionary<string, string> named)
    {
        _host.AddMessage("[green]  Bye![/]");
        _onQuit();
        return Task.CompletedTask;
    }

    private async Task ReconfigureHandler(string[] args, System.Collections.Generic.Dictionary<string, string> named)
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
    /// For plain text, sends it as a message (with attachment content prepended).
    /// For commands, StreamShell auto-executes via command registration — nothing to do here.
    /// </summary>
    private void OnUserInput(string input, InputType type, IReadOnlyList<Attachment> attachments)
    {
        // Commands are auto-executed by StreamShell — skip
        if (type == InputType.Command)
            return;

        // Use non-blocking send via fire-and-forget since StreamShell fires events synchronously.
        // Exceptions are caught and surfaced inside SendWithAttachmentsAsync.
        _ = Task.Run(async () =>
        {
            await _messageComposer.SendWithAttachmentsAsync(input, attachments, CancellationToken.None);
        });
    }

    private Task CrewHandler(string[] args, System.Collections.Generic.Dictionary<string, string> named)
    {
        if (args.Length > 0)
        {
            if (args[0].Equals("hotkey", System.StringComparison.OrdinalIgnoreCase))
                return _agentSettings.HandleHotkeyCommand(args.Skip(1).ToArray());
            if (args[0].Equals("emoji", System.StringComparison.OrdinalIgnoreCase))
                return _agentSettings.HandleEmojiCommand(args.Skip(1).ToArray());
        }

        return _agentSwitching.HandleCrew(args);
    }

    private Task ChatHandler(string[] args, System.Collections.Generic.Dictionary<string, string> named)
    {
        return _agentSwitching.HandleChat(args);
    }

    private Task OpenClawCommandForwarder(string commandName, string[] args, System.Collections.Generic.Dictionary<string, string> named)
    {
        return _agentSwitching.HandleOpenClawCommand(commandName, args, named);
    }
}
