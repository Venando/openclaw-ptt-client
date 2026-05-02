using System.Linq;
using Spectre.Console;

namespace OpenClawPTT.Services;

/// <summary>
/// Console implementation of IToolOutput using StreamShell.
/// Routes all output through the StreamShell host as markup.
/// </summary>
public sealed class ToolOutputHelper : IToolOutput
{
    //private readonly IStreamShellHost _shellHost;

    private readonly AgentReplyFormatter _agentReplayFormatter;
    private readonly StreamShellCapturingConsole _streamShellCapturingConsole;

    private string _prefix;

    /// <summary>
    /// Creates a ToolOutputHelper.
    /// </summary>
    /// <param name="shellHost">StreamShell host for markup output.</param>
    public ToolOutputHelper(IStreamShellHost shellHost)
    {
        //_shellHost = shellHost;

        _streamShellCapturingConsole = new StreamShellCapturingConsole(shellHost);

        _agentReplayFormatter = new AgentReplyFormatter("", 10, prefixAlreadyPrinted: false, output: _streamShellCapturingConsole);

    }

    public void Start(string prefix)
    {
        _prefix = prefix;
        _agentReplayFormatter.Reconfigure(prefix);
    }

    public void Finish()
    {
        _agentReplayFormatter.Finish();
    }

    public void Flush()
    {
        _streamShellCapturingConsole.FlushToStreamShell(_prefix);
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
        
        _agentReplayFormatter.ProcessMarkupDelta($"[{colorName}]{Markup.Escape(text)}[/]");
        //_shellHost.AddMessage($"[{colorName}]{Markup.Escape(text)}[/]");
    }

    public void Print(string text, ConsoleColor color = ConsoleColor.White)
    {
        WriteToShell(text, color);
    }

    public void PrintLine(string text, ConsoleColor color = ConsoleColor.White)
    {
        WriteToShell(text + "\n", color);
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
                PrintLine(line, color);
        }
        if (hasMore)
            PrintLine($"... ({allLines.Length - 4} more lines)", ConsoleColor.DarkGray);
    }
}
