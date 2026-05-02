using System.Text;
using Spectre.Console;

namespace OpenClawPTT;

/// <summary>
/// Formats streaming agent replies with word wrap and right margin indent.
/// Maintains state across delta chunks within a single reply.
/// </summary>
public sealed class AgentReplyFormatter : IAgentReplyFormatter
{
    private readonly int _rightMarginIndent;
    private readonly StringBuilder _wordBuffer = new StringBuilder();
    private int _currentLineLength; // length of current line excluding prefix
    private int _consoleWidth;
    private readonly IFormattedOutput _output;

    private string _prefix;
    private string _newlinePrefixLenght;
    private bool _prefixAlreadyPrinted;

    // Tracks currently open Spectre markup tags (e.g. "grey", "bold") for
    // re-emission on word-wrap line breaks, so markup is never split across lines.
    private readonly Stack<string> _openMarkupTags = new Stack<string>();

    /// <summary>
    /// Convenience constructor using default right-margin indent (10).
    /// </summary>
    public AgentReplyFormatter(string prefix, bool prefixAlreadyPrinted, IFormattedOutput output)
        : this(prefix, rightMarginIndent: 10, prefixAlreadyPrinted, output)
    {
    }

    /// <summary>
    /// Constructor with explicit word-wrap parameters.
    /// </summary>
    public AgentReplyFormatter(string prefix, int rightMarginIndent, bool prefixAlreadyPrinted, IFormattedOutput output)
    {
        _rightMarginIndent = rightMarginIndent;
        _output = output;
        Init(prefix, prefixAlreadyPrinted, output);
    }

    private void Init(string prefix, bool prefixAlreadyPrinted, IFormattedOutput output)
    {
        _prefix = Markup.Remove(prefix ?? string.Empty);
        _newlinePrefixLenght = new string(' ', _prefix.Length);
        _prefixAlreadyPrinted = prefixAlreadyPrinted;
        _consoleWidth = output.WindowWidth > 0 ? output.WindowWidth : 80;
    }

    /// <summary>
    /// Calculate available width for text based on configured console width.
    /// </summary>
    private int GetAvailableWidth()
    {
        int consoleWidth = _consoleWidth;

        int effectiveRightMargin = Math.Max(_rightMarginIndent, (int)(consoleWidth * 0.1));
        int available;
        if (_prefixAlreadyPrinted)
        {
            available = consoleWidth - _newlinePrefixLenght.Length - effectiveRightMargin;
        }
        else
        {
            available = consoleWidth - _prefix.Length - effectiveRightMargin;
        }
        return available > 0 ? available : consoleWidth / 2;
    }

    public void Reconfigure(string prefix, bool prefixAlreadyPrinted = false)
    {
        Init(prefix, prefixAlreadyPrinted, _output);
        _openMarkupTags.Clear();
        _wordBuffer.Clear();
        _currentLineLength = 0;
    }

    /// <summary>
    /// Process a plain-text delta chunk and write formatted output with word-wrap.
    /// </summary>
    public void ProcessDelta(string delta)
    {
        int availableWidth = GetAvailableWidth();

        foreach (char c in delta)
        {
            if (c == '\n')
            {
                FlushWordBuffer(availableWidth);
                _output.WriteLine();
                _output.Write(_newlinePrefixLenght);
                _currentLineLength = 0;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                FlushWordBuffer(availableWidth);
                if (_currentLineLength + 1 <= availableWidth)
                {
                    _output.Write(c.ToString());
                    _currentLineLength++;
                }
                else
                {
                    WriteNewLine();
                    _output.Write(c.ToString());
                    _currentLineLength = 1;
                }
            }
            else
            {
                _wordBuffer.Append(c);
                if (_wordBuffer.Length > availableWidth)
                {
                    int charsThatFit = availableWidth - _currentLineLength;
                    if (charsThatFit > 0)
                    {
                        string part = _wordBuffer.ToString(0, charsThatFit);
                        _output.Write(part);
                        _currentLineLength += charsThatFit;
                        _wordBuffer.Remove(0, charsThatFit);
                    }
                    if (_wordBuffer.Length > 0)
                    {
                        WriteNewLine();
                        _currentLineLength = 0;
                    }
                }
            }
        }
    }

    // ── helper: validate a Spectre tag name ──────────────────────────
    /// <summary>
    /// Returns true if <paramref name="tagContent"/> looks like a valid
    /// Spectre.Console tag name (alphanumeric, hyphens, dots, underscores,
    /// and color names). Tags containing quotes, spaces, or other
    /// special characters are likely literal bracket content.
    /// </summary>
    private static bool IsValidTagName(string tagContent)
    {
        if (string.IsNullOrEmpty(tagContent))
            return false;
        foreach (char ch in tagContent)
        {
            if (!char.IsLetterOrDigit(ch) && ch != '-' && ch != '.' && ch != '_' && ch != '#')
                return false;
        }
        return true;
    }

    /// <summary>
    /// Process a pre-formatted markup string where [tag]…[/tag] sequences
    /// have zero visible width. Preserves markup tags in output.
    /// </summary>
    public void ProcessMarkupDelta(string markup)
    {
        int availableWidth = GetAvailableWidth();
        bool insideTag = false;
        int visibleWordLen = 0;

        for (int i = 0; i < markup.Length; i++)
        {
            char c = markup[i];

            // ── tag boundary detection ───────────────────────────────
            if (!insideTag && c == '[')
            {
                // Spectre uses [[ to represent a literal '['. If the next
                // char is also '[', emit one '[' and skip both.
                if (i + 1 < markup.Length && markup[i + 1] == '[')
                {
                    _wordBuffer.Append(c);
                    i++; // skip the second '['
                    visibleWordLen++;
                    continue;
                }
                // Enter tag mode and start accumulating tag content.
                insideTag = true;
                _wordBuffer.Append(c);
                continue;
            }

            if (insideTag && c == ']')
            {
                insideTag = false;
                _wordBuffer.Append(c);
                // Determine whether this is an opening tag or closing tag
                // by inspecting the tag content (everything between '[' and ']').
                // The buffer now ends with "...]", so we search backwards.
                int closePos = _wordBuffer.Length - 1;
                int openPos = _wordBuffer.ToString().LastIndexOf('[', closePos - 1);
                string tagContent = _wordBuffer.ToString(openPos + 1, closePos - openPos - 1);

                // ── Validate tag content ────────────────────────────────
                // If the content between [ and ] doesn't look like a valid
                // Spectre tag name (e.g. ["a"] is not valid), treat the
                // brackets as escaped literal content: remove the [ and ]
                // from the buffer and replace with [[ and ]].
                if (tagContent != "/"
                    && tagContent.Length > 0
                    && !tagContent.StartsWith("/")
                    && !IsValidTagName(tagContent))
                {
                    // Back out: replace raw brackets with escaped ones
                    // so they render as literal characters.
                    _wordBuffer.Remove(openPos, closePos - openPos + 1);
                    _wordBuffer.Length = openPos;
                    // Re-append with escaped brackets
                    _wordBuffer.Append("[[");
                    _wordBuffer.Append(tagContent);
                    _wordBuffer.Append("]]");
                    visibleWordLen += tagContent.Length + 4; // [[ + content + ]]
                    continue;
                }

                if (tagContent == "/")
                {
                    // Generic close [/] — pop the most recent tag
                    if (_openMarkupTags.Count > 0)
                        _openMarkupTags.Pop();
                }
                else if (tagContent.StartsWith("/"))
                {
                    // Explicit close like [/dim], [/bold] — pop matching tag
                    string closeTagName = tagContent.Substring(1);
                    if (!string.IsNullOrEmpty(closeTagName) && _openMarkupTags.Count > 0)
                    {
                        var tempStack = new Stack<string>();
                        bool found = false;
                        while (_openMarkupTags.Count > 0)
                        {
                            string top = _openMarkupTags.Pop();
                            if (string.Equals(top, closeTagName, StringComparison.OrdinalIgnoreCase))
                            {
                                found = true;
                                break;
                            }
                            tempStack.Push(top);
                        }
                        while (tempStack.Count > 0)
                        {
                            _openMarkupTags.Push(tempStack.Pop());
                        }
                    }
                }
                else if (!string.IsNullOrEmpty(tagContent))
                {
                    // Opening tag like [dim], [bold] — push onto stack
                    _openMarkupTags.Push(tagContent);
                }
                continue;
            }

            if (insideTag)
            {
                _wordBuffer.Append(c);
                continue;
            }

            // ── visible (non-tag) characters below ──────────────────
            if (c == '\n')
            {
                FlushWordBuffer(availableWidth, visibleWordLen);
                visibleWordLen = 0;
                WriteNewLine();
                _currentLineLength = 0;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                FlushWordBuffer(availableWidth, visibleWordLen);
                visibleWordLen = 0;
                if (_currentLineLength + 1 <= availableWidth)
                {
                    _output.Write(c.ToString());
                    _currentLineLength++;
                }
                else
                {
                    WriteNewLine();
                    _output.Write(c.ToString());
                    _currentLineLength = 1;
                }
                continue;
            }

            // Regular visible character
            _wordBuffer.Append(c);
            visibleWordLen++;

            // If remaining space on current line can't fit the visible word, split.
            int remaining = availableWidth - _currentLineLength;
            if (visibleWordLen > remaining)
            {
                string full = _wordBuffer.ToString();
                int charsToEmit = Math.Min(remaining, full.Length);

                if (charsToEmit > 0)
                {
                    string emitPart = full.Substring(0, charsToEmit);
                    _output.Write(emitPart);
                    _currentLineLength += Math.Min(charsToEmit, visibleWordLen);
                    _wordBuffer.Clear();
                    _wordBuffer.Append(full.Substring(charsToEmit));
                    visibleWordLen = Math.Max(0, visibleWordLen - Math.Min(charsToEmit, visibleWordLen));
                }

                if (_wordBuffer.Length > 0)
                {
                    WriteNewLine();
                    _currentLineLength = 0;
                }
            }
        }

        FlushWordBuffer(availableWidth, visibleWordLen);
    }

    public void Finish()
    {
        int availableWidth = GetAvailableWidth();
        FlushWordBuffer(availableWidth);
        // Close all open markup tags so the output is valid self-contained markup.
        // This also resets the open-tag stack for reuse across multiple
        // ProcessMarkupDelta calls (e.g. one per code block line).
        foreach (string _ in _openMarkupTags)
        {
            _output.Write("[/]");
        }
        _openMarkupTags.Clear();
        _output.WriteLine();
    }

    [Obsolete("Use the overload with visibleLength parameter for markup support")]
    private void FlushWordBuffer_Old(int availableWidth)
    {
        if (_wordBuffer.Length == 0) return;
        string word = _wordBuffer.ToString();

        if (_currentLineLength + word.Length <= availableWidth)
        {
            _output.Write(word);
            _currentLineLength += word.Length;
        }
        else
        {
            if (_currentLineLength > 0) WriteNewLine();
            int start = 0;
            while (start < word.Length)
            {
                int chunkLength = Math.Min(availableWidth, word.Length - start);
                if (start > 0) WriteNewLine();
                _output.Write(word.Substring(start, chunkLength));
                start += chunkLength;
                _currentLineLength = chunkLength;
            }
        }
        _wordBuffer.Clear();
    }

    private void FlushWordBuffer(int availableWidth)
        => FlushWordBuffer(availableWidth, _wordBuffer.Length);

    private void FlushWordBuffer(int availableWidth, int visibleLength)
    {
        if (_wordBuffer.Length == 0) return;

        string word = _wordBuffer.ToString();

        if (_currentLineLength + visibleLength <= availableWidth)
        {
            _output.Write(word);
            _currentLineLength += visibleLength;
        }
        else
        {
            if (_currentLineLength > 0)
                WriteNewLine();
            _output.Write(word);
            _currentLineLength = visibleLength;
        }

        _wordBuffer.Clear();
    }

    private void WriteNewLine()
    {
        // Close all currently open markup tags before the line break
        // so the current line is self-contained markup.
        foreach (string tag in _openMarkupTags)
        {
            _output.Write("[/]");
        }

        _output.WriteLine();
        _output.Write(_newlinePrefixLenght);

        // Re-emit all open markup tags after the newline and prefix
        // so the next line is also self-contained.
        foreach (string tag in _openMarkupTags)
        {
            _output.Write($"[{tag}]");
        }
    }
}
