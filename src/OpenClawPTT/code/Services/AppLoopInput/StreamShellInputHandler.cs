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
    private readonly IGatewayService _gatewayService;
    private readonly IConfigurationService _configService;
    private readonly IDirectLlmService? _directLlmService;
    private readonly AudioResponseHandler? _audioResponseHandler;
    private readonly AppConfig _appConfig;
    private readonly Action _onQuit;
    private readonly AgentSettingsCommands _agentSettings;
    private readonly AgentSwitchingCommands _agentSwitching;
    private readonly TextMessageComposer _messageComposer;

    public StreamShellInputHandler(
        IStreamShellHost host,
        ITextMessageSender textSender,
        IGatewayService gatewayService,
        IConfigurationService configService,
        AppConfig appConfig,
        Action onQuit,
        IDirectLlmService? directLlmService = null,
        AudioResponseHandler? audioResponseHandler = null)
    {
        _host = host;
        _textSender = textSender;
        _gatewayService = gatewayService;
        _configService = configService;
        _onQuit = onQuit;
        _appConfig = appConfig;
        _directLlmService = directLlmService;
        _audioResponseHandler = audioResponseHandler;
        _agentSettings = new AgentSettingsCommands(host, configService);
        _agentSwitching = new AgentSwitchingCommands(host, textSender, gatewayService, appConfig);
        _messageComposer = new TextMessageComposer(host, textSender);
    }

    /// <summary>Register all commands and the UserInputSubmitted handler.</summary>
    public async Task RegisterAsync()
    {
        // Application commands
        _host.AddCommand(new Command("quit", "Exit the application", QuitHandler));
        _host.AddCommand(new Command("reconfigure", "Run reconfiguration wizard", ReconfigureHandler));
        _host.AddCommand(new Command("crew", "List available agents", CrewHandler));
        _host.AddCommand(new Command("chat", "<name|id> Switch active agent by name or ID", ChatHandler));

        // Direct LLM commands (bypasses agent)
        _host.AddCommand(new Command("llm", "<message> Send message directly to configured LLM (or: url, model, api, token, status)", LlmHandler));

        // OpenClaw tool commands (for StreamShell hint support)
        foreach (var name in OpenClawCommands.Names)
        {
            var cmdName = name; // Capture for closure
            _host.AddCommand(new Command(name, Markup.Escape(OpenClawCommands.Descriptions[name]),
                (args, named) => OpenClawCommandForwarder(cmdName, args, named)));
        }

        _host.UserInputSubmitted += OnUserInput;

        // Fetch initial session history after commands are registered
        var sessionKey = AgentRegistry.ActiveSessionKey;
        if (sessionKey != null)
            await _agentSwitching.PrintSessionHistory(sessionKey);
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

        _messageComposer.TryToComposeMessage(input, attachments, out string? composedMessage);

        // Use non-blocking send via fire-and-forget since StreamShell fires events synchronously.
        // Exceptions are caught and surfaced inside SendWithAttachmentsAsync.
        _ = Task.Run(async () =>
        {
            await _messageComposer.SendWithAttachmentsAsync(composedMessage!, CancellationToken.None);
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

    private async Task LlmHandler(string[] args, System.Collections.Generic.Dictionary<string, string> named)
    {
        if (args.Length == 0)
        {
            _host.AddMessage("[yellow]  Usage: /llm <message> or /llm url|model|api|token|status <value>[/]");
            return;
        }

        var subcommand = args[0].ToLowerInvariant();
        var remainingArgs = args.Skip(1).ToArray();
        var value = string.Join(" ", remainingArgs);

        switch (subcommand)
        {
            case "url":
                if (string.IsNullOrWhiteSpace(value))
                {
                    _host.AddMessage($"[grey]  Current URL: {_appConfig.DirectLlmUrl ?? "(not set)"}[/]");
                    _host.AddMessage("[yellow]  Usage: /llm url <url>[/]");
                }
                else
                {
                    _appConfig.DirectLlmUrl = value;
                    _configService.Save(_appConfig);
                    _host.AddMessage($"[green]  LLM URL set to: {value}[/]");
                }
                return;

            case "model":
                if (string.IsNullOrWhiteSpace(value))
                {
                    _host.AddMessage($"[grey]  Current model: {_appConfig.DirectLlmModelName ?? "(not set)"}[/]");
                    _host.AddMessage("[yellow]  Usage: /llm model <model-name>[/]");
                }
                else
                {
                    _appConfig.DirectLlmModelName = value;
                    _configService.Save(_appConfig);
                    _host.AddMessage($"[green]  LLM model set to: {value}[/]");
                }
                return;

            case "api":
                if (string.IsNullOrWhiteSpace(value))
                {
                    _host.AddMessage($"[grey]  Current API type: {_appConfig.DirectLlmApiType}[/]");
                    _host.AddMessage("[yellow]  Usage: /llm api <openai-completions|anthropic-messages>[/]");
                }
                else if (value != "openai-completions" && value != "anthropic-messages")
                {
                    _host.AddMessage("[red]  Invalid API type. Use: openai-completions or anthropic-messages[/]");
                }
                else
                {
                    _appConfig.DirectLlmApiType = value;
                    _configService.Save(_appConfig);
                    _host.AddMessage($"[green]  LLM API type set to: {value}[/]");
                }
                return;

            case "token":
                if (string.IsNullOrWhiteSpace(value))
                {
                    var tokenDisplay = string.IsNullOrEmpty(_appConfig.DirectLlmToken) 
                        ? "(not set)" 
                        : "***" + _appConfig.DirectLlmToken[^Math.Min(4, _appConfig.DirectLlmToken.Length)..];
                    _host.AddMessage($"[grey]  Current token: {tokenDisplay}[/]");
                    _host.AddMessage("[yellow]  Usage: /llm token <api-key>[/]");
                }
                else
                {
                    _appConfig.DirectLlmToken = value;
                    _configService.Save(_appConfig);
                    _host.AddMessage("[green]  LLM token updated.[/]");
                }
                return;

            case "status":
                _host.AddMessage("[cyan]  Direct LLM Configuration:[/]");
                _host.AddMessage($"    URL: {_appConfig.DirectLlmUrl ?? "(not set)"}");
                _host.AddMessage($"    Model: {_appConfig.DirectLlmModelName ?? "(not set)"}");
                _host.AddMessage($"    API Type: {_appConfig.DirectLlmApiType}");
                var tokenStatus = string.IsNullOrEmpty(_appConfig.DirectLlmToken) ? "(not set)" : "(set)";
                _host.AddMessage($"    Token: {tokenStatus}");
                var configured = _directLlmService?.IsConfigured == true ? "[green]Yes[/]" : "[red]No[/]";
                _host.AddMessage($"    Configured: {configured}");
                return;

            default:
                // Treat as a message to send to LLM
                await SendToLlmAsync(args);
                return;
        }
    }

    private async Task SendToLlmAsync(string[] args)
    {
        if (_directLlmService == null)
        {
            _host.AddMessage("[yellow]  Direct LLM is not available.[/]");
            return;
        }

        if (!_directLlmService.IsConfigured)
        {
            _host.AddMessage("[yellow]  Direct LLM is not configured. Use /llm url, /llm model to configure.[/]");
            _host.AddMessage("[grey]  Or run /llm status to check current configuration.[/]");
            return;
        }

        var message = string.Join(" ", args);
        if (string.IsNullOrWhiteSpace(message))
        {
            _host.AddMessage("[yellow]  Usage: /llm <your message>[/]");
            return;
        }

        _host.AddMessage($"[grey]  Sending to LLM ({_appConfig.DirectLlmModelName})...[/]");

        try
        {
            var response = await _directLlmService.SendAsync(message, CancellationToken.None);

            // Display response
            _host.AddMessage("[cyan]  LLM Response:[/]");
            _host.AddMessage($"  {Markup.Escape(response)}");

            // Play TTS for the response
            if (_audioResponseHandler != null)
            {
                await _audioResponseHandler.HandleAudioMarkerAsync(response, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _host.AddMessage($"[red]  LLM request failed: {Markup.Escape(ex.Message)}[/]");
        }
    }

    private Task OpenClawCommandForwarder(string commandName, string[] args, System.Collections.Generic.Dictionary<string, string> named)
    {
        return _agentSwitching.HandleOpenClawCommand(commandName, args, named);
    }
}
