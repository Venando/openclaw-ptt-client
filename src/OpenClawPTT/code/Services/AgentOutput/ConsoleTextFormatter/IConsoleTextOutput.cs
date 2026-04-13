namespace OpenClawPTT.Services;

/// <summary>
/// Abstraction for text output operations, enabling AgentReplyFormatter
/// to be tested without writing to the real console.
/// </summary>
public interface IConsoleTextOutput
{
    void Write(string? text);
    void WriteLine();
    int WindowWidth { get; }
}
