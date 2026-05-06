using Spectre.Console;

namespace OpenClawPTT.Services;

/// <summary>
/// Handles display of agent "thinking" content based on the configured
/// <see cref="ThinkingMode"/>.
///
/// Modes 2 (Emoji) and 3 (FirstNLines) render tool-output-style headers
/// via <see cref="IToolOutput"/>, reusing the same pipeline as tool calls.
/// Mode 4 (Full) renders through <see cref="AgentReplyFormatter"/> like
/// agent replies, supporting future streaming.
/// </summary>
public sealed class ThinkingDisplayHandler
{
    private readonly AppConfig _config;
    private readonly IStreamShellHost? _shellHost;
    private readonly ToolOutputHelper _toolOutput;

    public ThinkingDisplayHandler(AppConfig config, IStreamShellHost? shellHost)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _shellHost = shellHost;
        _toolOutput = new ToolOutputHelper(shellHost!, _config.RightMarginIndent);
    }

    public void DisplayThinking(string thinking)
    {
        switch (_config.ThinkingDisplayMode)
        {
            case ThinkingMode.None:
                return;

            case ThinkingMode.Emoji:
                DisplayEmojiOnly();
                return;

            case ThinkingMode.FirstNLines:
                DisplayFirstNLines(thinking);
                return;

            case ThinkingMode.Full:
                DisplayFull(thinking);
                return;
        }
    }

    /// <summary>
    /// Mode 2: Show emoji header with no body — like a tool call that had empty args.
    /// </summary>
    private void DisplayEmojiOnly()
    {
        string header = $"[gray93 on #333333]  💭 Thinking[/] ";
        _shellHost?.AddMessage(header);
        _shellHost?.AddMessage("");
    }

    /// <summary>
    /// Mode 3: Show emoji header + first N lines of thinking, tool-output style.
    /// Reuses <see cref="IToolOutput.PrintTruncated"/> for consistent rendering.
    /// </summary>
    private void DisplayFirstNLines(string thinking)
    {
        string emojiHeader = $"[gray93 on #333333]  💭 Thinking[/] ";
        _toolOutput.Start(emojiHeader);
        _toolOutput.PrintTruncated(
            thinking,
            continuationPrefix: "",
            rightMarginIndent: _config.RightMarginIndent,
            color: ConsoleColor.Gray,
            maxRows: _config.ThinkingPreviewLines);
        _toolOutput.PrintLine("", ConsoleColor.Gray);
        _toolOutput.Finish();
        _toolOutput.Flush();
    }

    /// <summary>
    /// Mode 4: Show all thinking through the agent-reply formatting pipeline
    /// (<see cref="AgentReplyFormatter"/> + <see cref="StreamShellCapturingConsole"/>),
    /// enabling word-wrapped output. The text is rendered in gray to match tool
    /// output styling. Ready for future streaming support.
    /// </summary>
    private void DisplayFull(string thinking)
    {
        if (_shellHost == null)
            return;

        var capturingConsole = new StreamShellCapturingConsole(_shellHost);
        string prefix = $"  💭 Thinking: ";
        var formatter = new AgentReplyFormatter(
            prefix,
            _config.RightMarginIndent,
            prefixAlreadyPrinted: false,
            output: capturingConsole);

        // Render thinking text in gray to match tool output style.
        // Escape brackets so Spectre doesn't interpret them as markup tags,
        // then wrap in gray color tag.
        var escaped = Markup.Escape(thinking);
        formatter.ProcessMarkupDelta($"[gray]{escaped}[/]");
        formatter.Finish();

        capturingConsole.FlushToStreamShell($"[gray93 on #333333]{Markup.Escape(prefix.TrimEnd())}[/]");
    }
}
