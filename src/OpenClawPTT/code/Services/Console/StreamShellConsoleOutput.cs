using System;
using Spectre.Console;

namespace OpenClawPTT.Services;

/// <summary>
/// IConsoleOutput implementation that routes display methods through StreamShell's message queue
/// as Spectre markup, while keeping raw I/O (ReadKey, Write, ReadLine) on the underlying IConsole.
/// </summary>
public sealed class StreamShellConsoleOutput : IConsoleOutput, IFormattedOutput
{
    private readonly IStreamShellHost _shellHost;

    public StreamShellConsoleOutput(IStreamShellHost shellHost)
    {
        _shellHost = shellHost;
    }

    /// <summary>Gets the underlying StreamShell host for capturing formatter output.</summary>
    internal IStreamShellHost GetStreamShellHost() => _shellHost;

    void IFormattedOutput.Write(string text) => _shellHost.AddMessage(Markup.Escape(text));
    void IFormattedOutput.WriteLine() => _shellHost.AddMessage("");
    int IFormattedOutput.WindowWidth => 80;

    // ── IConsoleOutput (display → StreamShell markup) ──

    public void PrintBanner()
    {
        _shellHost.AddMessage("[cyan]  ╔═══════════════════════════════════════╗[/]");
        _shellHost.AddMessage("[cyan]  ║    🐾  OpenClaw Push-to-Talk  v1.0    ║[/]");
        _shellHost.AddMessage("[cyan]  ╚═══════════════════════════════════════╝[/]");
    }

    public void PrintHelpMenu(string hotkeyCombination, bool holdToTalk)
    {
        var mode = holdToTalk ? "Hold-to-talk" : "Toggle recording";
        _shellHost.AddMessage("[green]  ╔══════════════════════════════════════════╗[/]");
        _shellHost.AddMessage("[green]  ║  Push-to-Talk ready                      ║[/]");
        _shellHost.AddMessage("[green]  ╠══════════════════════════════════════════╣[/]");
        _shellHost.AddMessage($"[green]  ║  [{Markup.Escape(hotkeyCombination)}]  {Markup.Escape(mode)}[/]");
        _shellHost.AddMessage("[green]  ║  [Alt+R]  Reconfigure settings[/]");
        _shellHost.AddMessage("[green]  ║  [T]      Type a text message[/]");
        _shellHost.AddMessage("[green]  ║  [Q]      Quit[/]");
        _shellHost.AddMessage("[green]  ╚══════════════════════════════════════════╝[/]");
    }

    public void PrintRecordingIndicator(bool isRecording, string hotkeyCombination, bool holdToTalk)
    {
        if (!isRecording) return;
        var action = holdToTalk ? $"release {Markup.Escape(hotkeyCombination)}" : $"press {Markup.Escape(hotkeyCombination)} again";
        _shellHost.AddMessage($"[red]  ● REC — {action} to stop[/]");
    }

    public void PrintUserMessage(string text)
    {
        _shellHost.AddMessage($"[green]  You:[/] {Markup.Escape(text)}");
    }

    public void PrintSuccess(string message)
        => _shellHost.AddMessage($"[green]  ✓ {Markup.Escape(message)}[/]");

    public void PrintSuccessWordWrap(string prefix, string message, int rightMarginIndent)
        => throw new NotSupportedException("PrintSuccessWordWrap is not supported on StreamShellConsoleOutput; use ConsoleUiOutput instead");

    public void PrintWarning(string message)
        => _shellHost.AddMessage($"[yellow]  ⚠ {Markup.Escape(message)}[/]");

    public void PrintError(string message)
        => _shellHost.AddMessage($"[red]  ✗ {Markup.Escape(message)}[/]");

    public void PrintInfo(string message)
        => _shellHost.AddMessage($"[grey]  {Markup.Escape(message)}[/]");

    public void PrintInlineInfo(string message)
        => _shellHost.AddMessage($"[grey]  {Markup.Escape(message)}[/]");

    public void PrintInlineSuccess(string message)
        => _shellHost.AddMessage($"[green]{Markup.Escape(message)}[/]");

    public void PrintGatewayError(string message, string? detailCode = null, string? recommendedStep = null)
    {
        _shellHost.AddMessage($"[red]  Gateway error: {Markup.Escape(message)}[/]");
        if (detailCode != null)
            _shellHost.AddMessage($"  Detail code : {Markup.Escape(detailCode)}");
        if (recommendedStep != null)
            _shellHost.AddMessage($"  Recommended : {Markup.Escape(recommendedStep)}");
    }

    public void PrintAgentReply(string prefix, string body)
    {
        // Prefix in cyan, body in default color
        _shellHost.AddMessage($"[cyan]{Markup.Escape(prefix)}[/]{Markup.Escape(body)}");
    }

    public void PrintAgentReplyDelta(string prefix, string delta, string newlineSuffix)
        => throw new NotSupportedException("PrintAgentReplyDelta is not supported on StreamShellConsoleOutput; use ConsoleUiOutput instead");

    public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException("ReadLineAsync is not supported on StreamShellConsoleOutput; use ConsoleUiOutput instead");

    public void Log(string tag, string msg)
        => _shellHost.AddMessage($"[grey]  [{Markup.Escape(tag)}] {Markup.Escape(msg)}[/]");

    public void LogOk(string tag, string msg)
        => _shellHost.AddMessage($"[green]  [{Markup.Escape(tag)}] {Markup.Escape(msg)}[/]");

    public void LogError(string tag, string msg)
        => _shellHost.AddMessage($"[red]  [{Markup.Escape(tag)}] {Markup.Escape(msg)}[/]");
}
