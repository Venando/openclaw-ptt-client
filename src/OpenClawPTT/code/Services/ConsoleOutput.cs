namespace OpenClawPTT.Services;

/// <summary>Production implementation delegating to static ConsoleUi.</summary>
public sealed class ConsoleOutput : IConsoleOutput
{
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

    public OpenClawPTT.AgentReplyFormatter CreateAgentReplyFormatter(string prefix, int rightMarginIndent, bool prefixAlreadyPrinted = false)
        => ConsoleUi.CreateAgentReplyFormatter(prefix, rightMarginIndent, prefixAlreadyPrinted);

    public void Log(string tag, string msg) => ConsoleUi.Log(tag, msg);

    public void LogOk(string tag, string msg) => ConsoleUi.LogOk(tag, msg);

    public void LogError(string tag, string msg) => ConsoleUi.LogError(tag, msg);
}
