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
        _host.AddCommand(new Command("quit", "Exit the application", QuitHandler));
        _host.AddCommand(new Command("q", "Short alias for /quit", QuitHandler));
        _host.AddCommand(new Command("reconfigure", "Run reconfiguration wizard", ReconfigureHandler));
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
    /// Handles non-command user input — sends it as a text message.
    /// StreamShell fires UserInputSubmitted for all input that isn't a command.
    /// </summary>
    private async void OnUserInput(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return;

        input = input.Trim();

        // Ignore command prefixes
        if (input.StartsWith("/"))
            return;

        try
        {
            await _textSender.SendAsync(input, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _host.AddMessage($"[red]  Failed to send message: {Markup.Escape(ex.Message)}[/]");
        }
    }
}
