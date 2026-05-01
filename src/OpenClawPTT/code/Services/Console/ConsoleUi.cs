using System;
using System.Text;
using OpenClawPTT.Services;
using Spectre.Console;

namespace OpenClawPTT;

/// <summary>
/// Central UI output facade. All display methods are static and delegate to the
/// current IConsole implementation via SetConsole().
/// When a StreamShell host is attached AND active (SetStreamShellHost called
/// AFTER config wizard), display methods route through it as Spectre markup.
/// During the config wizard phase, output always goes to raw console.
/// </summary>
public static class ConsoleUi
{
    private static IConsole _impl = new SystemConsole();
    private static IStreamShellHost? _shellHost;

    // ── StreamShell bridge ─────────────────────────────────────

    /// <summary>
    /// Attach a StreamShell host. Display methods will route through it
    /// ONLY AFTER shell becomes active (after AppBootstrapper calls UseShell).
    /// During config wizard, output always goes to raw console.
    /// </summary>
    public static void SetStreamShellHost(IStreamShellHost? host) => _shellHost = host;

    /// <summary>
    /// Called by AppBootstrapper after config wizard completes and StreamShell
    /// is about to take over the terminal. After this, display methods route
    /// through StreamShell for markup rendering.
    /// </summary>
    public static void UseShell()
    {
        // Handled by the ViaShell check below — this method exists for clarity
    }

    private static bool ViaShell => _shellHost != null;

    private static void ShellMsg(string markup) => _shellHost?.AddMessage(markup);

    // ── IConsole static surface ──────────────────────────────────

    public static void Write(string? text) => _impl.Write(text);
    public static void WriteLine(string? text = null) => _impl.WriteLine(text);
    public static ConsoleColor ForegroundColor
    {
        get => _impl.ForegroundColor;
        set => _impl.ForegroundColor = value;
    }
    public static void ResetColor() => _impl.ResetColor();
    public static bool KeyAvailable => _impl.KeyAvailable;
    public static ConsoleKeyInfo ReadKey(bool intercept = false) => _impl.ReadKey(intercept);
    public static int WindowWidth => _impl.WindowWidth;
    public static Encoding OutputEncoding
    {
        get => _impl.OutputEncoding;
        set => _impl.OutputEncoding = value;
    }
    public static bool TreatControlCAsInput
    {
        get => _impl.TreatControlCAsInput;
        set => _impl.TreatControlCAsInput = value;
    }

    public static async Task<string?> ReadLineAsync(CancellationToken cancellationToken = default)
        => await _impl.ReadLineAsync(cancellationToken);

    /// <summary>Swap the console implementation. Use a mock in tests.</summary>
    public static void SetConsole(IConsole console) => _impl = console;

    // ── Display methods ──
    // Banner always goes to raw console — must be visible before Run() takes over

    public static void PrintBanner()
    {
        // Always use raw console so the banner is visible before Run() takes over
        _impl.ForegroundColor = ConsoleColor.Cyan;
        _impl.WriteLine();
        _impl.WriteLine("  ╔═══════════════════════════════════════╗");
        _impl.WriteLine("  ║    🐾  OpenClaw Push-to-Talk  v1.0    ║");
        _impl.WriteLine("  ╚═══════════════════════════════════════╝");
        _impl.ResetColor();
        _impl.WriteLine();
    }

    public static void PrintHelpMenu(string hotkeyCombination, bool holdToTalk)
    {
        var modeDescription = holdToTalk ? "Hold-to-talk" : "Toggle recording";

        if (ViaShell)
        {
            ShellMsg("[green]  ╔══════════════════════════════════════════╗[/]");
            ShellMsg("[green]  ║  Push-to-Talk ready                      ║[/]");
            ShellMsg("[green]  ╠══════════════════════════════════════════╣[/]");
            ShellMsg($"[green]  ║  {Markup.Escape($"[{hotkeyCombination}]")}  {Markup.Escape(modeDescription)}[/]");
            ShellMsg($"[green]  ║  {Markup.Escape($"[Alt+R]")}  Reconfigure settings[/]");
            ShellMsg($"[green]  ║  {Markup.Escape($"[T]")}      Type a text message[/]");
            ShellMsg($"[green]  ║  {Markup.Escape($"[Q]")}      Quit[/]");
            ShellMsg("[green]  ╚══════════════════════════════════════════╝[/]");
            return;
        }

        _impl.ForegroundColor = ConsoleColor.Green;
        _impl.WriteLine("  ╔══════════════════════════════════════════╗");
        _impl.WriteLine("  ║  Push-to-Talk ready                      ║");
        _impl.WriteLine("  ╠══════════════════════════════════════════╣");
        _impl.WriteLine(FormatMenuLine($"[{hotkeyCombination}]", modeDescription));
        _impl.WriteLine(FormatMenuLine("[Alt+R]", "Reconfigure settings"));
        _impl.WriteLine(FormatMenuLine("[T]", "Type a text message"));
        _impl.WriteLine(FormatMenuLine("[Q]", "Quit"));
        _impl.WriteLine("  ╚══════════════════════════════════════════╝");
        _impl.ResetColor();
        _impl.WriteLine();
    }

    private static string FormatMenuLine(string leftText, string rightText)
    {
        const int totalWidth = 42;
        const int leftPadding = 2;
        const int middlePadding = 2;

        int leftLength = leftText.Length;
        int rightLength = rightText.Length;
        int totalContentLength = leftLength + middlePadding + rightLength;
        int rightPadding = totalWidth - leftPadding - totalContentLength;

        if (rightPadding < 1) rightPadding = 1;

        return $"  ║  {leftText}{new string(' ', middlePadding)}{rightText}{new string(' ', rightPadding)}║";
    }

    public static void PrintRecordingIndicator(bool isRecording, string hotkeyCombination, bool holdToTalk)
    {
        if (!isRecording) return;

        if (ViaShell)
        {
            var action = holdToTalk ? $"release {Markup.Escape(hotkeyCombination)}" : $"press {Markup.Escape(hotkeyCombination)} again";
            ShellMsg($"[red]  ● REC — {action} to stop[/]");
            return;
        }

        _impl.ForegroundColor = ConsoleColor.Red;
        _impl.WriteLine();
        if (holdToTalk)
        {
            _impl.Write($"  ● REC — release {hotkeyCombination} to stop ");
        }
        else
        {
            _impl.Write($"  ● REC — press {hotkeyCombination} again to stop ");
        }
        _impl.ResetColor();
    }

    public static void PrintSuccess(string message)
    {
        if (ViaShell)
        {
            ShellMsg($"[green]  ✓ {Markup.Escape(message)}[/]");
            return;
        }

        _impl.ForegroundColor = ConsoleColor.Green;
        _impl.Write($"  ✓ {message}");
        _impl.ResetColor();
    }

    public static void PrintSuccessWordWrap(string prefix, string message, int rightMarginIndent)
    {
        // Word-wrapped streaming: keep on raw console
        _impl.ForegroundColor = ConsoleColor.Green;
        _impl.Write(prefix);
        var formatter = new AgentReplyFormatter(prefix, rightMarginIndent, prefixAlreadyPrinted: true);
        formatter.ProcessDelta(message);
        formatter.Finish();
        _impl.ResetColor();
    }

    public static void PrintWarning(string message)
    {
        if (ViaShell)
        {
            ShellMsg($"[yellow]  ⚠ {Markup.Escape(message)}[/]");
            return;
        }

        _impl.ForegroundColor = ConsoleColor.Yellow;
        _impl.WriteLine($"  ⚠ {message}");
        _impl.ResetColor();
    }

    public static void PrintError(string message)
    {
        if (ViaShell)
        {
            ShellMsg($"[red]  ✗ {Markup.Escape(message)}[/]");
            return;
        }

        _impl.ForegroundColor = ConsoleColor.Red;
        _impl.WriteLine($"  ✗ {message}");
        _impl.ResetColor();
    }

    public static void PrintInfo(string message)
    {
        if (ViaShell)
        {
            ShellMsg($"[grey]  {Markup.Escape(message)}[/]");
            return;
        }

        _impl.ForegroundColor = ConsoleColor.DarkGray;
        _impl.WriteLine($"  {message}");
        _impl.ResetColor();
    }

    public static void PrintInlineInfo(string message)
    {
        if (ViaShell)
        {
            ShellMsg($"[grey]  {Markup.Escape(message)}[/]");
            return;
        }

        _impl.ForegroundColor = ConsoleColor.DarkGray;
        _impl.WriteLine();
        _impl.Write($"  {message}");
        _impl.ResetColor();
    }

    public static void PrintInlineSuccess(string message)
    {
        if (ViaShell)
        {
            ShellMsg($"[green]{Markup.Escape(message)}[/]");
            return;
        }

        _impl.ForegroundColor = ConsoleColor.DarkGray;
        _impl.WriteLine(message);
        _impl.ResetColor();
    }

    public static void PrintAgentReply(string prefix, string body)
    {
        if (ViaShell)
        {
            // Complete reply — push to StreamShell as a single message
            ShellMsg($"[cyan]{Markup.Escape(prefix)}{Markup.Escape(body)}[/]");
            return;
        }

        // Non-streaming fallback: keep on raw console
        _impl.WriteLine();
        _impl.ForegroundColor = ConsoleColor.Cyan;
        _impl.Write(prefix);
        _impl.ResetColor();
        _impl.WriteLine(body);
        _impl.WriteLine();
    }

    public static void PrintAgentReplyDelta(string prefix, string delta, string newlineSuffix)
    {
        // Streaming delta — keep on raw console (StreamShell AddMessage is complete-message only)
        _impl.Write(delta.Replace("\n", "\n" + newlineSuffix));
    }

    public static IAgentReplyFormatter CreateAgentReplyFormatter(string prefix, int rightMarginIndent, bool prefixAlreadyPrinted = false)
        => new AgentReplyFormatter(prefix, rightMarginIndent, prefixAlreadyPrinted);

    public static IAgentReplyFormatter CreateAgentReplyFormatter(string prefix, int rightMarginIndent, bool prefixAlreadyPrinted, int consoleWidth)
        => new AgentReplyFormatter(prefix, rightMarginIndent, prefixAlreadyPrinted, consoleWidth);

    public static void PrintAgentReplyDelta(string prefix, string delta, string newlineSuffix, AppConfig config)
    {
        if (config.EnableWordWrap)
        {
            throw new InvalidOperationException("Use CreateAgentReplyFormatter for word-wrapped streaming.");
        }
        else
        {
            PrintAgentReplyDelta(prefix, delta, newlineSuffix);
        }
    }

    public static void PrintGatewayError(string message, string? detailCode = null, string? recommendedStep = null)
    {
        if (ViaShell)
        {
            ShellMsg($"[red]  Gateway error: {Markup.Escape(message)}[/]");
            if (detailCode != null)
                ShellMsg($"  Detail code : {Markup.Escape(detailCode)}");
            if (recommendedStep != null)
                ShellMsg($"  Recommended : {Markup.Escape(recommendedStep)}");
            return;
        }

        _impl.ForegroundColor = ConsoleColor.Red;
        _impl.WriteLine($"\n  Gateway error: {message}");
        _impl.ResetColor();

        if (detailCode != null)
            _impl.WriteLine($"  Detail code : {detailCode}");
        if (recommendedStep != null)
            _impl.WriteLine($"  Recommended : {recommendedStep}");
    }

    public static void Log(string tag, string msg)
    {
        if (ViaShell)
        {
            ShellMsg($"[grey]  {Markup.Escape($"[{tag}]")} {Markup.Escape(msg)}[/]");
            return;
        }

        _impl.ForegroundColor = ConsoleColor.DarkGray;
        _impl.Write($"  [{tag}] ");
        _impl.ResetColor();
        _impl.WriteLine(msg);
    }

    public static void LogOk(string tag, string msg)
    {
        if (ViaShell)
        {
            ShellMsg($"[green]  {Markup.Escape($"[{tag}]")} {Markup.Escape(msg)}[/]");
            return;
        }

        _impl.ForegroundColor = ConsoleColor.Green;
        _impl.Write($"  [{tag}] ");
        _impl.ResetColor();
        _impl.WriteLine(msg);
    }

    public static void LogError(string tag, string msg)
    {
        if (ViaShell)
        {
            ShellMsg($"[red]  {Markup.Escape($"[{tag}]")} {Markup.Escape(msg)}[/]");
            return;
        }

        _impl.ForegroundColor = ConsoleColor.Red;
        _impl.Write($"  [{tag}] ");
        _impl.ResetColor();
        _impl.WriteLine(msg);
    }
}
