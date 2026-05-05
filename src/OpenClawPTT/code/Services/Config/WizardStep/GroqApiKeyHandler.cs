namespace OpenClawPTT;

public sealed class GroqApiKeyHandler : IWizardStepHandler
{
    public bool IsSecret => true;
    public bool IsOptionalSkip => false;
    public bool IsClearable => false;

    public string GetDescription() => "Groq API key (starts with gsk_)";
    public string GetValidationHint() => " Must start with gsk_";

    public string GetDefaultValue(AppConfig config) => config.GroqApiKey;

    public bool Validate(string input, out string? parsedValue)
    {
        parsedValue = input;
        return input.StartsWith("gsk_");
    }

    public void ApplyValue(string input, AppConfig config)
    {
        config.GroqApiKey = input;
    }
}
