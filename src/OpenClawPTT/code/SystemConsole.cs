using System;
using System.Text;

namespace OpenClawPTT;

/// <summary>
/// Production IConsole — delegates directly to System.Console.
/// </summary>
public sealed class SystemConsole : IConsole
{
    public void Write(string? text) => Console.Write(text);
    public void WriteLine(string? text = null) => Console.WriteLine(text);
    public ConsoleColor ForegroundColor
    {
        get => Console.ForegroundColor;
        set => Console.ForegroundColor = value;
    }
    public void ResetColor() => Console.ResetColor();
    public bool KeyAvailable => Console.KeyAvailable;
    public ConsoleKeyInfo ReadKey(bool intercept = false) => Console.ReadKey(intercept);
    public int WindowWidth => Console.WindowWidth;
    public Encoding OutputEncoding
    {
        get => Console.OutputEncoding;
        set => Console.OutputEncoding = value;
    }
    public bool TreatControlCAsInput
    {
        get => Console.TreatControlCAsInput;
        set => Console.TreatControlCAsInput = value;
    }

    public IAgentReplyFormatter CreateAgentReplyFormatter(string prefix, int rightMarginIndent, bool prefixAlreadyPrinted = false)
        => new AgentReplyFormatter(prefix, rightMarginIndent, prefixAlreadyPrinted);

    public IAgentReplyFormatter CreateAgentReplyFormatter(string prefix, int rightMarginIndent, bool prefixAlreadyPrinted, int consoleWidth)
        => new AgentReplyFormatter(prefix, rightMarginIndent, prefixAlreadyPrinted, consoleWidth);
}
