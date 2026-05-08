using OpenClawPTT.Services.Diagnostics;

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
        catch (GatewayException gex)
        {
            ShowGatewayError(gex);
        }
        catch (Exception ex)
        {
            _console.PrintError($"Send failed: {ex.Message}");
        }
    }

    private void ShowGatewayError(GatewayException gex)
    {
        var message = gex.Message;

        // Categorize the error for better display
        if (IsQuotaError(message))
        {
            _console.PrintModelFailed(message);
        }
        else if (IsRateLimitError(message))
        {
            _console.PrintWarning($"Rate limited: {message}");
        }
        else if (gex.DetailCode != null)
        {
            _console.PrintGatewayError(message, gex.DetailCode, gex.RecommendedStep);
        }
        else
        {
            _console.PrintError($"Gateway error: {message}");
        }
    }

    private static bool IsQuotaError(string message)
        => GatewayErrorClassifier.IsQuotaError(message);

    private static bool IsRateLimitError(string message)
        => GatewayErrorClassifier.IsRateLimitError(message);
}
