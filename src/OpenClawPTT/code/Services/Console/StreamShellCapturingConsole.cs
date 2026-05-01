using System;
using System.Text;
using System.Threading;
using Spectre.Console;

namespace OpenClawPTT.Services;

/// <summary>
/// An IConsole wrapper that captures all Write/WriteLine output into a StringBuffer,
/// then pushes the complete result as a single StreamShell message when <see cref="FlushAsync"/>
/// is called. Useful for word-wrapped agent replies that need to appear in StreamShell.
/// </summary>
public sealed class StreamShellCapturingConsole : IConsole
{
    private readonly IStreamShellHost _shellHost;
    private readonly StringBuilder _buffer = new StringBuilder();

    public StreamShellCapturingConsole(IStreamShellHost shellHost)
    {
        _shellHost = shellHost;
    }

    /// <summary>Push the captured output to StreamShell and clear the buffer.</summary>
    public void FlushToStreamShell(string? prefixColor = null)
    {
        if (_buffer.Length == 0)
            return;

        var text = _buffer.ToString().TrimEnd();
        if (string.IsNullOrEmpty(text))
            return;

        // Wrap in color markup if requested
        if (!string.IsNullOrEmpty(prefixColor))
            _shellHost.AddMessage($"[{prefixColor}]{Markup.Escape(text)}[/]");
        else
            _shellHost.AddMessage(text);

        _buffer.Clear();
    }

    // ── IConsole — capture all write operations ──

    public void Write(string? text) => _buffer.Append(text);

    public void WriteLine(string? text = null)
    {
        _buffer.AppendLine(text);
    }

    public int WindowWidth
    {
        get
        {
            try { return Console.WindowWidth; }
            catch { return 80; }
        }
    }

    // Unused by AgentReplyFormatter — throw if called
    public ConsoleColor ForegroundColor
    {
        get => ConsoleColor.Gray;
        set { /* captured at Flush time, not during streaming */ }
    }
    public void ResetColor() { }
    public bool KeyAvailable => throw new NotSupportedException();
    public ConsoleKeyInfo ReadKey(bool intercept = false) => throw new NotSupportedException();
    public Encoding OutputEncoding
    {
        get => Encoding.UTF8;
        set => throw new NotSupportedException();
    }
    public bool TreatControlCAsInput
    {
        get => false;
        set => throw new NotSupportedException();
    }
    public IAgentReplyFormatter CreateAgentReplyFormatter(string prefix, int rightMarginIndent, bool prefixAlreadyPrinted = false)
        => throw new NotSupportedException();
    public IAgentReplyFormatter CreateAgentReplyFormatter(string prefix, int rightMarginIndent, bool prefixAlreadyPrinted, int consoleWidth)
        => throw new NotSupportedException();
    public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
}
