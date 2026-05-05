using System;
using System.Linq;

namespace OpenClawPTT;

public sealed class VisualFeedbackPositionHandler : IWizardStepHandler
{
    private static readonly string[] ValidPositions = { "TopLeft", "TopRight", "BottomLeft", "BottomRight" };

    public bool IsSecret => false;
    public bool IsOptionalSkip => false;
    public bool IsClearable => false;

    public string GetDescription() => "Feedback position (TopLeft / TopRight / BottomLeft / BottomRight)";
    public string GetValidationHint() => " Choose: TopLeft, TopRight, BottomLeft, or BottomRight";

    public string GetDefaultValue(AppConfig config) => config.VisualFeedbackPosition;

    public bool Validate(string input, out string? parsedValue)
    {
        var match = ValidPositions.FirstOrDefault(
            p => p.Equals(input, StringComparison.OrdinalIgnoreCase));
        parsedValue = match;
        return match != null;
    }

    public void ApplyValue(string input, AppConfig config)
    {
        config.VisualFeedbackPosition = ValidPositions.First(
            p => p.Equals(input, StringComparison.OrdinalIgnoreCase));
    }
}
