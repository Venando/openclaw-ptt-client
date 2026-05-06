namespace OpenClawPTT;

public interface IWizardStepHandler
{
    string GetDescription();
    string GetValidationHint();
    string GetDefaultValue(AppConfig config);
    bool Validate(string input, out string? parsedValue);
    void ApplyValue(string input, AppConfig config);
    bool IsSecret { get; }
    bool IsOptionalSkip { get; }
    bool IsClearable { get; }
}
