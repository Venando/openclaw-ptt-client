using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.Services;
using Spectre.Console;
using StreamShell;

namespace OpenClawPTT;

/// <summary>
/// Registers StreamShell commands (/quit, /reconfigure) and wires up
/// non-command user text input to the text sender.
/// </summary>
public sealed class AppShellCommands : IDisposable
{
    private readonly IStreamShellHost _host;
    private readonly ITextMessageSender _textSender;
    private readonly IConfigurationService _configService;
    private readonly Action _onQuit;

    public AppShellCommands(
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
        _host.AddCommand(new Command("q", "Short alias for /quit", QuitHandler));
        _host.AddCommand(new Command("reconfigure", "Run reconfiguration wizard", ReconfigureHandler));

        // OpenClaw tool commands (for StreamShell hint support)
        foreach (var name in OpenClawCommands.Names)
        {
            var cmdName = name; // Capture for closure
            _host.AddCommand(new Command(name, $"Send {name} to OpenClaw",
                (args, named) => OpenClawToolHandler(cmdName, args, named)));
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
    private async void OnUserInput(string input, StreamShell.InputType type, System.Collections.Generic.IReadOnlyList<StreamShell.Attachment> attachments)
    {
        // Commands are auto-executed by StreamShell — skip
        if (type == StreamShell.InputType.Command)
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
    private async Task OpenClawToolHandler(string commandName, string[] args, Dictionary<string, string> named)
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
            _host.AddMessage($"[cyan]⚡[/] {Markup.Escape(commandText)}");
        }
        catch (Exception ex)
        {
            _host.AddMessage($"[red]  Failed to send command: {Markup.Escape(ex.Message)}[/]");
        }
    }
}
