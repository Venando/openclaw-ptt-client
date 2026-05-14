using System.Text.Json.Serialization;

namespace OpenClawPTT.Services.Themes;

/// <summary>
/// Represents an immutable theme configuration.
/// Default values are applied when no theme file is specified or loading fails.
/// </summary>
public sealed class ThemeConfig
{
    /// <summary>Theme display name.</summary>
    public string Name { get; init; } = "Default";

    /// <summary>Author or source label.</summary>
    public string Author { get; init; } = "OpenClaw PTT";

    /// <summary>Primary accent color for highlights and headers (Spectre.Console color name or hex).</summary>
    public string AccentColor { get; init; } = "cyan2";

    /// <summary>Secondary accent color for less prominent highlights.</summary>
    public string SecondaryColor { get; init; } = "springgreen4";

    /// <summary>Foreground color for normal text content.</summary>
    public string ForegroundColor { get; init; } = "white";

    /// <summary>Text color used for success / OK messages.</summary>
    public string SuccessColor { get; init; } = "green";

    /// <summary>Text color used for warnings.</summary>
    public string WarningColor { get; init; } = "yellow";

    /// <summary>Text color used for errors.</summary>
    public string ErrorColor { get; init; } = "red";

    /// <summary>Info message color.</summary>
    public string InfoColor { get; init; } = "grey";

    /// <summary>Border color for panels, separators, and tables.</summary>
    public string BorderColor { get; init; } = "blue";

    /// <summary>Background color for selected/highlighted items.</summary>
    public string SelectionBackground { get; init; } = "darkblue";

    /// <summary>Muted / dim text color for secondary or less important info.</summary>
    public string MutedColor { get; init; } = "grey69";

    /// <summary>
    /// Returns the default hardcoded theme configuration.
    /// Used when no theme file is specified or a file fails to load.
    /// </summary>
    public static ThemeConfig Default => new();
}
