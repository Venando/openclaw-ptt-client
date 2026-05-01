using System;
using System.Linq;

namespace OpenClawPTT.Services;

/// <summary>
/// Console implementation of IToolOutput using System.Console.
/// </summary>
public sealed class ToolOutputHelper : IToolOutput
{
    private readonly IConsole? _console;

    /// <summary>
    /// Creates a ToolOutputHelper. When auto-detect is true, uses System.Console.
    /// </summary>
    /// <param name="console">Optional IConsole override for injectable output.</param>
    public ToolOutputHelper(IConsole? console = null)
    {
        _console = console;
    }

    public void Print(string text, ConsoleColor color = ConsoleColor.White)
    {
        if (_console != null)
        {
            _console.ForegroundColor = color;
            _console.Write(text);
        }
        else
        {
            Console.ForegroundColor = color;
            Console.Write(text);
        }
    }

    public void PrintLine(string text, ConsoleColor color = ConsoleColor.White)
    {
        if (_console != null)
        {
            _console.ForegroundColor = color;
            _console.WriteLine(text);
        }
        else
        {
            Console.ForegroundColor = color;
            Console.WriteLine(text);
        }
    }

    public void PrintTruncated(string text, string continuationPrefix, int rightMarginIndent, ConsoleColor color = ConsoleColor.White)
    {
        if (string.IsNullOrEmpty(text)) return;

        var allLines = text.Split('\n');
        var displayLines = allLines.Take(4).ToArray();
        var displayContent = string.Join("\n", displayLines);
        bool hasMore = allLines.Length > 4;

        if (_console != null)
            _console.ForegroundColor = color;
        else
            Console.ForegroundColor = color;

        var consoleWidth = _console?.WindowWidth ?? GetConsoleWidth();
        var formatter = new AgentReplyFormatter(continuationPrefix, rightMarginIndent, prefixAlreadyPrinted: true, consoleWidth, _console);
        formatter.ProcessDelta(displayContent);
        formatter.Finish();

        if (_console != null)
            _console.ResetColor();
        else
            Console.ResetColor();

        if (hasMore)
        {
            if (_console != null)
            {
                _console.ForegroundColor = ConsoleColor.DarkGray;
                _console.Write($"  ... ({allLines.Length - 4} more lines)");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($"  ... ({allLines.Length - 4} more lines)");
            }
        }
    }

    public void ResetColor()
    {
        if (_console != null)
            _console.ResetColor();
        else
            Console.ResetColor();
    }

    private static int GetConsoleWidth()
    {
        try { return Console.WindowWidth; }
        catch { return 80; }
    }
}
