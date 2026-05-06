using System;

namespace OpenClawPTT;

public sealed class HotkeyCombinationHandler : IWizardStepHandler
{
    public bool IsSecret => false;
    public bool IsOptionalSkip => false;
    public bool IsClearable => false;

    public string GetDescription() => "PTT hotkey (e.g. Alt+= or Ctrl+Shift+Space)";
    public string GetValidationHint() => " Expected format like Alt+= or Ctrl+Shift+Space";

    public string GetDefaultValue(AppConfig config) => config.HotkeyCombination;

    public bool Validate(string input, out string? parsedValue)
    {
        parsedValue = input;
        try
        {
            HotkeyMapping.Parse(input);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void ApplyValue(string input, AppConfig config)
    {
        config.HotkeyCombination = input;
    }
}
