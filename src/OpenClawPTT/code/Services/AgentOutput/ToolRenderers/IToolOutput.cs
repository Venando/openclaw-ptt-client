namespace OpenClawPTT.Services;

/// <summary>
/// Abstraction for tool output operations, enabling testability and
/// alternate render targets (e.g. string builders for testing).
/// </summary>`
public interface IToolOutput
{
    void Start(string prefix);
    void Print(string text, ConsoleColor color = ConsoleColor.White);
    void PrintLine(string text, ConsoleColor color = ConsoleColor.White);
    void PrintTruncated(string text, string continuationPrefix, int rightMarginIndent, ConsoleColor color = ConsoleColor.White, int maxRows = 4);
    void PrintMarkup(string markup);
    void Finish();
    void Flush();
}
