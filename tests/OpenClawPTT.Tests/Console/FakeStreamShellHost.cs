using OpenClawPTT.Services;
using StreamShell;

namespace OpenClawPTT.Tests;

/// <summary>
/// Testable fake IStreamShellHost that captures messages and allows firing UserInputSubmitted.
/// </summary>
public sealed class FakeStreamShellHost : IStreamShellHost, IDisposable
{
    public readonly List<string> Messages = new();
    public readonly List<Command> Commands = new();

    public event Action<string>? UserInputSubmitted;

    public void AddMessage(string markup) => Messages.Add(markup);

    public void AddCommand(Command command) => Commands.Add(command);

    public void Run() { /* no-op: tests fire events directly */ }

    public void Stop() { /* no-op */ }

    public void Dispose() { /* no-op */ }

    /// <summary>Simulate user submitting input.</summary>
    public void SubmitInput(string input) => UserInputSubmitted?.Invoke(input);
}
