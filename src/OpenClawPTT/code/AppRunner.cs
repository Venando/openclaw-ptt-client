namespace OpenClawPTT;

using OpenClawPTT.Services;

/// <summary>
/// Owns the top-level application composition and run loop.
/// Disposable so it can be unit-tested in isolation from Program.
/// </summary>
public sealed class AppRunner : IDisposable
{
    private readonly AppConfig _cfg;
    private readonly IServiceFactory _factory;

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
        do
        {
            result = await RunAppLoopAsync(ct);
        } while (result == 100); // Restart
        return result;
    }

    private async Task<int> RunAppLoopAsync(CancellationToken ct)
    {
        using var gateway = _factory.CreateGatewayService(_cfg);
        await gateway.ConnectAsync(ct);
        return await RunPttLoopAsync(gateway, ct);
    }

    private async Task<int> RunPttLoopAsync(IGatewayService gateway, CancellationToken ct)
    {
        using var audioService = _factory.CreateAudioService(_cfg);
        var pttController = _factory.CreatePttController(_cfg, audioService);
        var textSender = _factory.CreateTextMessageSender(gateway);
        var inputHandler = _factory.CreateInputHandler(gateway, audioService, textSender);

        ConsoleUi.PrintHelpMenu(_cfg.HotkeyCombination, _cfg.HoldToTalk);

        using var pttLoop = _factory.CreatePttLoop(
            _cfg, gateway, audioService,
            pttController, textSender, inputHandler);

        return (int)(await pttLoop.RunAsync(ct));
    }

    public void Dispose()
    {
        // Nothing to dispose at runner level — all owned disposables
        // are disposed in their respective loops.
    }
}
