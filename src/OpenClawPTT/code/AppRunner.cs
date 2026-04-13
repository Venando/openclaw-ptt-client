namespace OpenClawPTT;

using System.Net.WebSockets;
using OpenClawPTT.Services;

/// <summary>
/// Owns the top-level application composition and run loop.
/// Disposable so it can be unit-tested in isolation from Program.
/// </summary>
public sealed class AppRunner : IDisposable
{
    private readonly AppConfig _cfg;
    private readonly IServiceFactory _factory;

    /// <summary>
    /// Maximum number of consecutive <see cref="AppLoopExitCode.Restart"/> responses
    /// allowed before the run loop gives up and returns an error.
    /// </summary>
    public const int MaxRestartCount = 3;

    public AppRunner(AppConfig cfg, IServiceFactory factory)
    {
        _cfg = cfg;
        _factory = factory;
    }

    /// <summary>
    /// Runs the app. Returns exit code (0=ok, 1=error, 100=restart).
    /// </summary>
    public async Task<int> RunAsync(CancellationToken ct)
    {
        int result;
        int restartCount = 0;
        do
        {
            result = await RunAppLoopAsync(ct);
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
        var pttController = _factory.CreatePttController(_cfg, audioService);
        var textSender = _factory.CreateTextMessageSender(gateway);
        var inputHandler = _factory.CreateInputHandler(textSender);

        ConsoleUi.PrintHelpMenu(_cfg.HotkeyCombination, _cfg.HoldToTalk);

        using IAppLoop pttLoop = _factory.CreatePttLoop(
            audioService, pttController, textSender, inputHandler);

        return (int)(await pttLoop.RunAsync(ct));
    }

    public void Dispose()
    {
        // Nothing to dispose at runner level — all owned disposables
        // are disposed in their respective loops.
    }
}