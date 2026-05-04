using OpenClawPTT.Services;

namespace OpenClawPTT;

/// <summary>
/// ILogger implementation using IColorConsole for output.
/// </summary>
public sealed class ConsoleLogger : ILogger
{
    private readonly IColorConsole _console;

    /// <summary>
    /// Creates a new ConsoleLogger instance with the specified IColorConsole.
    /// </summary>
    public ConsoleLogger(IColorConsole console)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
    }

    /// <inheritdoc />
    public void Log(string source, string message)
    {
        _console.Log(source, message);
    }

    /// <inheritdoc />
    public void LogError(string source, string message)
    {
        _console.LogError(source, message);
    }
}
