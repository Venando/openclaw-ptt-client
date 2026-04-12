using System;
using System.Text;

namespace OpenClawPTT.Services;

/// <summary>
/// IConsoleOutput implementation that delegates to the static ConsoleUi facade.
/// Also implements IConsole so callers that need IConsoleOutput get IConsole too.
/// </summary>
public sealed class ConsoleUiOutput : IConsoleOutput
{
    // IConsole
    public void Write(string? text) => ConsoleUi.Write(text);
    public void WriteLine(string? text = null) => ConsoleUi.WriteLine(text);
    public ConsoleColor ForegroundColor
    {
        get => ConsoleUi.ForegroundColor;
        set => ConsoleUi.ForegroundColor = value;
    }
    public void ResetColor() => ConsoleUi.ResetColor();
    public bool KeyAvailable => ConsoleUi.KeyAvailable;
    public ConsoleKeyInfo ReadKey(bool intercept = false) => ConsoleUi.ReadKey(intercept);
    public int WindowWidth => ConsoleUi.WindowWidth;
    public Encoding OutputEncoding
    {
        get => ConsoleUi.OutputEncoding;
        set => ConsoleUi.OutputEncoding = value;
    }
    public bool TreatControlCAsInput
    {
        get => ConsoleUi.TreatControlCAsInput;
        set => ConsoleUi.TreatControlCAsInput = value;
    }
    public IAgentReplyFormatter CreateAgentReplyFormatter(string prefix, int rightMarginIndent, bool prefixAlreadyPrinted = false)
        => ConsoleUi.CreateAgentReplyFormatter(prefix, rightMarginIndent, prefixAlreadyPrinted);
    public IAgentReplyFormatter CreateAgentReplyFormatter(string prefix, int rightMarginIndent, bool prefixAlreadyPrinted, int consoleWidth)

        => ConsoleUi.CreateAgentReplyFormatter(prefix, rightMarginIndent, prefixAlreadyPrinted, consoleWidth);

    // IConsoleOutput display methods
    public void PrintBanner() => ConsoleUi.PrintBanner();
    public void PrintHelpMenu(string hotkeyCombination, bool holdToTalk)
        => ConsoleUi.PrintHelpMenu(hotkeyCombination, holdToTalk);
    public void PrintRecordingIndicator(bool isRecording, string hotkeyCombination, bool holdToTalk)
        => ConsoleUi.PrintRecordingIndicator(isRecording, hotkeyCombination, holdToTalk);
    public void PrintSuccess(string message) => ConsoleUi.PrintSuccess(message);
    public void PrintSuccessWordWrap(string prefix, string message, int rightMarginIndent)
        => ConsoleUi.PrintSuccessWordWrap(prefix, message, rightMarginIndent);
    public void PrintWarning(string message) => ConsoleUi.PrintWarning(message);
    public void PrintError(string message) => ConsoleUi.PrintError(message);
    public void PrintInfo(string message) => ConsoleUi.PrintInfo(message);
    public void PrintInlineInfo(string message) => ConsoleUi.PrintInlineInfo(message);
    public void PrintInlineSuccess(string message) => ConsoleUi.PrintInlineSuccess(message);
    public void PrintGatewayError(string message, string? detailCode, string? recommendedStep)
        => ConsoleUi.PrintGatewayError(message, detailCode, recommendedStep);
    public void PrintAgentReply(string prefix, string body)
        => ConsoleUi.PrintAgentReply(prefix, body);
    public void PrintAgentReplyDelta(string prefix, string delta, string newlineSuffix)
        => ConsoleUi.PrintAgentReplyDelta(prefix, delta, newlineSuffix);
    public void Log(string tag, string msg) => ConsoleUi.Log(tag, msg);
    public void LogOk(string tag, string msg) => ConsoleUi.LogOk(tag, msg);
    public void LogError(string tag, string msg) => ConsoleUi.LogError(tag, msg);
    public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default)
        => Console.In.ReadLineAsync(cancellationToken);
}
