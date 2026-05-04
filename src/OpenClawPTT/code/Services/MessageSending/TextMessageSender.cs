namespace OpenClawPTT.Services;

/// <summary>Unified text message sender that sends text directly to the gateway.</summary>
public sealed class TextMessageSender : ITextMessageSender
{
    private readonly IGatewayService _gateway;
    private readonly IColorConsole _console;

    public TextMessageSender(IGatewayService gateway, IColorConsole console)
    {
        _gateway = gateway;
        _console = console;
    }

    public async Task SendAsync(string text, CancellationToken ct, bool printMessage)
    {
        try
        {
            if (printMessage)
            {
                _console.PrintUserMessage(text);
            }
            await _gateway.SendTextAsync(text, ct);
        }
        catch (Exception ex)
        {
            _console.PrintError($"failed: {ex.Message}");
        }
    }
}
