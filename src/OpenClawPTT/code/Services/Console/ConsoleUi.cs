using OpenClawPTT.Services;
using Spectre.Console;

namespace OpenClawPTT;

/// <summary>
/// Central UI output facade. All display methods are static and route through
/// StreamShell for markup rendering.
/// </summary>
[Obsolete("Use IColorConsole/ColorConsole via dependency injection instead. This static facade is retained for backward compatibility.")]
public static class ConsoleUi
{
    public const string AppEmoji = "🦞";
    private static IStreamShellHost? _shellHost;
    private static IColorConsole? _instance;

    // ── Initialization ─────────────────────────────────────────

    /// <summary>
    /// Initializes the ConsoleUi facade with an IColorConsole instance.
    /// All static methods will delegate to this instance.
    /// </summary>
    public static void Initialize(IColorConsole colorConsole)
    {
        _instance = colorConsole ?? throw new ArgumentNullException(nameof(colorConsole));
    }

    // ── StreamShell bridge ─────────────────────────────────────

    /// <summary>
    /// Attach a StreamShell host. Display methods will route through it
    /// when non-null.
    /// </summary>
    [Obsolete("Use IColorConsole.GetStreamShellHost() instead.")]
    public static void SetStreamShellHost(IStreamShellHost? host) => _shellHost = host;

    /// <summary>Gets the underlying StreamShell host for capturing formatter output.</summary>
    public static IStreamShellHost? GetStreamShellHost() => _instance?.GetStreamShellHost() ?? _shellHost;

    private static void ShellMsg(string markup) => _shellHost?.AddMessage(markup);

    // ── Display methods ────────────────────────────────────────

    public static void PrintBanner()
    {
        if (_shellHost != null)
        {
            // Legacy mode: use _shellHost directly
        }
        else if (_instance != null)
        {
            _instance.PrintBanner();
            return;
        }

        // Fallback to legacy implementation (writes to console if no shell)
        ShellMsg("");
        ShellMsg("[deepskyblue3]  ╔═══════════════════════════════════════╗[/]");
        ShellMsg($"[deepskyblue3]  ║    {AppEmoji}  OpenClaw Push-to-Talk  v1.0    ║[/]");
        ShellMsg("[deepskyblue3]  ╚═══════════════════════════════════════╝[/]");
        ShellMsg("");
    }

    public static void PrintHelpMenu(AppConfig appConfig)
    {
        if (_shellHost != null)
        {
            // Legacy mode: use _shellHost directly
        }
        else if (_instance != null)
        {
            _instance.PrintHelpMenu(appConfig);
            return;
        }

        PrintAgentIntroduction(appConfig);
        ShellMsg("    type [grey]/crew [/]to list agents [grey]/chat <agent>[/] to switch");
        ShellMsg("");
    }

    public static void PrintAgentIntroduction(AppConfig appConfig)
    {
        if (_shellHost != null)
        {
            // Legacy mode: use _shellHost directly
        }
        else if (_instance != null)
        {
            _instance.PrintAgentIntroduction(appConfig);
            return;
        }

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

    /// <summary>Send a raw Spectre markup message to the StreamShell output.</summary>
    public static void PrintMarkup(string markup)
    {
        if (_shellHost != null)
        {
            ShellMsg(markup);
            return;
        }
        if (_instance != null)
        {
            _instance.PrintMarkup(markup);
            return;
        }
    }

    /// <summary>
    /// Writes a debug/log message at grey level.
    /// </summary>
    public static void PrintInfo(string message)
    {
        if (_shellHost != null)
        {
            ShellMsg($"[grey]  {Markup.Escape(message)}[/]");
            return;
        }
        if (_instance != null)
        {
            _instance.PrintInfo(message);
            return;
        }
    }

    public static void PrintRecordingIndicator(bool isRecording, string hotkeyCombination, bool holdToTalk)
    {
        if (_shellHost != null)
        {
            // Legacy mode: use _shellHost directly
        }
        else if (_instance != null)
        {
            _instance.PrintRecordingIndicator(isRecording, hotkeyCombination, holdToTalk);
            return;
        }

        if (!isRecording) return;

        var action = holdToTalk ? $"release {Markup.Escape(hotkeyCombination)}" : $"press {Markup.Escape(hotkeyCombination)} again";
        ShellMsg($"[red]  ● REC — {action} to stop[/]");
    }

    /// <summary>
    /// Display user's own text message — routes through StreamShell when active.
    /// </summary>
    public static void PrintUserMessage(string text)
    {
        if (_shellHost != null)
        {
            // Legacy mode: use _shellHost directly
        }
        else if (_instance != null)
        {
            _instance.PrintUserMessage(text);
            return;
        }
        else
        {
            return;
        }

        var streamShellCapturingConsole = new StreamShellCapturingConsole(_shellHost);
        var userMessageFormatter = new AgentReplyFormatter("", 10, prefixAlreadyPrinted: false, output: streamShellCapturingConsole);
        var prefix = "[green]  You:[/] ";
        userMessageFormatter.Reconfigure(prefix);
        userMessageFormatter.ProcessDelta(Markup.Escape(text));
        userMessageFormatter.Finish();
        streamShellCapturingConsole.FlushToStreamShell(prefix);
    }

    public static void PrintMarkupedUserMessage(string text)
    {
        if (_shellHost != null)
        {
            ShellMsg($"[green]  You:[/] {text}");
            return;
        }
        if (_instance != null)
        {
            _instance.PrintMarkupedUserMessage(text);
            return;
        }
    }

    public static void PrintSuccess(string message)
    {
        if (_shellHost != null)
        {
            ShellMsg($"[green]  ✓ {Markup.Escape(message)}[/]");
            return;
        }
        if (_instance != null)
        {
            _instance.PrintSuccess(message);
            return;
        }
    }

    public static void PrintSuccessWordWrap(string prefix, string message, int rightMarginIndent)
    {
        if (_shellHost != null)
        {
            ShellMsg($"[green]  ✓ {Markup.Escape(prefix)}{Markup.Escape(message)}[/]");
            return;
        }
        if (_instance != null)
        {
            _instance.PrintSuccessWordWrap(prefix, message, rightMarginIndent);
            return;
        }
    }

    public static void PrintWarning(string message)
    {
        if (_shellHost != null)
        {
            ShellMsg($"[yellow]  ⚠ {Markup.Escape(message)}[/]");
            return;
        }
        if (_instance != null)
        {
            _instance.PrintWarning(message);
            return;
        }
    }

    public static void PrintError(string message)
    {
        if (_shellHost != null)
        {
            ShellMsg($"[red]  ✗ {Markup.Escape(message)}[/]");
            return;
        }
        if (_instance != null)
        {
            _instance.PrintError(message);
            return;
        }
    }

    public static void PrintAgentReply(string prefix, string body)
    {
        if (_shellHost != null)
        {
            ShellMsg($"[cyan]{Markup.Escape(prefix)}[/]{Markup.Escape(body)}");
            return;
        }
        if (_instance != null)
        {
            _instance.PrintAgentReply(prefix, body);
            return;
        }
    }

    public static void PrintAgentReplyWithMarkdown(string prefix, string body)
    {
        if (_shellHost != null)
        {
            ShellMsg($"[cyan]{Markup.Escape(prefix)}[/]{body}");
            return;
        }
        if (_instance != null)
        {
            _instance.PrintAgentReplyWithMarkdown(prefix, body);
            return;
        }
    }

    public static void PrintAgentReplyDelta(string prefix, string delta, string newlineSuffix)
    {
        if (_shellHost != null)
        {
            ShellMsg(Markup.Escape(delta));
            return;
        }
        if (_instance != null)
        {
            _instance.PrintAgentReplyDelta(prefix, delta, newlineSuffix);
            return;
        }
    }

    public static void PrintAgentReplyDelta(string prefix, string delta, string newlineSuffix, AppConfig config)
    {
        if (_shellHost != null)
        {
            PrintAgentReplyDelta(prefix, delta, newlineSuffix);
            return;
        }
        if (_instance != null)
        {
            _instance.PrintAgentReplyDelta(prefix, delta, newlineSuffix, config);
            return;
        }
    }

    public static void PrintGatewayError(string message, string? detailCode = null, string? recommendedStep = null)
    {
        if (_shellHost != null)
        {
            ShellMsg($"[red]  Gateway error: {Markup.Escape(message)}[/]");
            if (detailCode != null)
                ShellMsg($"  Detail code : {Markup.Escape(detailCode)}");
            if (recommendedStep != null)
                ShellMsg($"  Recommended : {Markup.Escape(recommendedStep)}");
            return;
        }
        if (_instance != null)
        {
            _instance.PrintGatewayError(message, detailCode, recommendedStep);
            return;
        }
    }

    public static void Log(string tag, string msg)
    {
        if (_shellHost != null)
        {
            ShellMsg($"[grey]  {Markup.Escape($"[{tag}]")} {Markup.Escape(msg)}[/]");
            return;
        }
        if (_instance != null)
        {
            _instance.Log(tag, msg);
            return;
        }
    }

    public static void LogOk(string tag, string msg)
    {
        if (_shellHost != null)
        {
            ShellMsg($"[green]  {Markup.Escape($"[{tag}]")} {Markup.Escape(msg)}[/]");
            return;
        }
        if (_instance != null)
        {
            _instance.LogOk(tag, msg);
            return;
        }
    }

    public static void LogError(string tag, string msg)
    {
        if (_shellHost != null)
        {
            ShellMsg($"[red]  {Markup.Escape($"[{tag}]")} {Markup.Escape(msg)}[/]");
            return;
        }
        if (_instance != null)
        {
            _instance.LogError(tag, msg);
            return;
        }
    }
}
