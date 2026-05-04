using System.Text;

namespace OpenClawPTT;

/// <summary>
/// Pure word-wrapping engine that tracks current line length and available width.
/// Accumulates characters into a buffer and determines when wrapping is needed.
/// </summary>
public sealed class WordWrapEngine
{
    private readonly StringBuilder _buffer = new();
    private int _currentLineLength;
    private readonly int _availableWidth;

    /// <summary>
    /// Creates a new word-wrap engine with the specified available width.
    /// </summary>
    public WordWrapEngine(int availableWidth)
    {
        _availableWidth = availableWidth > 0 ? availableWidth : 80;
    }

    /// <summary>
    /// Gets the current line length (visible characters on current line).
    /// </summary>
    public int CurrentLineLength => _currentLineLength;

    /// <summary>
    /// Gets the available width for each line.
    /// </summary>
    public int AvailableWidth => _availableWidth;

    /// <summary>
    /// Gets the length of the accumulated buffer.
    /// </summary>
    public int BufferLength => _buffer.Length;

    /// <summary>
    /// Gets whether the buffer is empty.
    /// </summary>
    public bool IsBufferEmpty => _buffer.Length == 0;

    /// <summary>
    /// Appends a single character to the buffer.
    /// </summary>
    public void AppendChar(char c) => _buffer.Append(c);

    /// <summary>
    /// Appends a string to the buffer.
    /// </summary>
    public void AppendString(string s) => _buffer.Append(s);

    /// <summary>
    /// Returns true if adding <paramref name="visibleLength"/> characters
    /// would exceed the available width.
    /// </summary>
    public bool NeedsWrap(int visibleLength)
    {
        return _currentLineLength + visibleLength > _availableWidth;
    }

    /// <summary>
    /// Calculates how many characters would fit on the current line
    /// given a <paramref name="visibleLength"/> that needs to be added.
    /// </summary>
    public int CalculateFitLength(int visibleLength)
    {
        int remaining = _availableWidth - _currentLineLength;
        return Math.Min(remaining, visibleLength);
    }

    /// <summary>
    /// Returns true if the current word (based on visible length) is too long
    /// for the remaining space on the current line.
    /// </summary>
    public bool WouldOverflow(int visibleWordLength)
    {
        return visibleWordLength > _availableWidth - _currentLineLength;
    }

    /// <summary>
    /// Flushes the buffer and returns its contents.
    /// Does not reset line length - use <see cref="RecordWritten"/> to update.
    /// </summary>
    public string Flush()
    {
        if (_buffer.Length == 0)
            return string.Empty;

        string content = _buffer.ToString();
        _buffer.Clear();
        return content;
    }

    /// <summary>
    /// Flushes a specific number of characters from the start of the buffer.
    /// </summary>
    public string FlushChars(int charCount)
    {
        if (_buffer.Length == 0 || charCount <= 0)
            return string.Empty;

        int toFlush = Math.Min(charCount, _buffer.Length);
        string content = _buffer.ToString(0, toFlush);
        _buffer.Remove(0, toFlush);
        return content;
    }

    /// <summary>
    /// Records that <paramref name="visibleLength"/> visible characters
    /// were written to the output, updating the current line length.
    /// </summary>
    public void RecordWritten(int visibleLength)
    {
        _currentLineLength += visibleLength;
    }

    /// <summary>
    /// Records that a new line was started, resetting the current line length to zero.
    /// </summary>
    public void RecordNewLine()
    {
        _currentLineLength = 0;
    }

    /// <summary>
    /// Sets the current line length to a specific value (used after prefix output).
    /// </summary>
    public void SetLineLength(int length)
    {
        _currentLineLength = Math.Max(0, length);
    }

    /// <summary>
    /// Clears the buffer without returning its contents.
    /// </summary>
    public void ClearBuffer() => _buffer.Clear();

    /// <summary>
    /// Gets the current buffer contents without clearing it.
    /// </summary>
    public string PeekBuffer() => _buffer.ToString();

    /// <summary>
    /// Gets a substring of the buffer without modifying it.
    /// </summary>
    public string PeekBufferSubstring(int startIndex, int length)
    {
        if (startIndex >= _buffer.Length)
            return string.Empty;

        int availableLength = Math.Min(length, _buffer.Length - startIndex);
        return _buffer.ToString(startIndex, availableLength);
    }

    /// <summary>
    /// Removes characters from the start of the buffer.
    /// </summary>
    public void RemoveFromBuffer(int charCount)
    {
        if (charCount > 0 && _buffer.Length > 0)
        {
            _buffer.Remove(0, Math.Min(charCount, _buffer.Length));
        }
    }
}
