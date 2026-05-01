using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenClawPTT.Services;

/// <summary>Unified text message sender — uses MessageComposer then GatewayService.</summary>
public sealed class TextMessageSender : ITextMessageSender
{
    private readonly IGatewayService _gateway;
    private readonly IConfigurationService _configService;
    private readonly IMessageComposer _composer;

    public TextMessageSender(IGatewayService gateway, IConfigurationService configService, IMessageComposer composer)
    {
        _gateway = gateway;
        _configService = configService;
        _composer = composer;
    }

    public async Task SendAsync(string text, CancellationToken ct)
    {
        var cfg = _configService.Load()
            ?? throw new InvalidOperationException("Configuration not loaded");

        var composed = _composer.ComposeOutgoing(text, cfg);
        try
        {
            await _gateway.SendTextAsync(composed, ct);
            ConsoleUi.PrintUserMessage(composed);
        }
        catch (Exception ex)
        {
            ConsoleUi.PrintError($"failed: {ex.Message}");
        }
    }
}
