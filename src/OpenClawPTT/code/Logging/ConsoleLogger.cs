namespace OpenClawPTT;

/// <summary>
/// ILogger implementation using ConsoleUi for output.
/// </summary>
public sealed class ConsoleLogger : ILogger
{
    /// <inheritdoc />
    public void Log(string source, string message)
    {
        ConsoleUi.Log(source, message);
    }

    /// <inheritdoc />
    public void LogError(string source, string message)
    {
        ConsoleUi.LogError(source, message);
    }
}
