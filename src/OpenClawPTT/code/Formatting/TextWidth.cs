namespace OpenClawPTT.Formatting;

/// <summary>
/// Utility for measuring and wrapping text by visual display width,
/// accounting for CJK / fullwidth characters (width 2) vs. standard characters (width 1).
/// </summary>
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
    /// visual columns. Breaks at word boundaries (whitespace) when possible,
    /// or at the exact column limit otherwise.
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

        // Treat tab as a single space for wrapping purposes
        text = text.Replace('\t', ' ');

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

            // If we can't fit even one character, force-break at current position
            if (lineEnd == lineStart && i < text.Length)
            {
                int cw = GetVisualWidth(text[i]);
                lineEnd = i + 1;
                i = lineEnd;
                lines.Add(text[lineStart..lineEnd]);
                continue;
            }

            // If we broke mid-word, try to find the last whitespace for a cleaner break
            if (i < text.Length && text[i] != '\n' && lineEnd > lineStart)
            {
                int breakAt = -1;
                // Scan backwards from the break point for any whitespace
                for (int j = lineEnd - 1; j >= lineStart; j--)
                {
                    if (char.IsWhiteSpace(text[j]))
                    {
                        breakAt = j;
                        break;
                    }
                }

                if (breakAt > lineStart)
                {
                    lineEnd = breakAt;
                    i = breakAt + 1; // skip the whitespace so next line starts clean
                }
            }

            lines.Add(text[lineStart..lineEnd]);
        }

        return lines;
    }
}
