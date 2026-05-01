using System;
using System.Text;
using Spectre.Console;

namespace OpenClawPTT.Services;

/// <summary>
/// An IFormattedOutput wrapper that captures all Write/WriteLine output into a StringBuffer,
/// then pushes the complete result as a single StreamShell message when <see cref="FlushAsync"/>
/// is called. Useful for word-wrapped agent replies that need to appear in StreamShell.
/// </summary>
public sealed class StreamShellCapturingConsole : IFormattedOutput
{
    private readonly IStreamShellHost _shellHost;
    private readonly StringBuilder _buffer = new StringBuilder();

    public StreamShellCapturingConsole(IStreamShellHost shellHost)
    {
        _shellHost = shellHost;
    }

    /// <summary>
    /// Push the captured output to StreamShell and clear the buffer.
    /// The <paramref name="cyanPrefix"/> is rendered in cyan, and the captured body
    /// (everything after the prefix) is rendered in the default StreamShell color.
    /// </summary>
    public void FlushToStreamShell(string cyanPrefix)
    {
        if (_buffer.Length == 0)
            return;

        var text = _buffer.ToString().TrimEnd();
        if (string.IsNullOrEmpty(text))
            return;

        // First part of captured text is the prefix (already written via _consoleOutput.Write)
        // Everything after is the agent reply body.
        // Render prefix in cyan, body in default color, separated by newlines for clarity.
        _shellHost.AddMessage($"[cyan]{Markup.Escape(cyanPrefix)}[/]");

        // Split body into lines and add each as a default-color message
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            _shellHost.AddMessage(Markup.Escape(line));
        }

        _buffer.Clear();
    }

    // ── IFormattedOutput — capture all write operations ──

    public void Write(string text) => _buffer.Append(text);

    public void WriteLine() => _buffer.AppendLine();

    public int WindowWidth
    {
        get
        {
            try { return Console.WindowWidth; }
            catch { return 80; }
        }
    }
}
