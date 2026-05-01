using OpenClawPTT.Services;

namespace OpenClawPTT;

internal static class Program
{
    private static async Task<int> Main()
    {
        var cts = new CancellationTokenSource();
        IConfigurationService configService = new ConfigurationService();
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var shellHost = new StreamShellHost();
        var consoleOutput = new StreamShellConsoleOutput(shellHost);
        var factory = new ServiceFactory(configService, consoleOutput, new MessageComposer(), shellHost);

        ConsoleUi.SetStreamShellHost(shellHost);

        var bootstrapper = new AppBootstrapper(configService, factory, shellHost);
        var exitCode = await bootstrapper.RunAsync(cts.Token);

        bootstrapper.Dispose();
        shellHost.Dispose();
        cts.Dispose();

        return exitCode;
    }
}
