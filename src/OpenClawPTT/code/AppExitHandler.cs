namespace OpenClawPTT;

/// <summary>
/// Maps exceptions to process exit codes and orchestrates the shutdown user experience
/// (error messages, key-press waits). Inject an <see cref="IConsole"/> mock in tests
/// to suppress console I/O and capture output via <see cref="ConsoleUi.SetConsole"/>.
/// </summary>
public sealed class AppExitHandler : IDisposable
{
    /// <summary>Returned when the application exits normally after cancellation.</summary>
    public const int ExitCancelled = 0;

    /// <summary>Returned when the application exits due to a handled error.</summary>
    public const int ExitError = 1;

    private readonly IConsole _console;

    public AppExitHandler(IConsole console)
    {
        _console = console;
    }

    /// <summary>
    /// Handles <paramref name="ex"/> and returns the appropriate process exit code.
    /// Calls <see cref="IConsole.ReadKey"/> for error exits so tests can inject a silent mock.
    /// </summary>
    public int HandleExit(Exception? ex)
    {
        switch (ex)
        {
            case OperationCanceledException:
                return ExitCancelled;

            case GatewayException gex:
                ConsoleUi.PrintGatewayError(gex.Message, gex.DetailCode, gex.RecommendedStep);
                _console.ReadKey(intercept: true);
                return ExitError;

            case Exception ex2:
                ConsoleUi.PrintError($"Fatal: {ex2.Message}. Press any button");
#if DEBUG
                _console.WriteLine(ex2.StackTrace);
#endif
                _console.ReadKey(intercept: true);
                return ExitError;

            default:
                return ExitCancelled;
        }
    }

    public void Dispose() { }
}
