using System;
using System.Text;
using Spectre.Console;

namespace OpenClawPTT.Services;

/// <summary>
/// IConsoleOutput implementation that routes display methods through StreamShell's message queue
/// as Spectre markup, while keeping raw I/O (ReadKey, Write, ReadLine) on the underlying IConsole.
/// </summary>
public sealed class StreamShellConsoleOutput : IConsoleOutput
{
    private readonly IStreamShellHost _shellHost;
    private readonly IConsole _console;

    public StreamShellConsoleOutput(IStreamShellHost shellHost, IConsole console)
    {
        _shellHost = shellHost;
        _console = console;
    }

    // ── IConsole (raw I/O stays on the underlying console) ──

    public void Write(string? text) => _console.Write(text);
    public void WriteLine(string? text = null) => _console.WriteLine(text);
    public ConsoleColor ForegroundColor
    {
        get => _console.ForegroundColor;
        set => _console.ForegroundColor = value;
    }
    public void ResetColor() => _console.ResetColor();
    public bool KeyAvailable => _console.KeyAvailable;
    public ConsoleKeyInfo ReadKey(bool intercept = false) => _console.ReadKey(intercept);
    public int WindowWidth => _console.WindowWidth;
    public Encoding OutputEncoding
    {
        get => _console.OutputEncoding;
        set => _console.OutputEncoding = value;
    }
    public bool TreatControlCAsInput
    {
        get => _console.TreatControlCAsInput;
        set => _console.TreatControlCAsInput = value;
    }
    public IAgentReplyFormatter CreateAgentReplyFormatter(string prefix, int rightMarginIndent, bool prefixAlreadyPrinted = false)
        => new AgentReplyFormatter(prefix, rightMarginIndent, prefixAlreadyPrinted);
    public IAgentReplyFormatter CreateAgentReplyFormatter(string prefix, int rightMarginIndent, bool prefixAlreadyPrinted, int consoleWidth)
        => new AgentReplyFormatter(prefix, rightMarginIndent, prefixAlreadyPrinted, consoleWidth);
    public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default)
        => _console.ReadLineAsync(cancellationToken);

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
    {
        // Word-wrap streaming: keep on raw console
        _console.ForegroundColor = ConsoleColor.Green;
        _console.Write(prefix);
        var formatter = new AgentReplyFormatter(prefix, rightMarginIndent, prefixAlreadyPrinted: true);
        formatter.ProcessDelta(message);
        formatter.Finish();
        _console.ResetColor();
    }

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
        // Complete reply — push to StreamShell as a single message
        _shellHost.AddMessage($"[cyan]{Markup.Escape(prefix)}{Markup.Escape(body)}[/]");
    }

    public void PrintAgentReplyDelta(string prefix, string delta, string newlineSuffix)
    {
        // Streaming delta — keep on raw console (StreamShell AddMessage is complete-message only)
        _console.Write(delta.Replace("\n", "\n" + newlineSuffix));
    }

    public void Log(string tag, string msg)
        => _shellHost.AddMessage($"[grey]  [{Markup.Escape(tag)}] {Markup.Escape(msg)}[/]");

    public void LogOk(string tag, string msg)
        => _shellHost.AddMessage($"[green]  [{Markup.Escape(tag)}] {Markup.Escape(msg)}[/]");

    public void LogError(string tag, string msg)
        => _shellHost.AddMessage($"[red]  [{Markup.Escape(tag)}] {Markup.Escape(msg)}[/]");
}
