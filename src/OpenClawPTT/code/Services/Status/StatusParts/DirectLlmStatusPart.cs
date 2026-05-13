using System;

namespace OpenClawPTT.Services.StatusParts;

/// <summary>
/// Renders the direct LLM status with last-called timestamp, e.g.
/// "LLM:● OK 5s ago" or "LLM:● —".
/// Extends <see cref="ServiceStatusPart"/> to reuse the color/animation
/// infrastructure while providing a richer status label format.
/// Caches the rendered value so it only rebuilds on actual status changes.
/// </summary>
public sealed class DirectLlmStatusPart : ServiceStatusPart
{
    private const string StatusPrefix = "]● ";
    private const string RightSuffix = "[/]";

    private string _statusLabel = "\u2014"; // em dash — "not configured"
    private DateTime? _lastCalled;

    public DirectLlmStatusPart(DisplayPosition defaultPosition = DisplayPosition.TopSeparatorRight, int order = 5)
        : base("LLM:", defaultPosition, order)
    {
    }

    /// <summary>Updates the LLM status label and color. Marks dirty on actual change.</summary>
    public override void SetStatus(string label, StatusColor color)
    {
        if (!string.Equals(_statusLabel, label, StringComparison.Ordinal) || Color != color)
        {
            _statusLabel = label;
            SetStatus(color);
        }
    }

    /// <summary>Updates the last-called timestamp. Always marks dirty.</summary>
    public void SetLastCalled(DateTime? timestamp)
    {
        if (_lastCalled != timestamp)
        {
            _lastCalled = timestamp;
            MarkDirty();
        }
    }

    protected override void BuildText()
    {
        // "LLM:" prefix comes from base via Label
        Builder.Append(' ');
        Builder.Append(Label);
        Builder.Append('[');
        Builder.Append(Color.ToMarkupColor());
        Builder.Append(StatusPrefix);
        Builder.Append(_statusLabel);

        // Show last-called timestamp if available
        if (_lastCalled.HasValue)
        {
            Builder.Append(' ');
            Builder.Append(FormatElapsed(DateTime.Now - _lastCalled.Value));
        }

        Builder.Append(RightSuffix);
    }

    /// <summary>
    /// Formats an elapsed <see cref="TimeSpan"/> as a compact human-readable string
    /// (e.g. "5s", "2m", "1h", "3d").
    /// </summary>
    private static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed.TotalSeconds < 60)
            return $"{(int)elapsed.TotalSeconds}s";
        if (elapsed.TotalMinutes < 60)
            return $"{(int)elapsed.TotalMinutes}m";
        if (elapsed.TotalHours < 24)
            return $"{(int)elapsed.TotalHours}h";
        return $"{(int)elapsed.TotalDays}d";
    }
}
