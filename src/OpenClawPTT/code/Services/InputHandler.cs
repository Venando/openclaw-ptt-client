using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenClawPTT.Services;

public sealed class InputHandler : IInputHandler
{
    private readonly ITextMessageSender _textSender;
    private readonly IConfigurationService _configService;
    private readonly IConsoleOutput _console;

    public InputHandler(ITextMessageSender textSender, IConfigurationService configService, IConsoleOutput console)
    {
        _textSender = textSender;
        _configService = configService;
        _console = console;
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
        Console.WriteLine();
        Console.Write("  ✏️  Type message: ");
        var text = Console.ReadLine()?.Trim();
        if (!string.IsNullOrEmpty(text))
            await _textSender.SendAsync(text, ct);
    }

    private async Task<int> HandleReconfigurationAsync()
    {
        _console.PrintWarning("\nStarting reconfiguration wizard...\n");

        var currentCfg = _configService.Load();
        if (currentCfg != null)
        {
            await _configService.ReconfigureAsync(currentCfg);
            _console.PrintSuccess("Configuration updated. Reconnecting...\n");
            return 100; // Restart code
        }

        return 0;
    }
}