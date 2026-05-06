using Spectre.Console;

namespace OpenClawPTT.Services;

public static class TextWidth
{
    /// <summary>
    /// Returns the visual display width of a single character.
    /// CJK / fullwidth characters have width 2; everything else width 1.
    /// </summary>
    public static int GetVisualWidth(char c)
    {
        if ((c >= 0x1100 && c <= 0x115f) || // Hangul Jamo
            (c >= 0x2e80 && c <= 0xa4cf && c != 0x303f) || // CJK Radicals, Symbols, Kanji
            (c >= 0xac00 && c <= 0xd7a3) || // Hangul Syllables
            (c >= 0xf900 && c <= 0xfaff) || // CJK Compatibility Ideographs
            (c >= 0xfe10 && c <= 0xfe19) || // Vertical forms
            (c >= 0xfe30 && c <= 0xfe6f) || // CJK Compatibility Forms
            (c >= 0xff00 && c <= 0xff60) || // Fullwidth Forms
            (c >= 0xffe0 && c <= 0xffe6))   // Fullwidth Symbols
        {
            return 2;
        }
        return 1;
    }

    /// <summary>
    /// Returns the total visual display width of a string.
    /// </summary>
    public static int GetVisualWidth(string input)
    {
        int width = 0;
        foreach (char c in input)
            width += GetVisualWidth(c);
        return width;
    }

    /// <summary>
    /// Splits text into lines each not exceeding <paramref name="maxWidth"/>
    /// visual columns. Breaks at word boundaries (spaces) when possible, or
    /// at the exact column limit otherwise.
    /// </summary>
    public static List<string> WrapToVisualWidth(string text, int maxWidth)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text) || maxWidth <= 0)
        {
            if (!string.IsNullOrEmpty(text))
                lines.Add(text);
            return lines;
        }

        int i = 0;
        while (i < text.Length)
        {
            if (text[i] == '\n')
            {
                lines.Add("");
                i++;
                continue;
            }

            int lineStart = i;
            int visualWidth = 0;

            // Find the longest substring that fits within maxWidth
            while (i < text.Length && text[i] != '\n')
            {
                int cw = GetVisualWidth(text[i]);
                if (visualWidth + cw > maxWidth)
                    break;
                visualWidth += cw;
                i++;
            }

            int lineEnd = i;

            // If we broke mid-word, try to find the last space for a cleaner break
            if (i < text.Length && text[i] != '\n' && lineEnd > lineStart)
            {
                int lastSpace = text.LastIndexOf(' ', lineEnd - 1, lineEnd - lineStart);
                if (lastSpace > lineStart)
                {
                    lineEnd = lastSpace;
                    i = lastSpace + 1; // skip the space
                }
            }

            lines.Add(text[lineStart..lineEnd]);
        }

        return lines;
    }
}

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
        string header = $"[gray93 on #333333]  💭 Thinking[/] ";
        _shellHost?.AddMessage(header);
        _shellHost?.AddMessage("");
    }

    /// <summary>
    /// Mode 3: Show emoji header + first N lines of thinking, tool-output style.
    /// Breaks thinking text into lines that fit within the console width,
    /// accounting for full-width and half-width character differences,
    /// and respecting <see cref="AppConfig.ThinkingPreviewLines"/>.
    /// </summary>
    private void DisplayFirstNLines(string thinking)
    {
        string emojiHeader = $"[gray93 on #333333]  💭 Thinking[/] ";
        _toolOutput.Start(emojiHeader);

        if (!string.IsNullOrEmpty(thinking))
        {
            // Available width: min of 80 and console width minus overhead
            int consoleWidth = 80;
            try { consoleWidth = Console.WindowWidth; } catch { }

            // Estimate prefix visual width (~5 chars for "  💭 ", variable for rest)
            int prefixWidth = TextWidth.GetVisualWidth("  💭 Thinking ");
            int maxLineWidth = Math.Min(79, consoleWidth - prefixWidth - _config.RightMarginIndent);
            if (maxLineWidth < 20) maxLineWidth = 79; // fallback

            var wrappedLines = TextWidth.WrapToVisualWidth(thinking, maxLineWidth);
            int totalVisualChars = TextWidth.GetVisualWidth(thinking);

            var displayLines = wrappedLines.Take(_config.ThinkingPreviewLines).ToList();
            bool hasMore = wrappedLines.Count > _config.ThinkingPreviewLines;

            foreach (var line in displayLines)
            {
                _toolOutput.PrintLine(line, ConsoleColor.Gray);
            }

            if (hasMore)
            {
                // Count remaining visual characters
                int remainingStart = 0;
                for (int i = 0; i < _config.ThinkingPreviewLines && i < wrappedLines.Count; i++)
                    remainingStart += wrappedLines[i].Length;
                int remainingVisualWidth = TextWidth.GetVisualWidth(thinking[remainingStart..]);

                _toolOutput.PrintLine($"... ({remainingVisualWidth} more chars)", ConsoleColor.DarkGray);
            }
        }

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
