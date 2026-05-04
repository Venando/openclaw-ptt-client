namespace OpenClawPTT;

/// <summary>
/// Simple logging abstraction for diagnostic output.
/// </summary>
public interface ILogger
{
    /// <summary>Logs an informational message.</summary>
    void Log(string source, string message);

    /// <summary>Logs an error message.</summary>
    void LogError(string source, string message);
}
