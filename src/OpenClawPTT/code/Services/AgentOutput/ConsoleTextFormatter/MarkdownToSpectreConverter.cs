using System;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Converts a Markdown string (.md) into an equivalent Spectre.Console markup string.
/// </summary>
/// <remarks>
/// Supported Markdown constructs:
///   Headings        # H1  ## H2  ### H3+
///   Bold            **text**  __text__
///   Italic          *text*  _text_
///   Bold+Italic     ***text***
///   Strikethrough   ~~text~~
///   Inline code     `code`
///   Links           [label](url)
///   Blockquotes     > text
///   Thematic break  --- or *** or ___ (on its own line)
///
/// Unsupported (passed through as plain escaped text):
///   Tables, task lists, footnotes, HTML blocks, images.
/// </remarks>
public static class MarkdownToSpectreConverter
{
    // ── Inline patterns (applied in order — order matters) ──────────────────

    // Bold + italic must come before bold and italic individually.
    private static readonly Regex BoldItalicStars = new(@"\*\*\*(.+?)\*\*\*", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex BoldStars = new(@"\*\*(.+?)\*\*", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex BoldUnderscores = new(@"__(.+?)__", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex ItalicStars = new(@"\*(.+?)\*", RegexOptions.Compiled | RegexOptions.Singleline);
    // Underscore-italic: only match when surrounded by word boundaries to avoid
    // false positives inside snake_case identifiers.
    private static readonly Regex ItalicUnderscores = new(@"(?<!\w)_(.+?)_(?!\w)", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex Strikethrough = new(@"~~(.+?)~~", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex InlineCode = new(@"`(.+?)`", RegexOptions.Compiled | RegexOptions.Singleline);
    // Markdown link: [label](url)
    private static readonly Regex Link = new(@"\[([^\]]+)\]\(([^)]+)\)", RegexOptions.Compiled);

    // ── Block patterns (applied per-line) ────────────────────────────────────

    private static readonly Regex HeadingPattern = new(@"^(#{1,6})\s+(.*)", RegexOptions.Compiled);
    private static readonly Regex BlockquotePattern = new(@"^>\s?(.*)", RegexOptions.Compiled);
    private static readonly Regex HrPattern = new(@"^(\*{3,}|-{3,}|_{3,})\s*$", RegexOptions.Compiled);
    // Fenced code block delimiter: ``` optionally followed by a language name.
    private static readonly Regex FencePattern = new(@"^```", RegexOptions.Compiled);

    /// <summary>
    /// Converts <paramref name="markdown"/> to a Spectre.Console markup string.
    /// </summary>
    public static string Convert(string markdown)
    {
        if (markdown is null) throw new ArgumentNullException(nameof(markdown));

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var result = new StringBuilder();

        bool inFencedBlock = false;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];

            // ── Fenced code block ────────────────────────────────────────────
            if (FencePattern.IsMatch(line))
            {
                inFencedBlock = !inFencedBlock;
                // Don't emit the fence delimiter itself.
                if (i < lines.Length - 1)
                    result.MyAppendLine();
                continue;
            }

            if (inFencedBlock)
            {
                // Emit code lines verbatim (but escape brackets so Spectre
                // doesn't try to interpret them as markup tags).
                result.MyAppendLine($"[dim]{EscapeBrackets(line)}[/]");
                continue;
            }

            // ── Thematic break (--- / *** / ___) ────────────────────────────
            if (HrPattern.IsMatch(line))
            {
                result.MyAppendLine("[dim]────────────────────────────────────────[/]");
                continue;
            }

            // ── Headings ─────────────────────────────────────────────────────
            var headingMatch = HeadingPattern.Match(line);
            if (headingMatch.Success)
            {
                int level = headingMatch.Groups[1].Value.Length;
                string content = ConvertInline(headingMatch.Groups[2].Value);

                string spectreTag = level switch
                {
                    1 => $"[bold underline]{content}[/]",
                    2 => $"[bold]{content}[/]",
                    _ => $"[bold dim]{content}[/]",   // H3–H6
                };

                result.MyAppendLine(spectreTag);
                continue;
            }

            // ── Blockquote ───────────────────────────────────────────────────
            var bqMatch = BlockquotePattern.Match(line);
            if (bqMatch.Success)
            {
                string content = ConvertInline(bqMatch.Groups[1].Value);
                result.MyAppendLine($"[italic dim]{content}[/]");
                continue;
            }

            // ── Normal paragraph line ────────────────────────────────────────
            result.MyAppendLine(ConvertInline(line));
        }

        return result.ToString().TrimEnd();
    }

    private static StringBuilder MyAppendLine(this StringBuilder stringBuilder)
    {
        return stringBuilder.Append('\n');
    }

    private static StringBuilder MyAppendLine(this StringBuilder stringBuilder, string line)
    {
        return stringBuilder.Append(line + "\n");
    }

    // ────────────────────────────────────────────────────────────────────────
    // Inline conversion
    // ────────────────────────────────────────────────────────────────────────

    private static string ConvertInline(string text)
    {
        // Step 1: escape any literal '[' or ']' that are NOT part of a
        //         Markdown link so they don't confuse Spectre's parser.
        //         We do this before applying inline patterns so that the
        //         patterns themselves can still inject '[' and ']' for markup.
        text = EscapeBracketsExceptLinks(text);

        // Step 2: apply inline patterns in precedence order.
        text = BoldItalicStars.Replace(text, "[bold italic]$1[/]");
        text = BoldStars.Replace(text, "[bold]$1[/]");
        text = BoldUnderscores.Replace(text, "[bold]$1[/]");
        text = ItalicStars.Replace(text, "[italic]$1[/]");
        text = ItalicUnderscores.Replace(text, "[italic]$1[/]");
        text = Strikethrough.Replace(text, "[strikethrough]$1[/]");
        text = InlineCode.Replace(text, "[bold yellow]$1[/]");
        text = Link.Replace(text, "[link=$2]$1[/]");

        return text;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Bracket escaping helpers
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Escapes all '[' and ']' as '[[' and ']]' so Spectre.Console treats
    /// them as literal characters.
    /// </summary>
    private static string EscapeBrackets(string text)
        => text.Replace("[", "[[").Replace("]", "]]");

    /// <summary>
    /// Escapes brackets that are NOT part of a Markdown link pattern
    /// <c>[label](url)</c>, so they render as literals in Spectre.Console
    /// while still allowing the Link regex to fire afterwards.
    /// </summary>
    private static string EscapeBracketsExceptLinks(string text)
    {
        // Replace the Markdown link temporarily with a placeholder so we can
        // escape everything else without touching the link syntax.
        var placeholders = new System.Collections.Generic.List<string>();

        string protected_ = Link.Replace(text, m =>
        {
            int idx = placeholders.Count;
            placeholders.Add(m.Value);
            return $"\x00LINK{idx}\x00";
        });

        // Now escape all remaining brackets.
        protected_ = protected_.Replace("[", "[[").Replace("]", "]]");

        // Restore the placeholders.
        for (int i = 0; i < placeholders.Count; i++)
            protected_ = protected_.Replace($"\x00LINK{i}\x00", placeholders[i]);

        return protected_;
    }
}