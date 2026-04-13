using System;
using System.Linq;

namespace OpenClawPTT.Services;

/// <summary>
/// Console implementation of IToolOutput using System.Console.
/// </summary>
public sealed class ToolOutputHelper : IToolOutput
{
    public void Print(string text, ConsoleColor color = ConsoleColor.White)
    {
        Console.ForegroundColor = color;
        Console.Write(text);
    }

    public void PrintLine(string text, ConsoleColor color = ConsoleColor.White)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(text);
    }

    public void PrintTruncated(string text, string continuationPrefix, int rightMarginIndent, ConsoleColor color = ConsoleColor.White)
    {
        if (string.IsNullOrEmpty(text)) return;

        var allLines = text.Split('\n');
        var displayLines = allLines.Take(4).ToArray();
        var displayContent = string.Join("\n", displayLines);
        bool hasMore = allLines.Length > 4;

        Console.ForegroundColor = color;
        var formatter = new AgentReplyFormatter(continuationPrefix, rightMarginIndent, prefixAlreadyPrinted: true);
        formatter.ProcessDelta(displayContent);
        formatter.Finish();
        Console.ResetColor();

        if (hasMore)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"  ... ({allLines.Length - 4} more lines)");
            Console.ResetColor();
        }
    }

    public void ResetColor() => Console.ResetColor();
}
