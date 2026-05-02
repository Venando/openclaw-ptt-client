namespace OpenClawPTT;

using System.Net.WebSockets;
using OpenClawPTT.Services;
using StreamShell;

/// <summary>
/// Owns the top-level application composition and run loop.
/// Disposable so it can be unit-tested in isolation from Program.
/// </summary>
public class AppRunner : IDisposable
{
    private readonly AppConfig _cfg;
    private readonly IServiceFactory _factory;
    private readonly IStreamShellHost _shellHost;
    private readonly IConfigurationService _configService;
    private CancellationTokenSource? _cts;

    /// <summary>
    /// Maximum number of consecutive <see cref="AppLoopExitCode.Restart"/> responses
    /// allowed before the run loop gives up and returns an error.
    /// </summary>
    public const int MaxRestartCount = 3;

    public AppRunner(AppConfig cfg, IServiceFactory factory, IStreamShellHost shellHost, IConfigurationService configService)
    {
        _cfg = cfg;
        _factory = factory;
        _shellHost = shellHost;
        _configService = configService;
    }

    /// <summary>
    /// Runs the app. Returns exit code (0=ok, 1=error, 100=restart).
    /// </summary>
    public virtual async Task<int> RunAsync(CancellationToken ct)
    {
        int result;
        int restartCount = 0;

        _cts?.Dispose();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        do
        {
            result = await RunAppLoopAsync(_cts.Token);
            if (result == (int)AppLoopExitCode.Restart)
            {
                restartCount++;
                if (restartCount >= MaxRestartCount)
                    return (int)AppLoopExitCode.Error;
            }
        } while (result == (int)AppLoopExitCode.Restart);
        return result;
    }

    private async Task<int> RunAppLoopAsync(CancellationToken ct)
    {
        using var gateway = _factory.CreateGatewayService(_cfg);
        try
        {
            await gateway.ConnectAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // Cancellation is not an application error — let it propagate.
        }
        catch (IOException)
        {
            return (int)AppLoopExitCode.Error;
        }
        catch (WebSocketException)
        {
            return (int)AppLoopExitCode.Error;
        }
        return await RunPttLoopAsync(gateway, ct);
    }

    private async Task<int> RunPttLoopAsync(IGatewayService gateway, CancellationToken ct)
    {
        using var audioService = _factory.CreateAudioService(_cfg);
        var textSender = _factory.CreateTextMessageSender(gateway);
        var inputHandler = _factory.CreateInputHandler(textSender);

        // Agent settings (loaded in AppBootstrapper, already merged into AgentRegistry)
        var pttController = new PttController();

        using var agentHotkeyService = new AgentHotkeyService(
            pttController, textSender, _shellHost, _cfg,
            gatewayService: gateway);

        ConsoleUi.PrintHelpMenu(_cfg);

        // Register StreamShell commands (/quit, /reconfigure) before PTT loop
        using var shellCommands = new StreamShellInputHandler(
            _shellHost,
            textSender,
            gateway,
            _configService,
            _cfg,
            onQuit: () => _cts?.Cancel()
        );
        shellCommands.Register();
        ConsoleUi.Log("debug", "[History] StreamShell registered, initial history will be fetched");

        using IAppLoop pttLoop = _factory.CreatePttLoop(
            audioService, pttController, textSender, inputHandler,
            requireConfirmBeforeSend: _cfg.RequireConfirmBeforeSend);

        return (int)(await pttLoop.RunAsync(ct));
    }

    public void Dispose()
    {
        _cts?.Dispose();
        _cts = null;
    }
}