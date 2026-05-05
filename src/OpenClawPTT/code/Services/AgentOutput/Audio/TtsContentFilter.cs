using System.Text.RegularExpressions;

namespace OpenClawPTT.Services;

/// <summary>
/// Filters and sanitizes content for text-to-speech output.
/// Strips markdown, removes code blocks, URLs, etc.
/// </summary>
public static class TtsContentFilter
{
    /// <summary>
    /// Sanitizes text for TTS by removing markdown and formatting.
    /// </summary>
    public static string SanitizeForTts(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Remove code blocks entirely (will be handled separately)
        text = Regex.Replace(text, @"```[\s\S]*?```", " [Code block] ");
        text = Regex.Replace(text, @"`[^`]+`", " [Code] ");

        // Remove markdown formatting
        text = Regex.Replace(text, @"\*\*([^*]+)\*\*", "$1");  // bold
        text = Regex.Replace(text, @"\*([^*]+)\*", "$1");      // italic
        text = Regex.Replace(text, @"_([^_]+)_", "$1");        // underline
        text = Regex.Replace(text, @"#+\s*", "");               // headers
        text = Regex.Replace(text, @"\[([^\]]+)\]\([^\)]+\)", "$1 [Link]");  // links
        text = Regex.Replace(text, @"!\[[^\]]*\]\([^\)]+\)", " [Image] ");   // images

        // Replace URLs
        text = Regex.Replace(text, @"https?://[^\s]+", " [Link] ");

        // Replace tables (simplified - look for | patterns)
        if (text.Contains('|'))
        {
            var lines = text.Split('\n');
            int tableLines = lines.Count(l => l.Trim().Contains('|'));
            if (tableLines >= 2)
            {
                text = Regex.Replace(text, @"(\|[^\n]+\|\n?)+", " [Table] ");
            }
        }

        // Clean up extra whitespace
        text = Regex.Replace(text, @"\s+", " ");
        text = text.Trim();

        return text;
    }

    /// <summary>
    /// Checks if text contains markdown or code that needs special handling.
    /// </summary>
    public static bool HasSpecialFormatting(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        return text.Contains("```") ||
               text.Contains("`") ||
               text.Contains("**") ||
               text.Contains("#") ||
               text.Contains("|") ||
               text.Contains("http");
    }

    /// <summary>
    /// Truncates text to max length with ellipsis.
    /// </summary>
    public static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;
        if (maxLength <= 4)
            return text[..maxLength];

        return text[..(maxLength - 4)] + " ...";
    }
}
