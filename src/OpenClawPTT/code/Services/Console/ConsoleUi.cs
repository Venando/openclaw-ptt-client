using OpenClawPTT.Services;
using Spectre.Console;

namespace OpenClawPTT;

/// <summary>
/// Central UI output facade. All display methods are static and route through
/// StreamShell for markup rendering.
/// </summary>
public static class ConsoleUi
{
    public const string AppEmoji = "🦞";
    private static IStreamShellHost? _shellHost;

    // ── StreamShell bridge ─────────────────────────────────────

    /// <summary>
    /// Attach a StreamShell host. Display methods will route through it
    /// when non-null.
    /// </summary>
    public static void SetStreamShellHost(IStreamShellHost? host) => _shellHost = host;

    /// <summary>Gets the underlying StreamShell host for capturing formatter output.</summary>
    public static IStreamShellHost? GetStreamShellHost() => _shellHost;

    private static void ShellMsg(string markup) => _shellHost?.AddMessage(markup);

    // ── Display methods ──

    public static void PrintBanner()
    {
        ShellMsg("");
        ShellMsg("[deepskyblue3]  ╔═══════════════════════════════════════╗[/]");
        ShellMsg($"[deepskyblue3]  ║    {AppEmoji}  OpenClaw Push-to-Talk  v1.0    ║[/]");
        ShellMsg("[deepskyblue3]  ╚═══════════════════════════════════════╝[/]");
        ShellMsg("");
    }

    public static void PrintHelpMenu(AppConfig appConfig)
    {
        PrintAgentIntroduction(appConfig);
        ShellMsg("    type [grey]/crew [/]to list agents [grey]/chat <agent>[/] to switch");
        ShellMsg("");
    }

    public static void PrintAgentIntroduction(AppConfig appConfig)
    {
        if (!AgentRegistry.IsActiveAgentAvailable)
        {
            //TODO throw some error?
            return;
        }

        var hotkeyCombination = AgentRegistry.GetPersistedHotkey(AgentRegistry.ActiveAgentId!) ?? appConfig.HotkeyCombination;
        AgentRegistry.GetActiveNameAndEmoji(out var agentName, out var emoji);
        var modeDescription = appConfig.HoldToTalk ? "Hold-to-talk" : "Toggle recording";
        var middleContent = $"   Agent: [white on gray15]{emoji} {agentName}[/] [deepskyblue3]·[/] [white on gray15]{Markup.Escape($"[{hotkeyCombination}]")}[/] [deepskyblue3]·[/] {modeDescription} [deepskyblue3]·[/] /help [deepskyblue3]·[/] /quit    ";
        var dashCount = Markup.Remove(middleContent).Length; // +4 for the leading/trailing spaces and │ padding
        var topLineStart = $"── {AppEmoji} PTT Active ─";
        var topLine = $"[deepskyblue3]╭{topLineStart}{new string('─', dashCount - topLineStart.Length)}╮[/]";
        var bottomLine = $"[deepskyblue3]╰{new string('─', dashCount)}╯[/]";
        ShellMsg("");
        ShellMsg(topLine);
        ShellMsg($"[deepskyblue3]│[/]{middleContent}[deepskyblue3]│[/]");
        ShellMsg(bottomLine);
        ShellMsg("");
    }

    /// <summary>Send a raw Spectre markup message to the StreamShell output.</summary>
    public static void PrintMarkup(string markup) => ShellMsg(markup);

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

    public static void PrintMarkupedUserMessage(string text)
    {
        ShellMsg($"[green]  You:[/] {text}");
    }

    public static void PrintSuccess(string message)
    {
        ShellMsg($"[green]  ✓ {Markup.Escape(message)}[/]");
    }

    public static void PrintSuccessWordWrap(string prefix, string message, int rightMarginIndent)
    {
        ShellMsg($"[green]  ✓ {Markup.Escape(prefix)}{Markup.Escape(message)}[/]");
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

    public static void PrintAgentReply(string prefix, string body)
    {
        // Prefix in cyan, body in default color
        ShellMsg($"[cyan]{Markup.Escape(prefix)}[/]{Markup.Escape(body)}");
    }

    public static void PrintAgentReplyWithMarkdown(string prefix, string body)
    {
        // Prefix in cyan, body in default color
        ShellMsg($"[cyan]{Markup.Escape(prefix)}[/]{body}");
    }

    public static void PrintAgentReplyDelta(string prefix, string delta, string newlineSuffix)
    {
        // Streaming delta — push to StreamShell as a complete message
        ShellMsg(Markup.Escape(delta));
    }

    public static void PrintAgentReplyDelta(string prefix, string delta, string newlineSuffix, AppConfig config)
    {
        PrintAgentReplyDelta(prefix, delta, newlineSuffix);
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
}
