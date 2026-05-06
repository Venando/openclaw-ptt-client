using System;

namespace OpenClawPTT;

public sealed class GatewayUrlHandler : IWizardStepHandler
{
    public bool IsSecret => false;
    public bool IsOptionalSkip => false;
    public bool IsClearable => false;

    public string GetDescription() => "Gateway URL";
    public string GetValidationHint() => " Expected ws:// or wss:// URL (e.g. ws://localhost:18789)";

    public string GetDefaultValue(AppConfig config) => config.GatewayUrl;

    public bool Validate(string input, out string? parsedValue)
    {
        parsedValue = input;
        return Uri.TryCreate(input, UriKind.Absolute, out var uri)
               && (uri.Scheme == "ws" || uri.Scheme == "wss");
    }

    public void ApplyValue(string input, AppConfig config)
    {
        config.GatewayUrl = input;
    }
}
