namespace OpenClawPTT;

using OpenClawPTT.Services;
using System.Threading;

internal static class Program
{
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

            using var runner = new AppRunner(cfg, factory);
            return await runner.RunAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("\n  Shutting down.");
            Console.ReadKey();
            return 0;
        }
        catch (GatewayException gex)
        {
            ConsoleUi.PrintGatewayError(gex.Message, gex.DetailCode, gex.RecommendedStep);
            Console.ReadKey();
            return 1;
        }
        catch (Exception ex)
        {
            ConsoleUi.PrintError($"Fatal: {ex.Message}");
#if DEBUG
            Console.WriteLine(ex.StackTrace);
#endif
            Console.ReadKey();
            return 1;
        }
    }
}
