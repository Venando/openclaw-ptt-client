using OpenClawPTT.Services;

namespace OpenClawPTT;

/// <summary>
/// Orchestrates application startup: console configuration, cancellation setup,
/// service resolution, and the run loop.
/// </summary>
public sealed class AppBootstrapper : IDisposable
{
    private readonly IConsole _console;
    private readonly IServiceFactory _factory;
    private readonly IConfigurationService _configService;
    private readonly IStreamShellHost _shellHost;
    private readonly Func<AppConfig, IServiceFactory, AppRunner> _runnerFactory;
    private CancellationTokenSource? _cts;

    public AppBootstrapper(
        IConsole console,
        IConfigurationService configService,
        IServiceFactory factory,
        IStreamShellHost? shellHost = null,
        Func<AppConfig, IServiceFactory, AppRunner>? runnerFactory = null)
    {
        _console = console;
        _configService = configService;
        _factory = factory;
        _shellHost = shellHost ?? new StreamShellHost();
        _runnerFactory = runnerFactory ?? ((cfg, f) => new AppRunner(cfg, f));
    }

    /// <summary>Runs the application and returns the exit code.</summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        _console.OutputEncoding = System.Text.Encoding.UTF8;
        ConsoleUi.PrintBanner();

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Console.CancelKeyPress += OnCancelKeyPress;

        Exception? ex = null;
        int runnerExitCode = 0;

        try
        {
            // Start StreamShell UI (non-blocking)
            _ = Task.Run(() => _shellHost.Run(), _cts.Token);

            var cfg = await _configService.LoadOrSetupAsync(_shellHost);


            using var runner = _runnerFactory(cfg, _factory);
            runnerExitCode = await runner.RunAsync(_cts.Token);
        }
        catch (Exception caught)
        {
            ex = caught;
        }

        if (runnerExitCode != 0)
            return runnerExitCode;

        if (ex != null)
            return new AppExitHandler(_console).HandleExit(ex);

        return 0;
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
        _cts?.Cancel();
    }

    public void Dispose()
    {
        Console.CancelKeyPress -= OnCancelKeyPress;
        _cts?.Dispose();
    }
}
