namespace OpenClawPTT.Services;

/// <summary>Abstraction for console output operations, enabling testability.</summary>
public interface IConsoleOutput
{
    void PrintBanner();
    void PrintHelpMenu(string hotkeyCombination, bool holdToTalk);
    void PrintRecordingIndicator(bool isRecording, string hotkeyCombination, bool holdToTalk);
    void PrintSuccess(string message);
    void PrintSuccessWordWrap(string prefix, string message, int rightMarginIndent);
    void PrintWarning(string message);
    void PrintError(string message);
    void PrintInfo(string message);
    void PrintInlineInfo(string message);
    void PrintInlineSuccess(string message);
    void PrintGatewayError(string message, string? detailCode, string? recommendedStep);
    void PrintAgentReply(string prefix, string body);
    void PrintAgentReplyDelta(string prefix, string delta, string newlineSuffix);
    OpenClawPTT.AgentReplyFormatter CreateAgentReplyFormatter(string prefix, int rightMarginIndent, bool prefixAlreadyPrinted = false);
    OpenClawPTT.AgentReplyFormatter CreateAgentReplyFormatter(string prefix, int rightMarginIndent, bool prefixAlreadyPrinted, int consoleWidth);
    void Log(string tag, string msg);
    void LogOk(string tag, string msg);
    void LogError(string tag, string msg);
}
