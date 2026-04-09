namespace OpenClawPTT;

using OpenClawPTT.Services;
using System.Linq;
using System.Threading;

internal static class Program
{
    private const int ExitOk = 0;
    private const int ExitError = 1;
    private const int ExitRestart = 100;

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        ConsoleUi.PrintBanner();

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        try
        {
            var configService = new ConfigurationService();
            var console = new ConsoleOutput();
            var factory = new ServiceFactory(configService, console);
            var cfg = await configService.LoadOrSetupAsync();

            int result;
            do
            {
                result = await RunAppLoop(cfg, factory, cts.Token);
            } while (result == ExitRestart);

            return result;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("\n  Shutting down.");
            Console.ReadKey();
            return ExitOk;
        }
        catch (GatewayException gex)
        {
            ConsoleUi.PrintGatewayError(gex.Message, gex.DetailCode, gex.RecommendedStep);
            Console.ReadKey();
            return ExitError;
        }
        catch (Exception ex)
        {
            ConsoleUi.PrintError($"Fatal: {ex.Message}");
#if DEBUG
            Console.WriteLine(ex.StackTrace);
#endif
            Console.ReadKey();
            return ExitError;
        }
    }

    private static async Task<int> RunAppLoop(AppConfig cfg, ServiceFactory factory, CancellationToken ct)
    {
        using var gateway = factory.CreateGatewayService(cfg);
        await gateway.ConnectAsync(ct);
        return await RunPttLoop(cfg, factory, gateway, ct);
    }

    private static async Task<int> RunPttLoop(
        AppConfig cfg,
        ServiceFactory factory,
        GatewayService gateway,
        CancellationToken ct)
    {
        using var audioService = factory.CreateAudioService(cfg);
        var pttController = factory.CreatePttController(cfg, audioService);
        var textSender = factory.CreateTextMessageSender(gateway);
        var inputHandler = factory.CreateInputHandler(gateway, audioService, textSender);

        ConsoleUi.PrintHelpMenu(cfg.HotkeyCombination, cfg.HoldToTalk);

        using var pttLoop = factory.CreatePttLoop(cfg, gateway, audioService, pttController, textSender, inputHandler);
        return (int)(await pttLoop.RunAsync(ct));
    }
}
