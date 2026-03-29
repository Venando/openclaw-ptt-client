using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenClawPTT.Services;

public class InputHandler
{
    private readonly GatewayService _gateway;
    private readonly AudioService _audioService;
    private readonly ConfigurationService _configService;
    
    public InputHandler(GatewayService gateway, AudioService audioService, ConfigurationService configService)
    {
        _gateway = gateway;
        _audioService = audioService;
        _configService = configService;
    }
    
    public async Task<int> HandleInputAsync(CancellationToken ct)
    {
        // non-blocking key poll
        if (!Console.KeyAvailable)
        {
            await Task.Delay(50, ct);
            return 0; // Continue
        }

        var key = Console.ReadKey(intercept: true);

        if (key.Key == ConsoleKey.Q)
        {
            Console.WriteLine("  Bye!");
            return -1; // Quit
        }

        if (key.Key == ConsoleKey.T)
        {
            await HandleTypeMessageAsync(ct);
            return 0;
        }
        
        // Alt+R for reconfiguration
        if (key.Key == ConsoleKey.R && (key.Modifiers & ConsoleModifiers.Alt) != 0)
        {
            return await HandleReconfigurationAsync();
        }
        
        return 0; // Continue
    }
    
    private async Task HandleTypeMessageAsync(CancellationToken ct)
    {
        Console.Write("  ✏️  Type message: ");
        var text = Console.ReadLine()?.Trim();
        if (!string.IsNullOrEmpty(text))
            await SendTextAsync(_gateway, text, ct);
    }
    
    private async Task<int> HandleReconfigurationAsync()
    {
        ConsoleUi.PrintWarning("\nStarting reconfiguration wizard...\n");
        
        var currentCfg = _configService.Load();
        if (currentCfg != null)
        {
            var newCfg = await _configService.ReconfigureAsync(currentCfg);
            ConsoleUi.PrintSuccess("Configuration updated. Reconnecting...\n");
            return 100; // Restart code
        }
        
        return 0;
    }
    
    private static async Task SendTextAsync(GatewayService gateway, string text, CancellationToken ct)
    {
        ConsoleUi.PrintInfo("Sending… ");
        try
        {
            await gateway.SendTextAsync(text, ct);
            ConsoleUi.PrintSuccess("sent.");
        }
        catch (Exception ex)
        {
            ConsoleUi.PrintError($"failed: {ex.Message}");
        }
    }
}