namespace OpenClawPTT;

/// <summary>
/// Maps exceptions to process exit codes and orchestrates the shutdown user experience
/// (error messages, key-press waits).
/// </summary>
public sealed class AppExitHandler : IDisposable
{
    /// <summary>Returned when the application exits normally after cancellation.</summary>
    public const int ExitCancelled = 0;

    /// <summary>Returned when the application exits due to a handled error.</summary>
    public const int ExitError = 1;

    public AppExitHandler() { }

    /// <summary>
    /// Handles <paramref name="ex"/> and returns the appropriate process exit code.
    /// </summary>
    public int HandleExit(Exception? ex)
    {
        switch (ex)
        {
            case OperationCanceledException:
                return ExitCancelled;

            case GatewayException gex:
                ConsoleUi.PrintGatewayError(gex.Message, gex.DetailCode, gex.RecommendedStep);
                Console.ReadKey(intercept: true);
                return ExitError;

            case Exception ex2:
                ConsoleUi.PrintError($"Fatal: {ex2.Message}. Press any button");
#if DEBUG
                Console.WriteLine(ex2.StackTrace);
#endif
                Console.ReadKey(intercept: true);
                return ExitError;

            default:
                return ExitCancelled;
        }
    }

    public void Dispose() { }
}
