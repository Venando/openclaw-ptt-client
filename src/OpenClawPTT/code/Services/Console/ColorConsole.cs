using Spectre.Console;

namespace OpenClawPTT.Services;

/// <summary>
/// Default implementation of IColorConsole. Routes all output through
/// a StreamShell host for markup rendering.
/// </summary>
public sealed class ColorConsole : IColorConsole
{
    public const string AppEmoji = "🦞";
    private readonly IStreamShellHost _shellHost;

    /// <summary>
    /// Creates a new ColorConsole instance with the specified StreamShell host.
    /// </summary>
    public ColorConsole(IStreamShellHost shellHost)
    {
        _shellHost = shellHost ?? throw new ArgumentNullException(nameof(shellHost));
    }

    private void ShellMsg(string markup) => _shellHost.AddMessage(markup);

    // ── Banner and Help ────────────────────────────────────────

    /// <inheritdoc />
    public void PrintBanner()
    {
        ShellMsg("");
        ShellMsg("[deepskyblue3]  ╔═══════════════════════════════════════╗[/]");
        ShellMsg($"[deepskyblue3]  ║    {AppEmoji}  OpenClaw Push-to-Talk  v1.0    ║[/]");
        ShellMsg("[deepskyblue3]  ╚═══════════════════════════════════════╝[/]");
        ShellMsg("");
    }

    /// <inheritdoc />
    public void PrintHelpMenu(AppConfig appConfig)
    {
        PrintAgentIntroduction(appConfig);
        ShellMsg("    type [grey]/crew [/]to list agents [grey]/chat <agent>[/] to switch");
        ShellMsg("");
    }

    /// <inheritdoc />
    public void PrintAgentIntroduction(AppConfig appConfig)
    {
        if (!AgentRegistry.IsActiveAgentAvailable)
        {
            return;
        }

        var hotkeyCombination = AgentSettingsPersistence.GetPersistedHotkey(AgentRegistry.ActiveAgentId!) ?? appConfig.HotkeyCombination;
        AgentRegistry.GetActiveNameAndEmoji(out var agentName, out var emoji);
        var modeDescription = appConfig.HoldToTalk ? "Hold-to-talk" : "Toggle recording";
        var middleContent = $"   Agent: [white on gray15]{emoji} {agentName}[/] [deepskyblue3]·[/] [white on gray15]{Markup.Escape($"[{hotkeyCombination}]")}[/] [deepskyblue3]·[/] {modeDescription} [deepskyblue3]·[/] /help [deepskyblue3]·[/] /quit    ";
        var dashCount = Markup.Remove(middleContent).Length;
        var topLineStart = $"── {AppEmoji} PTT Active ─";
        var topLine = $"[deepskyblue3]╭{topLineStart}{new string('─', dashCount - topLineStart.Length)}╮[/]";
        var bottomLine = $"[deepskyblue3]╰{new string('─', dashCount)}╯[/]";
        ShellMsg("");
        ShellMsg(topLine);
        ShellMsg($"[deepskyblue3]│[/]{middleContent}[deepskyblue3]│[/]");
        ShellMsg(bottomLine);
        ShellMsg("");
    }

    // ── General Output ─────────────────────────────────────────

    /// <inheritdoc />
    public void PrintMarkup(string markup) => ShellMsg(markup);

    /// <inheritdoc />
    public void PrintUserMessage(string text)
    {
        var streamShellCapturingConsole = new StreamShellCapturingConsole(_shellHost);
        var userMessageFormatter = new AgentReplyFormatter("", 10, prefixAlreadyPrinted: false, output: streamShellCapturingConsole);
        var prefix = "[green]  You:[/] ";
        userMessageFormatter.Reconfigure(prefix);
        userMessageFormatter.ProcessDelta(Markup.Escape(text));
        userMessageFormatter.Finish();
        streamShellCapturingConsole.FlushToStreamShell(prefix);
    }

    /// <inheritdoc />
    public void PrintMarkupedUserMessage(string text)
    {
        ShellMsg($"[green]  You:[/] {text}");
    }

    // ── Status Messages ────────────────────────────────────────

    /// <inheritdoc />
    public void PrintInfo(string message)
    {
        ShellMsg($"[grey]  {Markup.Escape(message)}[/]");
    }

    /// <inheritdoc />
    public void PrintSuccess(string message)
    {
        ShellMsg($"[green]  ✓ {Markup.Escape(message)}[/]");
    }

    /// <inheritdoc />
    public void PrintSuccessWordWrap(string prefix, string message, int rightMarginIndent)
    {
        ShellMsg($"[green]  ✓ {Markup.Escape(prefix)}{Markup.Escape(message)}[/]");
    }

    /// <inheritdoc />
    public void PrintWarning(string message)
    {
        ShellMsg($"[yellow]  ⚠ {Markup.Escape(message)}[/]");
    }

    /// <inheritdoc />
    public void PrintError(string message)
    {
        ShellMsg($"[red]  ✗ {Markup.Escape(message)}[/]");
    }

    /// <inheritdoc />
    public void PrintRecordingIndicator(bool isRecording, string hotkeyCombination, bool holdToTalk)
    {
        if (!isRecording) return;

        var action = holdToTalk ? $"release {Markup.Escape(hotkeyCombination)}" : $"press {Markup.Escape(hotkeyCombination)} again";
        ShellMsg($"[red]  ● REC — {action} to stop[/]");
    }

    // ── Agent Replies ──────────────────────────────────────────

    /// <inheritdoc />
    public void PrintAgentReply(string prefix, string body)
    {
        ShellMsg($"[cyan]{Markup.Escape(prefix)}[/]{Markup.Escape(body)}");
    }

    /// <inheritdoc />
    public void PrintAgentReplyWithMarkdown(string prefix, string body)
    {
        ShellMsg($"[cyan]{Markup.Escape(prefix)}[/]{body}");
    }

    /// <inheritdoc />
    public void PrintAgentReplyDelta(string prefix, string delta, string newlineSuffix)
    {
        ShellMsg(Markup.Escape(delta));
    }

    /// <inheritdoc />
    public void PrintAgentReplyDelta(string prefix, string delta, string newlineSuffix, AppConfig config)
    {
        PrintAgentReplyDelta(prefix, delta, newlineSuffix);
    }

    // ── Gateway Errors ─────────────────────────────────────────

    /// <inheritdoc />
    public void PrintGatewayError(string message, string? detailCode = null, string? recommendedStep = null)
    {
        ShellMsg($"[red]  Gateway error: {Markup.Escape(message)}[/]");
        if (detailCode != null)
            ShellMsg($"  Detail code : {Markup.Escape(detailCode)}");
        if (recommendedStep != null)
            ShellMsg($"  Recommended : {Markup.Escape(recommendedStep)}");
    }

    // ── Logging ────────────────────────────────────────────────

    /// <inheritdoc />
    public void Log(string tag, string msg)
    {
        ShellMsg($"[grey]  {Markup.Escape($"[{tag}]")} {Markup.Escape(msg)}[/]");
    }

    /// <inheritdoc />
    public void LogOk(string tag, string msg)
    {
        ShellMsg($"[green]  {Markup.Escape($"[{tag}]")} {Markup.Escape(msg)}[/]");
    }

    /// <inheritdoc />
    public void LogError(string tag, string msg)
    {
        ShellMsg($"[red]  {Markup.Escape($"[{tag}]")} {Markup.Escape(msg)}[/]");
    }

    // ── StreamShell Access ─────────────────────────────────────

    /// <inheritdoc />
    public IStreamShellHost? GetStreamShellHost() => _shellHost;
}
