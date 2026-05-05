using System;
using System.Linq;

namespace OpenClawPTT;

public sealed class AudioResponseModeHandler : IWizardStepHandler
{
    private static readonly string[] ValidModes = { "text-only", "audio-only", "both" };

    public bool IsSecret => false;
    public bool IsOptionalSkip => false;
    public bool IsClearable => false;

    public string GetDescription() => "Audio response mode (text-only / audio-only / both)";
    public string GetValidationHint() => " Choose: text-only, audio-only, or both";

    public string GetDefaultValue(AppConfig config) => config.AudioResponseMode ?? "text-only";

    public bool Validate(string input, out string? parsedValue)
    {
        var match = ValidModes.FirstOrDefault(
            m => string.Equals(m, input, StringComparison.OrdinalIgnoreCase));
        parsedValue = match;
        return match != null;
    }

    public void ApplyValue(string input, AppConfig config)
    {
        config.AudioResponseMode = ValidModes.First(
            m => string.Equals(m, input, StringComparison.OrdinalIgnoreCase));
    }
}
