namespace OpenClawPTT.Services;

/// <summary>Unified text message sender that sends text directly to the gateway.</summary>
public sealed class TextMessageSender : ITextMessageSender
{
    private readonly IGatewayService _gateway;

    public TextMessageSender(IGatewayService gateway)
    {
        _gateway = gateway;
    }

    public async Task SendAsync(string text, CancellationToken ct, bool printMessage)
    {
        try
        {
            if (printMessage)
            {
                ConsoleUi.PrintUserMessage(text);
            }
            await _gateway.SendTextAsync(text, ct);
        }
        catch (Exception ex)
        {
            ConsoleUi.PrintError($"failed: {ex.Message}");
        }
    }
}
