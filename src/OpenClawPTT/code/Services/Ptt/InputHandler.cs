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
    
    public async Task<InputResult> HandleInputAsync(CancellationToken ct)
    {
        // non-blocking key poll
        if (!ConsoleUi.KeyAvailable)
        {
            try { await Task.Delay(50, ct); }
            catch (TaskCanceledException) { return InputResult.Continue; }
            return InputResult.Continue;
        }

        var key = ConsoleUi.ReadKey(intercept: true);

        if (key.Key == ConsoleKey.Q)
        {
            ConsoleUi.WriteLine("  Bye!");
            return InputResult.Quit;
        }

        if (key.Key == ConsoleKey.T)
        {
            await HandleTypeMessageAsync(ct);
            return InputResult.Continue;
        }
        
        // Alt+R for reconfiguration
        if (key.Key == ConsoleKey.R && (key.Modifiers & ConsoleModifiers.Alt) != 0)
        {
            return await HandleReconfigurationAsync();
        }
        
        return InputResult.Continue;
    }
    
    private async Task HandleTypeMessageAsync(CancellationToken ct)
    {
        try
        {
            ConsoleUi.WriteLine();
            ConsoleUi.Write("  ✏️  Type message: ");
            var text = (await ConsoleUi.ReadLineAsync(ct))?.Trim();
            if (!string.IsNullOrEmpty(text))
                await _textSender.SendAsync(text, ct);
        }
        catch (Exception)
        {
            // Swallow all errors — typing a message is best-effort
        }
    }

    private async Task<InputResult> HandleReconfigurationAsync()
    {
        _console.PrintWarning("\nStarting reconfiguration wizard...\n");

        var currentCfg = _configService.Load();
        if (currentCfg != null)
        {
            await _configService.ReconfigureAsync(currentCfg);
            _console.PrintSuccess("Configuration updated. Reconnecting...\n");
            return InputResult.Restart;
        }

        return InputResult.Continue;
    }
}