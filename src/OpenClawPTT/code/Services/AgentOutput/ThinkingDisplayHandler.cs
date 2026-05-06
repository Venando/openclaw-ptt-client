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
        _toolOutput = new ToolOutputHelper(shellHost!);
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
        string header = $"[gray93 on #333333]  💭 Thinking[/]  ";
        _shellHost?.AddMessage(header);
        _shellHost?.AddMessage("");
    }

    /// <summary>
    /// Mode 3: Show emoji header + first N lines of thinking, tool-output style.
    /// Reuses <see cref="IToolOutput.PrintTruncated"/> for consistent rendering.
    /// </summary>
    private void DisplayFirstNLines(string thinking)
    {
        string emojiHeader = $"[gray93 on #333333]  💭 Thinking[/]  ";
        _toolOutput.Start(emojiHeader);
        _toolOutput.PrintTruncated(
            thinking,
            continuationPrefix: "",
            rightMarginIndent: _config.RightMarginIndent,
            maxRows: _config.ThinkingPreviewLines);
        _toolOutput.PrintLine("");
        _toolOutput.Finish();
        _toolOutput.Flush();
    }

    /// <summary>
    /// Mode 4: Show all thinking through the agent-reply formatting pipeline
    /// (<see cref="AgentReplyFormatter"/> + <see cref="StreamShellCapturingConsole"/>),
    /// enabling word-wrapped, Spectre-markup-rendered output. Ready for streaming
    /// when thinking delta events are added.
    /// </summary>
    private void DisplayFull(string thinking)
    {
        if (_shellHost == null)
        {
            // No StreamShell available — fall back to simple console output
            // (same as Emoji mode behavior without StreamShell)
            return;
        }

        var capturingConsole = new StreamShellCapturingConsole(_shellHost);
        string prefix = $"  💭 Thinking: ";
        var formatter = new AgentReplyFormatter(
            prefix,
            _config.RightMarginIndent,
            prefixAlreadyPrinted: false,
            output: capturingConsole);

        // Apply the same markdown-to-Spectre conversion used for agent replies
        var markdownBody = MarkdownToSpectreConverter.Convert(thinking);
        formatter.ProcessMarkupDelta(markdownBody);
        formatter.Finish();

        capturingConsole.FlushToStreamShell($"[cyan]{Markup.Escape(prefix)}[/]");
    }
}
