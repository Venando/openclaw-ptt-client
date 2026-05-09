using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Spectre.Console;
using OpenClawPTT;

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
///   Tables          | a | b |
///                   |---|---|
///                   | 1 | 2 |
/// </remarks>
/// <summary>
/// Per-column alignment for markdown tables.
/// </summary>
internal enum TableAlignment
{
    Left,
    Center,
    Right,
}

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
    // Table delimiter line: |---|---| pattern
    private static readonly Regex TableSeparatorPattern = new(@"^\|[-:\s|]+\|$", RegexOptions.Compiled);
    // Table row: | cells |
    private static readonly Regex TableRowPattern = new(@"^\|.+\|$", RegexOptions.Compiled);

    // ── Placeholder tokens for inline code protection ───────────────────────
    // These are unlikely to appear in real markdown input.
    private const string CodePlaceholderPrefix = "\x00CODE";
    private const string CodePlaceholderSuffix = "\x00";

    /// <summary>
    /// Converts <paramref name="markdown"/> to a Spectre.Console markup string
    /// with an available width for table layout (to avoid overflowing the console).
    /// </summary>
    public static string Convert(string markdown, int availableWidth = int.MaxValue)
    {
        if (markdown is null) throw new ArgumentNullException(nameof(markdown));
        if (availableWidth <= 0) availableWidth = int.MaxValue;

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

                if (inFencedBlock)
                    result.MyAppendLine("[dim]─────────────────[italic]code[/]─────────────────[/]");
                else
                    result.MyAppendLine("[dim]──────────────────────────────────────[/]");

                continue;
            }

            if (inFencedBlock)
            {
                result.MyAppendLine($"[default on gray15]{EscapeBrackets(line)}[/]");
                continue;
            }

            // ── Table ────────────────────────────────────────────────────────
            if (TableRowPattern.IsMatch(line) && i + 1 < lines.Length && TableSeparatorPattern.IsMatch(lines[i + 1]))
            {
                i = RenderTable(lines, i, result, availableWidth);
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

    // ────────────────────────────────────────────────────────────────────────
    // Table rendering
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Represents a single parsed markdown table.
    /// </summary>
    private sealed class MarkdownTable
    {
        public int ColumnCount { get; }
        public List<TableAlignment> Alignments { get; } = new();
        public List<List<string>> FormattedRows { get; } = new();

        public MarkdownTable(int columnCount)
        {
            ColumnCount = columnCount;
        }
    }

    /// <summary>
    /// Parses the separator line to determine per-column alignment.
    /// </summary>
    private static TableAlignment ParseAlignment(string cellText)
    {
        string trimmed = cellText.Trim();
        bool left = trimmed.StartsWith(':');
        bool right = trimmed.EndsWith(':');

        if (left && right) return TableAlignment.Center;
        if (right) return TableAlignment.Right;
        return TableAlignment.Left;
    }

    /// <summary>
    /// Parses a table row (header or body) into individual cell values.
    /// Strips leading/trailing pipe and splits on internal pipes.
    /// </summary>
    private static string[] ParseRowCells(string line)
    {
        string trimmed = line.Trim();
        if (trimmed.StartsWith('|')) trimmed = trimmed.Substring(1);
        if (trimmed.EndsWith('|')) trimmed = trimmed.Substring(0, trimmed.Length - 1);

        return trimmed.Split('|');
    }

    /// <summary>
    /// Gets the display width of cell content with Spectre markup stripped.
    /// Uses CharacterWidth for accurate East Asian character width measurement.
    /// </summary>
    private static int GetCellDisplayWidth(string formattedCell)
    {
        string plain = Markup.Remove(formattedCell);
        return CharacterWidth.GetDisplayWidth(plain);
    }

    /// <summary>
    /// Renders a markdown table starting at <paramref name="startIndex"/>.
    /// Returns the index of the last processed line.
    /// </summary>
    private static int RenderTable(string[] lines, int startIndex, StringBuilder result, int availableWidth)
    {
        var tableLines = CollectTableLines(lines, startIndex);

        if (tableLines.Count < 2)
        {
            result.MyAppendLine(ConvertInline(lines[startIndex]));
            return startIndex;
        }

        var table = ParseTable(tableLines);
        int[] colWidths = CalculateColumnWidths(table);
        colWidths = ShrinkColumnWidths(colWidths, availableWidth);

        result.MyAppendLine(RenderBorder(colWidths, '╭', '┬', '╮'));
        result.MyAppendLine(RenderContentRow(table.FormattedRows[0], colWidths, table.Alignments, isHeader: true));
        result.MyAppendLine(RenderBorder(colWidths, '├', '┼', '┤'));

        for (int r = 1; r < table.FormattedRows.Count; r++)
            result.MyAppendLine(RenderContentRow(table.FormattedRows[r], colWidths, table.Alignments, isHeader: false));

        result.MyAppendLine(RenderBorder(colWidths, '╰', '┴', '╯'));

        return startIndex + tableLines.Count - 1;
    }

    /// <summary>
    /// Collects consecutive table rows (header + separator + body).
    /// </summary>
    private static List<string> CollectTableLines(string[] lines, int startIndex)
    {
        var tableLines = new List<string>();
        int i = startIndex;
        while (i < lines.Length && TableRowPattern.IsMatch(lines[i]))
        {
            tableLines.Add(lines[i]);
            i++;
        }
        return tableLines;
    }

    /// <summary>
    /// Parses collected table lines into a <see cref="MarkdownTable"/>.
    /// </summary>
    private static MarkdownTable ParseTable(List<string> tableLines)
    {
        string[] separatorCells = ParseRowCells(tableLines[1]);
        string[] headerCells = ParseRowCells(tableLines[0]);
        int columnCount = Math.Max(headerCells.Length, separatorCells.Length);

        var table = new MarkdownTable(columnCount);

        for (int c = 0; c < columnCount; c++)
        {
            string sepCell = c < separatorCells.Length ? separatorCells[c].Trim() : "---";
            table.Alignments.Add(ParseAlignment(sepCell));
        }

        // Parse and format header (line 0)
        var headerFormatted = new List<string>();
        for (int c = 0; c < columnCount; c++)
        {
            string raw = c < headerCells.Length ? headerCells[c].Trim() : "";
            headerFormatted.Add(ConvertInline(raw));
        }
        table.FormattedRows.Add(headerFormatted);

        // Parse and format body rows (line 2+)
        for (int r = 2; r < tableLines.Count; r++)
        {
            string[] cells = ParseRowCells(tableLines[r]);
            var rowFormatted = new List<string>();
            for (int c = 0; c < columnCount; c++)
            {
                string raw = c < cells.Length ? cells[c].Trim() : "";
                rowFormatted.Add(ConvertInline(raw));
            }
            table.FormattedRows.Add(rowFormatted);
        }

        return table;
    }

    /// <summary>
    /// Calculates per-column display widths from formatted table rows.
    /// </summary>
    private static int[] CalculateColumnWidths(MarkdownTable table)
    {
        int[] colWidths = new int[table.ColumnCount];
        for (int c = 0; c < table.ColumnCount; c++)
        {
            int maxWidth = 0;
            foreach (var row in table.FormattedRows)
            {
                if (c < row.Count)
                {
                    int w = GetCellDisplayWidth(row[c]);
                    if (w > maxWidth) maxWidth = w;
                }
            }
            colWidths[c] = Math.Max(maxWidth, 1);
        }
        return colWidths;
    }

    /// <summary>
    /// Shrinks column widths to fit <paramref name="availableWidth"/> if needed.
    /// Table row overhead: left border(1) + 2*padding per column + separators + right border(1)
    /// = 3*colCount + 1
    /// Three-pass shrink: rightmost → minimum 3px, all → minimum 1px, then proportional.
    /// </summary>
    private static int[] ShrinkColumnWidths(int[] colWidths, int availableWidth)
    {
        int totalWidth = ComputeTableTotalWidth(colWidths);

        if (totalWidth <= availableWidth || availableWidth <= 10)
            return colWidths;

        int[] result = (int[])colWidths.Clone();
        int excess = totalWidth - availableWidth;

        // Pass 1: shrink rightmost columns to minimum 3
        for (int c = result.Length - 1; c >= 0 && excess > 0; c--)
        {
            int shrink = Math.Min(excess, Math.Max(0, result[c] - 3));
            result[c] -= shrink;
            excess -= shrink;
        }

        // Pass 2: shrink all columns to minimum 1
        for (int c = 0; c < result.Length && excess > 0; c++)
        {
            int shrink = Math.Min(excess, Math.Max(0, result[c] - 1));
            result[c] -= shrink;
            excess -= shrink;
        }

        // Pass 3: last resort proportional shrink
        if (excess > 0)
        {
            for (int c = 0; c < result.Length; c++)
                result[c] = Math.Max(1, result[c] - (excess / result.Length) - 1);
        }

        return result;
    }

    /// <summary>
    /// Computes the total rendered width of a table given column widths.
    /// </summary>
    private static int ComputeTableTotalWidth(int[] colWidths)
    {
        int total = 1; // Left border
        for (int c = 0; c < colWidths.Length; c++)
        {
            total += colWidths[c] + 2; // content + padding both sides
            if (c < colWidths.Length - 1)
                total += 1; // column separator
        }
        total += 1; // Right border
        return total;
    }

    /// <summary>
    /// Renders a horizontal border row for the table.
    /// </summary>
    private static string RenderBorder(int[] colWidths, char left, char join, char right)
    {
        var sb = new StringBuilder();
        sb.Append("[blue]");
        sb.Append(left);

        for (int c = 0; c < colWidths.Length; c++)
        {
            sb.Append('─', colWidths[c] + 2);
            if (c < colWidths.Length - 1)
                sb.Append(join);
        }

        sb.Append(right);
        sb.Append("[/]");
        return sb.ToString();
    }

    /// <summary>
    /// Truncates <paramref name="formattedCell"/> (Spectre markup) so its
    /// visible display width does not exceed <paramref name="maxWidth"/>.
    /// Appends "…" if truncation occurs.
    /// </summary>
    private static string TruncateCellToWidth(string formattedCell, int maxWidth)
    {
        if (string.IsNullOrEmpty(formattedCell) || maxWidth <= 0)
            return "";

        string plain = Markup.Remove(formattedCell);
        int width = CharacterWidth.GetDisplayWidth(plain);

        if (width <= maxWidth)
            return formattedCell;

        int targetWidth = Math.Max(1, maxWidth - 1);
        var truncated = new StringBuilder();
        int currentWidth = 0;

        foreach (char c in plain)
        {
            int charWidth = CharacterWidth.GetDisplayWidth(c);
            if (currentWidth + charWidth > targetWidth)
                break;
            truncated.Append(c);
            currentWidth += charWidth;
        }

        truncated.Append('…');
        return truncated.ToString();
    }

    private static string RenderContentRow(List<string> formattedCells, int[] colWidths, List<TableAlignment> alignments, bool isHeader)
    {
        var sb = new StringBuilder();
        sb.Append("[blue]│[/]");

        for (int c = 0; c < colWidths.Length; c++)
        {
            string cellContent = c < formattedCells.Count ? formattedCells[c] : "";
            int cellDisplayWidth = GetCellDisplayWidth(cellContent);

            string displayContent = cellDisplayWidth > colWidths[c]
                ? TruncateCellToWidth(cellContent, colWidths[c])
                : cellContent;

            cellDisplayWidth = GetCellDisplayWidth(displayContent);
            string styledContent = isHeader ? $"[bold]{displayContent}[/]" : displayContent;
            int padding = colWidths[c] - cellDisplayWidth;

            sb.Append(' '); // Left padding (always 1)

            if (padding > 0)
            {
                if (c < alignments.Count && alignments[c] == TableAlignment.Right)
                {
                    sb.Append(' ', padding);
                    sb.Append(styledContent);
                }
                else if (c < alignments.Count && alignments[c] == TableAlignment.Center)
                {
                    int leftPad = padding / 2;
                    int rightPad = padding - leftPad;
                    sb.Append(' ', leftPad);
                    sb.Append(styledContent);
                    sb.Append(' ', rightPad);
                }
                else
                {
                    sb.Append(styledContent);
                    sb.Append(' ', padding);
                }
            }
            else
            {
                sb.Append(styledContent);
            }

            sb.Append(' '); // Right padding (always 1)
            sb.Append("[blue]│[/]");
        }

        return sb.ToString();
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
        text = EscapeBracketsExceptLinks(text);

        var codePlaceholders = new Dictionary<int, string>();
        int codeIdx = 0;
        text = InlineCode.Replace(text, m =>
        {
            string content = m.Groups[1].Value;
            int idx = codeIdx++;
            codePlaceholders[idx] = content;
            return CodePlaceholderPrefix + idx + CodePlaceholderSuffix;
        });

        text = ConvertLinksWithFormatting(text);
        text = ApplyInlineFormatting(text);

        for (int i = 0; i < codeIdx; i++)
        {
            text = text.Replace(
                CodePlaceholderPrefix + i + CodePlaceholderSuffix,
                "[bold gray89 on darkblue]" + codePlaceholders[i] + "[/]");
        }

        return text;
    }

    /// <summary>
    /// Applies bold, italic, bold-italic, and strikethrough formatting patterns.
    /// </summary>
    private static string ApplyInlineFormatting(string text)
    {
        text = BoldItalicStars.Replace(text, "[bold italic]$1[/]");
        text = BoldStars.Replace(text, "[bold]$1[/]");
        text = BoldUnderscores.Replace(text, "[bold]$1[/]");
        text = ItalicStars.Replace(text, "[italic]$1[/]");
        text = ItalicUnderscores.Replace(text, "[italic]$1[/]");
        text = Strikethrough.Replace(text, "[strikethrough]$1[/]");
        return text;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Bracket escaping helpers
    // ────────────────────────────────────────────────────────────────────────

    private static string EscapeBrackets(string text)
        => text.Replace("[", "[[").Replace("]", "]]");

    private static string EscapeBracketsExceptLinks(string text)
    {
        var placeholders = new List<string>();

        string protected_ = Link.Replace(text, m =>
        {
            int idx = placeholders.Count;
            placeholders.Add(m.Value);
            return $"\x00LINK{idx}\x00";
        });

        protected_ = protected_.Replace("[", "[[").Replace("]", "]]");

        for (int i = 0; i < placeholders.Count; i++)
            protected_ = protected_.Replace($"\x00LINK{i}\x00", placeholders[i]);

        return protected_;
    }

    private static string ConvertLinksWithFormatting(string text)
    {
        return Link.Replace(text, m =>
        {
            string label = m.Groups[1].Value;
            string url = m.Groups[2].Value;

            string formattedLabel = ApplyInlineFormatting(label);

            var outerTagMatch = Regex.Match(
                formattedLabel, @"^\[([a-z0-9 ]+)\](.+)\[/\]$", RegexOptions.Singleline);

            if (outerTagMatch.Success)
            {
                string style = outerTagMatch.Groups[1].Value;
                string inner = outerTagMatch.Groups[2].Value;
                return $"[{style} link={url}]{inner}[/]";
            }

            return $"[link={url}]{formattedLabel}[/]";
        });
    }
}
