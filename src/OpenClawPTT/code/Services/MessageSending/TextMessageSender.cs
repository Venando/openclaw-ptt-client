using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenClawPTT.Services;

/// <summary>Unified text message sender — uses MessageComposer then GatewayService.</summary>
public sealed class TextMessageSender : ITextMessageSender
{
    private readonly IGatewayService _gateway;
    private readonly IConfigurationService _configService;
    private readonly IConsoleOutput _console;
    private readonly IMessageComposer _composer;

    public TextMessageSender(IGatewayService gateway, IConfigurationService configService, IConsoleOutput console, IMessageComposer composer)
    {
        _gateway = gateway;
        _configService = configService;
        _console = console;
        _composer = composer;
    }

    public async Task SendAsync(string text, CancellationToken ct)
    {
        var cfg = _configService.Load()
            ?? throw new InvalidOperationException("Configuration not loaded");

        var composed = _composer.ComposeOutgoing(text, cfg);
        _console.PrintUserMessage(composed);
        _console.PrintInlineInfo("Sending… ");
        try
        {
            await _gateway.SendTextAsync(composed, ct);
            _console.PrintInlineSuccess("sent.");
        }
        catch (Exception ex)
        {
            _console.PrintError($"failed: {ex.Message}");
        }
    }
}
