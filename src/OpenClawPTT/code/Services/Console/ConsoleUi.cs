using OpenClawPTT.Services;
using Spectre.Console;

namespace OpenClawPTT;

/// <summary>
/// Central UI output facade. All display methods are static and route through
/// StreamShell for markup rendering when a StreamShell host is attached.
/// Methods that produce streaming content (word-wrap, delta) write to
/// System.Console directly.
/// </summary>
public static class ConsoleUi
{
    private static IStreamShellHost? _shellHost;

    // ── StreamShell bridge ─────────────────────────────────────

    /// <summary>
    /// Attach a StreamShell host. Display methods will route through it
    /// when non-null.
    /// </summary>
    public static void SetStreamShellHost(IStreamShellHost? host) => _shellHost = host;

    private static bool ViaShell => _shellHost != null;

    private static void ShellMsg(string markup) => _shellHost?.AddMessage(markup);

    // ── Display methods ──

    public static void PrintBanner()
    {
        ShellMsg("");
        ShellMsg("[cyan]  ╔═══════════════════════════════════════╗[/]");
        ShellMsg("[cyan]  ║    🐾  OpenClaw Push-to-Talk  v1.0    ║[/]");
        ShellMsg("[cyan]  ╚═══════════════════════════════════════╝[/]");
        ShellMsg("");
    }

    public static void PrintHelpMenu(string hotkeyCombination, bool holdToTalk)
    {
        var modeDescription = holdToTalk ? "Hold-to-talk" : "Toggle recording";

        ShellMsg("[green]  ╔══════════════════════════════════════════╗[/]");
        ShellMsg("[green]  ║  Push-to-Talk ready                      ║[/]");
        ShellMsg("[green]  ╠══════════════════════════════════════════╣[/]");
        ShellMsg($"[green]  ║  {Markup.Escape($"[{hotkeyCombination}]")}  {Markup.Escape(modeDescription)}[/]");
        ShellMsg($"[green]  ║  {Markup.Escape($"[Alt+R]")}  Reconfigure settings[/]");
        ShellMsg($"[green]  ║  {Markup.Escape($"[T]")}      Type a text message[/]");
        ShellMsg($"[green]  ║  {Markup.Escape($"[Q]")}      Quit[/]");
        ShellMsg("[green]  ╚══════════════════════════════════════════╝[/]");
    }

    public static void PrintRecordingIndicator(bool isRecording, string hotkeyCombination, bool holdToTalk)
    {
        if (!isRecording) return;

        var action = holdToTalk ? $"release {Markup.Escape(hotkeyCombination)}" : $"press {Markup.Escape(hotkeyCombination)} again";
        ShellMsg($"[red]  ● REC — {action} to stop[/]");
    }

    /// <summary>
    /// Display user's own text message — routes through StreamShell when active.
    /// </summary>
    public static void PrintUserMessage(string text)
    {
        ShellMsg($"[green]  You:[/] {Markup.Escape(text)}");
    }

    public static void PrintSuccess(string message)
    {
        ShellMsg($"[green]  ✓ {Markup.Escape(message)}[/]");
    }

    public static void PrintSuccessWordWrap(string prefix, string message, int rightMarginIndent)
    {
        // Word-wrapped streaming: keep on raw console
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write(prefix);
        var formatter = new AgentReplyFormatter(prefix, rightMarginIndent, prefixAlreadyPrinted: true, output: new ConsoleFormattedOutput());
        formatter.ProcessDelta(message);
        formatter.Finish();
        Console.ResetColor();
    }

    public static void PrintWarning(string message)
    {
        ShellMsg($"[yellow]  ⚠ {Markup.Escape(message)}[/]");
    }

    public static void PrintError(string message)
    {
        ShellMsg($"[red]  ✗ {Markup.Escape(message)}[/]");
    }

    public static void PrintInfo(string message)
    {
        ShellMsg($"[grey]  {Markup.Escape(message)}[/]");
    }

    public static void PrintInlineInfo(string message)
    {
        ShellMsg($"[grey]  {Markup.Escape(message)}[/]");
    }

    public static void PrintInlineSuccess(string message)
    {
        ShellMsg($"[green]{Markup.Escape(message)}[/]");
    }

    public static void PrintAgentReply(string prefix, string body)
    {
        if (ViaShell)
        {
            // Prefix in cyan, body in default color
            ShellMsg($"[cyan]{Markup.Escape(prefix)}[/]{Markup.Escape(body)}");
            return;
        }

        // Non-streaming fallback: keep on raw console
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write(prefix);
        Console.ResetColor();
        Console.WriteLine(body);
        Console.WriteLine();
    }

    public static void PrintAgentReplyDelta(string prefix, string delta, string newlineSuffix)
    {
        // Streaming delta — keep on raw console (StreamShell AddMessage is complete-message only)
        Console.Write(delta.Replace("\n", "\n" + newlineSuffix));
    }

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
        ShellMsg($"[red]  Gateway error: {Markup.Escape(message)}[/]");
        if (detailCode != null)
            ShellMsg($"  Detail code : {Markup.Escape(detailCode)}");
        if (recommendedStep != null)
            ShellMsg($"  Recommended : {Markup.Escape(recommendedStep)}");
    }

    public static void Log(string tag, string msg)
    {
        ShellMsg($"[grey]  {Markup.Escape($"[{tag}]")} {Markup.Escape(msg)}[/]");
    }

    public static void LogOk(string tag, string msg)
    {
        ShellMsg($"[green]  {Markup.Escape($"[{tag}]")} {Markup.Escape(msg)}[/]");
    }

    public static void LogError(string tag, string msg)
    {
        ShellMsg($"[red]  {Markup.Escape($"[{tag}]")} {Markup.Escape(msg)}[/]");
    }

    /// <summary>
    /// Minimal <see cref="IFormattedOutput"/> that writes directly to <see cref="System.Console"/>.
    /// </summary>
    private sealed class ConsoleFormattedOutput : IFormattedOutput
    {
        public void Write(string text) => Console.Write(text);
        public void WriteLine() => Console.WriteLine();
        public int WindowWidth => Console.WindowWidth;
    }
}
