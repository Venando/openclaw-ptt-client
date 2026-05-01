using System;
using System.Linq;
using Spectre.Console;

namespace OpenClawPTT.Services;

/// <summary>
/// Console implementation of IToolOutput using System.Console.
/// When a StreamShell host is provided, routes output through it as markup.
/// </summary>
public sealed class ToolOutputHelper : IToolOutput
{
    private readonly IConsole? _console;
    private readonly IStreamShellHost? _shellHost;

    /// <summary>
    /// Creates a ToolOutputHelper.
    /// </summary>
    /// <param name="console">Optional IConsole for raw console output.</param>
    /// <param name="shellHost">Optional StreamShell host for markup output.</param>
    public ToolOutputHelper(IConsole? console = null, IStreamShellHost? shellHost = null)
    {
        _console = console;
        _shellHost = shellHost;
    }

    private void WriteToShell(string text, ConsoleColor color)
    {
        if (_shellHost == null) return;
        var colorName = color switch
        {
            ConsoleColor.Gray => "grey",
            ConsoleColor.Green => "green",
            ConsoleColor.Red => "red",
            ConsoleColor.Yellow => "yellow",
            ConsoleColor.Cyan => "cyan",
            ConsoleColor.DarkGray => "grey",
            ConsoleColor.White => "white",
            ConsoleColor.DarkYellow => "olive",
            _ => "default"
        };
        _shellHost.AddMessage($"[{colorName}]{Markup.Escape(text)}[/]");
    }

    public void Print(string text, ConsoleColor color = ConsoleColor.White)
    {
        // For StreamShell, accumulate in a single line
        if (_shellHost != null)
        {
            WriteToShell(text, color);
        }
        else if (_console != null)
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
        if (_shellHost != null)
        {
            WriteToShell(text, color);
        }
        else if (_console != null)
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

        if (_shellHost != null)
        {
            // Write each line separately to StreamShell
            foreach (var line in displayLines)
            {
                if (!string.IsNullOrWhiteSpace(line))
                    WriteToShell(line, color);
            }
            if (hasMore)
                WriteToShell($"... ({allLines.Length - 4} more lines)", ConsoleColor.DarkGray);
        }
        else
        {
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
