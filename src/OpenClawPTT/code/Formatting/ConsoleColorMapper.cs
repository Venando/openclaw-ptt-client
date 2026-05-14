using OpenClawPTT.Services.Themes;

namespace OpenClawPTT.Formatting;

/// <summary>
/// Maps System.ConsoleColor values to Spectre.Console color names.
/// Allows overriding the default mapping via <see cref="ThemeProvider.Current.Tools.ConsoleColorOverrides"/>.
/// </summary>
public static class ConsoleColorMapper
{
    /// <summary>
    /// Converts a System.ConsoleColor to its corresponding Spectre.Console color name.
    /// Checks <see cref="ThemeProvider.Current"/> for any ConsoleColor overrides first,
    /// then falls back to the built-in mapping.
    /// </summary>
    /// <param name="consoleColor">The ConsoleColor to convert.</param>
    /// <returns>The Spectre.Console color name (e.g., "red", "darkgreen", "grey").</returns>
    public static string ToSpectreColor(ConsoleColor consoleColor)
    {
        // Check theme overrides first
        var overrides = ThemeProvider.Current.Tools.ConsoleColorOverrides;
        if (overrides.Count > 0)
        {
            var key = consoleColor.ToString();
            if (overrides.TryGetValue(key, out var overriddenColor))
                return overriddenColor;
        }

        return consoleColor switch
        {
            ConsoleColor.Gray => "grey",
            ConsoleColor.DarkGray => "grey",
            ConsoleColor.Black => "black",
            ConsoleColor.Red => "red",
            ConsoleColor.DarkRed => "darkred",
            ConsoleColor.Green => "green",
            ConsoleColor.DarkGreen => "darkgreen",
            ConsoleColor.Yellow => "yellow",
            ConsoleColor.DarkYellow => "olive",
            ConsoleColor.Blue => "blue",
            ConsoleColor.DarkBlue => "darkblue",
            ConsoleColor.Magenta => "magenta",
            ConsoleColor.DarkMagenta => "darkmagenta",
            ConsoleColor.Cyan => "cyan",
            ConsoleColor.DarkCyan => "darkcyan",
            ConsoleColor.White => "white",
            _ => "default"
        };
    }
}
