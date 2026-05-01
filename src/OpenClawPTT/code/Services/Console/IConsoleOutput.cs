namespace OpenClawPTT.Services;

/// <summary>Abstraction for console display output operations, enabling testability.</summary>
public interface IConsoleOutput
{
    void PrintBanner();
    void PrintHelpMenu(string hotkeyCombination, bool holdToTalk);
    void PrintRecordingIndicator(bool isRecording, string hotkeyCombination, bool holdToTalk);
    void PrintUserMessage(string text);
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
    ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default);
    void Log(string tag, string msg);
    void LogOk(string tag, string msg);
    void LogError(string tag, string msg);
}
