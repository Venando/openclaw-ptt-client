using System.Text.RegularExpressions;

namespace OpenClawPTT;

public sealed class VisualFeedbackColorHandler : IWizardStepHandler
{
    private static readonly Regex HexColorPattern = new(@"^#?([0-9A-Fa-f]{6})$", RegexOptions.Compiled);

    public bool IsSecret => false;
    public bool IsOptionalSkip => false;
    public bool IsClearable => false;

    public string GetDescription() => "Feedback color (hex e.g. #00FF00)";
    public string GetValidationHint() => " Expected hex color like #00FF00";

    public string GetDefaultValue(AppConfig config) => config.VisualFeedbackColor;

    public bool Validate(string input, out string? parsedValue)
    {
        parsedValue = input;
        return HexColorPattern.IsMatch(input);
    }

    public void ApplyValue(string input, AppConfig config)
    {
        config.VisualFeedbackColor = input.StartsWith("#")
            ? input
            : $"#{input}";
    }
}
