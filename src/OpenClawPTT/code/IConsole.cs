using System;
using System.Text;

namespace OpenClawPTT;

/// <summary>
/// Abstracts System.Console for testability. Production implementation: SystemConsole.
/// Tests can substitute a mock to capture/suppress output.
/// </summary>
public interface IConsole
{
    void Write(string? text);
    void WriteLine(string? text = null);
    ConsoleColor ForegroundColor { get; set; }
    void ResetColor();
    bool KeyAvailable { get; }
    ConsoleKeyInfo ReadKey(bool intercept = false);
    int WindowWidth { get; }
    Encoding OutputEncoding { get; set; }
    bool TreatControlCAsInput { get; set; }

    IAgentReplyFormatter CreateAgentReplyFormatter(string prefix, int rightMarginIndent, bool prefixAlreadyPrinted = false);
    IAgentReplyFormatter CreateAgentReplyFormatter(string prefix, int rightMarginIndent, bool prefixAlreadyPrinted, int consoleWidth);
}
