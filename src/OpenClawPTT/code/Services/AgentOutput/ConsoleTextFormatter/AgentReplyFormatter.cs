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
    /// color names, and spaces for compound tags like "bold underline").
    /// Tags containing quotes or other special characters are likely
    /// literal bracket content.
    /// </summary>
    private static bool IsValidTagName(string tagContent)
    {
        if (string.IsNullOrEmpty(tagContent))
            return false;
        foreach (char ch in tagContent)
        {
            if (!char.IsLetterOrDigit(ch) && ch != '-' && ch != '.' && ch != '_' && ch != '#' && ch != ' ' && ch != '=' && ch != ':' && ch != '/')
                return false;
        }
        return true;
    }

    /// <summary>
    /// Returns true if <paramref name="tagName"/> is a known Spectre.Console
    /// style/tag that is intentionally used in this application.
    /// Unknown tags like "text" or "foo" appearing in content are more
    /// likely literal brackets than intentional markup.
    /// </summary>
    private static readonly HashSet<string> _knownSpectreTags = new(System.StringComparer.OrdinalIgnoreCase)
    {
        // Decoration keywords
        "bold", "dim", "italic", "underline", "strikethrough", "invert", "conceal",
        "blink", "slowblink", "rapidblink",
        // Style shortcuts
        "default", "none",
        // Named colors
        "red", "green", "yellow", "blue", "magenta", "cyan", "white",
        "grey", "gray",
        "darkred", "darkgreen", "darkyellow", "darkblue", "darkmagenta", "darkcyan",
        "darkgrey", "darkgray",
        "lightred", "lightgreen", "lightyellow", "lightblue", "lightmagenta", "lightcyan",
        "lightgrey", "lightgray",
        // Specific colors used in this app
        "deepskyblue3", "cyan2", "gray15", "gray93", "olive",
        // Link
        "link",
    };

    /// <summary>
    /// Normalizes tag content to match Spectre.Console's expected format.
    /// Spectre requires link=url without spaces around the '=', but agent
    /// output may contain a space before '=' (e.g. "link = url").
    /// This normalization strips spaces adjacent to '=' so the tag is valid.
    /// </summary>
    private static string NormalizeTagContent(string tagContent)
    {
        if (string.IsNullOrEmpty(tagContent))
            return tagContent;
        int eqIdx = tagContent.IndexOf('=');
        if (eqIdx < 0)
            return tagContent;
        // Only normalize if there are spaces before '='
        if (eqIdx > 0 && tagContent[eqIdx - 1] == ' ')
        {
            // Remove spaces immediately before '='
            int trimEnd = eqIdx - 1;
            while (trimEnd >= 0 && tagContent[trimEnd] == ' ')
                trimEnd--;
            // Also remove spaces immediately after '='
            int trimStart = eqIdx + 1;
            while (trimStart < tagContent.Length && tagContent[trimStart] == ' ')
                trimStart++;
            // Rebuild: part before spaces + '=' + part after spaces
            string before = tagContent.Substring(0, trimEnd + 1);
            string after = tagContent.Substring(trimStart);
            return before + "=" + after;
        }
        return tagContent;
    }

    /// <summary>
    /// Returns true if <paramref name="tagName"/> is a known Spectre.Console
    /// tag/style. Only used for rejecting improbable tag names that appeared
    /// after whitespace in content.
    /// </summary>
    private static bool IsSpectreKnownTag(string tagName)
    {
        if (string.IsNullOrEmpty(tagName))
            return false;
        // Strip any style attributes like "on color" or "link=url"
        if (tagName.StartsWith("link=", StringComparison.OrdinalIgnoreCase)
            || tagName.StartsWith("link ", StringComparison.OrdinalIgnoreCase))
            return true;
        int spaceIdx = tagName.IndexOf(' ');
        string baseName = spaceIdx >= 0 ? tagName.Substring(0, spaceIdx) : tagName;
        return _knownSpectreTags.Contains(baseName);
    }

    /// <summary>
    /// Process a pre-formatted markup string where [tag]…[/tag] sequences
    /// have zero visible width. Preserves markup tags in output.
    /// </summary>
    public void ProcessMarkupDelta(string markup)
    {
        int availableWidth = GetAvailableWidth();
        bool insideTag = false;
        int realVisibleWordLen = Markup.Remove(markup).Length;
        int visibleWordLen = 0;

        for (int i = 0; i < markup.Length; i++)
        {
            char c = markup[i];

            // ── tag boundary detection ───────────────────────────────
            if (!insideTag && c == '[')
            {
                // Flush any pending visible word before entering tag mode.
                // This prevents the visible word from being split mid-word
                // by the word-wrap logic when the NEXT characters are a tag
                // (like [/]) rather than visible text. Without this, the
                // visibleWordLen > remaining check can trigger a split right
                // at the '[' of a [/] close tag, corrupting the markup.
                FlushWordBuffer(availableWidth, visibleWordLen);
                visibleWordLen = 0;

                // Spectre uses [[ to represent a literal '['. Preserve
                // the double-bracket escape in the output so Spectre's
                // markup parser will render it as a literal '['.
                if (i + 1 < markup.Length && markup[i + 1] == '[')
                {
                    _wordBuffer.Append(c);
                    _wordBuffer.Append(c);
                    i++; // skip the second '['
                    visibleWordLen++;
                    continue;
                }

                // Enter tag mode and start accumulating tag content.
                // Let the tag validator at ']' decide if it's a valid
                // Spectre tag or literal content. This is safer than
                // heuristic context checks, which can fail for patterns
                // like "and[bold]" where the bracket follows a letter.
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
                bool shouldEscape = false;

                if (tagContent != "/"
                    && tagContent.Length > 0
                    && !tagContent.StartsWith("/"))
                {
                    // Secondary guard: single-character tag names (like [x],
                    // [b], [5]) are almost certainly literal code content
                    // (array access, variable names), not intentional markup.
                    if (tagContent.Length <= 1)
                    {
                        shouldEscape = true;
                    }
                    else if (!IsValidTagName(tagContent))
                    {
                        // Original check: invalid characters in tag name
                        shouldEscape = true;
                    }
                    else if (!IsSpectreKnownTag(tagContent))
                    {
                        // Tertiary guard: unknown tag names (not in the known
                        // Spectre style set) are almost certainly literal
                        // content that happened to be bracketed. Escape them
                        // to avoid pushing bogus tags onto the stack.
                        shouldEscape = true;
                    }
                }

                if (shouldEscape)
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

                // ── Normalize Spectre tag format ────────────────────
                // Spectre.Console requires link=url without spaces around
                // the '=', but agent output may produce link = url.
                // Normalize by removing spaces adjacent to '=' in tag content.
                string normalizedTag = NormalizeTagContent(tagContent);
                if (normalizedTag != tagContent)
                {
                    // Update the buffer to use normalized tag
                    _wordBuffer.Remove(openPos, closePos - openPos + 1);
                    _wordBuffer.Length = openPos;
                    _wordBuffer.Append("[");
                    _wordBuffer.Append(normalizedTag);
                    _wordBuffer.Append("]");
                    tagContent = normalizedTag;
                    // Recalculate closePos since buffer changed
                    closePos = _wordBuffer.Length - 1;
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

                // Flush the processed tag text to output immediately.
                // This prevents subsequent visible characters from being
                // combined with the tag in the word buffer, which could
                // cause the mid-word split logic to cut through the
                // middle of a tag (like splitting [bold yellow...] into
                // [bold yellow st
                // rikethrough]some text line).
                int tagVisibleLen = 0; // tags have zero visible width
                FlushWordBuffer(availableWidth, tagVisibleLen);
                continue;
            }

            if (insideTag)
            {
                _wordBuffer.Append(c);
                continue;
            }

            // ── visible (non-tag) characters below ──────────────────
            // Spectre uses ]] to represent a literal ']'. Preserve
            // the double-bracket escape in the output so Spectre's
            // markup parser will render it as a literal ']'.
            if (!insideTag && c == ']' && i + 1 < markup.Length && markup[i + 1] == ']')
            {
                _wordBuffer.Append("]]");
                i++; // skip the second ']'
                visibleWordLen++;
                continue;
            }

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
                    // Before calling WriteNewLine, write any pending markup tags
                    // from the buffer to the current line output. This ensures
                    // WriteNewLine's close/reopen has matching open tags on the
                    // current line.
                    string fullBuf = _wordBuffer.ToString();
                    int rawLen = fullBuf.Length;
                    int tagLen = rawLen - visibleWordLen;

                    if (tagLen > 0 && remaining <= 0)
                    {
                        string pendingTags = fullBuf.Substring(0, tagLen);
                        _output.Write(pendingTags);
                        // Tags have zero visible width, so _currentLineLength unchanged.
                        // Remove the emitted tags from the buffer.
                        _wordBuffer.Remove(0, tagLen);
                    }

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
