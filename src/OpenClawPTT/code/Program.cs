using OpenClawPTT.Services;

namespace OpenClawPTT;

internal static class Program
{
    private static async Task<int> Main()
    {
        var cts = new CancellationTokenSource();
        IConsole console = new SystemConsole();
        IConfigurationService configService = new ConfigurationService();
        var factory = new ServiceFactory(configService);

        var bootstrapper = new AppBootstrapper(console, configService, factory);
        var exitCode = await bootstrapper.RunAsync(cts.Token);

        bootstrapper.Dispose();
        cts.Dispose();

        return exitCode;
    }
}
