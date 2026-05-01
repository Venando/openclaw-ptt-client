using System.Linq;
using Spectre.Console;

namespace OpenClawPTT.Services;

/// <summary>
/// Console implementation of IToolOutput using StreamShell.
/// Routes all output through the StreamShell host as markup.
/// </summary>
public sealed class ToolOutputHelper : IToolOutput
{
    private readonly IStreamShellHost _shellHost;

    /// <summary>
    /// Creates a ToolOutputHelper.
    /// </summary>
    /// <param name="shellHost">StreamShell host for markup output.</param>
    public ToolOutputHelper(IStreamShellHost shellHost)
    {
        _shellHost = shellHost;
    }

    private void WriteToShell(string text, ConsoleColor color)
    {
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
        WriteToShell(text, color);
    }

    public void PrintLine(string text, ConsoleColor color = ConsoleColor.White)
    {
        WriteToShell(text, color);
    }

    public void PrintTruncated(string text, string continuationPrefix, int rightMarginIndent, ConsoleColor color = ConsoleColor.White)
    {
        if (string.IsNullOrEmpty(text)) return;

        var allLines = text.Split('\n');
        var displayLines = allLines.Take(4).ToArray();
        bool hasMore = allLines.Length > 4;

        foreach (var line in displayLines)
        {
            if (!string.IsNullOrWhiteSpace(line))
                WriteToShell(line, color);
        }
        if (hasMore)
            WriteToShell($"... ({allLines.Length - 4} more lines)", ConsoleColor.DarkGray);
    }

    public void ResetColor() { }
}
