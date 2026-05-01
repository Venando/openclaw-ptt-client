using System.Text;

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
    private string _newlineSuffix;
    private bool _prefixAlreadyPrinted;

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
        _prefix = prefix ?? string.Empty;
        _newlineSuffix = new string(' ', _prefix.Length);
        _prefixAlreadyPrinted = prefixAlreadyPrinted;
        _consoleWidth = output.WindowWidth > 0 ? output.WindowWidth : 80;
    }

    /// <summary>
    /// Calculate available width for text based on configured console width.
    /// </summary>
    private int GetAvailableWidth()
    {
        int consoleWidth = _consoleWidth;

        // When prefix was already printed on current line, available width is from suffix to right margin
        // When prefix not yet printed, available width is from prefix to right margin
        int effectiveRightMargin = Math.Max(_rightMarginIndent, (int)(consoleWidth * 0.1));
        int available;
        if (_prefixAlreadyPrinted)
        {
            int suffixLength = _newlineSuffix.Length;
            available = consoleWidth - suffixLength - effectiveRightMargin;
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
    }

    /// <summary>
    /// Process a delta chunk and write formatted output.
    /// </summary>
    public void ProcessDelta(string delta)
    {
        int availableWidth = GetAvailableWidth();

        foreach (char c in delta)
        {
            if (c == '\n')
            {
                FlushWordBuffer(availableWidth);
                // Explicit newline: break line and start new line with appropriate suffix
                _output.WriteLine();
                _output.Write(_newlineSuffix);
                _currentLineLength = 0;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                FlushWordBuffer(availableWidth);
                // Add whitespace to current line if it fits
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
                // If word exceeds available width, we need to break it
                if (_wordBuffer.Length > availableWidth)
                {
                    // Break the word: output part that fits, keep remainder in buffer
                    int charsThatFit = availableWidth - _currentLineLength;
                    if (charsThatFit > 0)
                    {
                        string part = _wordBuffer.ToString(0, charsThatFit);
                        _output.Write(part);
                        _currentLineLength += charsThatFit;
                        _wordBuffer.Remove(0, charsThatFit);
                    }
                    // Now current line is full, wrap
                    if (_wordBuffer.Length > 0)
                    {
                        WriteNewLine();
                        _currentLineLength = 0;
                        // Continue processing remaining word buffer
                    }
                }
            }
        }
    }

    /// <summary>
    /// Process a pre-formatted markup string and write it directly without word-wrapping.
    /// Use this for strings that already contain Spectre.Console markup tags.
    /// </summary>
    public void ProcessMarkupDelta(string markup)
    {
        int availableWidth = GetAvailableWidth();
        bool insideTag = false;
        int visibleWordLen = 0;

        foreach (char c in markup)
        {
            if (c == '[') { insideTag = true; _wordBuffer.Append(c); continue; }
            if (c == ']' && insideTag) { insideTag = false; _wordBuffer.Append(c); continue; }
            if (insideTag) { _wordBuffer.Append(c); continue; }

            if (c == '\n')
            {
                FlushWordBuffer(availableWidth, visibleWordLen);
                visibleWordLen = 0;
                _output.WriteLine();
                _output.Write(_newlineSuffix);
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

            _wordBuffer.Append(c);
            visibleWordLen++;
        }

        FlushWordBuffer(availableWidth, visibleWordLen);
    }

    /// <summary>
    /// Flush any remaining word buffer and finish the reply.
    /// </summary>
    public void Finish()
    {
        int availableWidth = GetAvailableWidth();
        FlushWordBuffer(availableWidth);
        _output.WriteLine();
    }
    
    private void FlushWordBuffer_Old(int availableWidth)
    {
        if (_wordBuffer.Length == 0)
            return;
            
        string word = _wordBuffer.ToString();
        
        // If word fits on current line, print it
        if (_currentLineLength + word.Length <= availableWidth)
        {
            _output.Write(word);
            _currentLineLength += word.Length;
        }
        else
        {
            // Word doesn't fit; need to wrap
            if (_currentLineLength > 0)
                WriteNewLine();
            
            // Word may still be longer than available width; split across multiple lines
            int start = 0;
            while (start < word.Length)
            {
                int chunkLength = Math.Min(availableWidth, word.Length - start);
                if (start > 0)
                    WriteNewLine();
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
        if (_wordBuffer.Length == 0)
            return;

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

            // Word may still exceed width; split it (markup stays on first chunk)
            // In practice Spectre markup words are short — this is a safety net.
            _output.Write(word);
            _currentLineLength = visibleLength;
        }

        _wordBuffer.Clear();
    }

    private void WriteNewLine()
    {
        _output.WriteLine();
        _output.Write(_newlineSuffix);
    }
}
