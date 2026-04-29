using StreamShell;

namespace OpenClawPTT.Services;

/// <summary>
/// Testable abstraction over StreamShell's ConsoleAppHost.
/// </summary>
public interface IStreamShellHost
{
    void AddMessage(string markup);
    void AddCommand(Command command);
    event Action<string>? UserInputSubmitted;
    void Run();
    void Stop();
}
