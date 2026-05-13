using System.Text;

namespace OpenClawPTT.Services.StatusParts;

/// <summary>
/// Renders a single service status as a colored dot only (no text label).
/// Green = connected/ok, Yellow = transitional, Red = disconnected/error.
/// When yellow, the dot animates by cycling through [·, •, ●, •] on each render.
/// </summary>
public sealed class ServiceStatusPart : StatusPartBase
{
    // Animation frames for yellow/transitional state: thin → medium → full → medium
    private static readonly char[] YellowFrames = ['·', '•', '●', '•'];

    private StatusColor _color = StatusColor.Yellow;
    private int _frameIndex;

    public ServiceStatusPart(DisplayPosition defaultPosition = DisplayPosition.TopSeparatorRight, int order = 0)
        : base(defaultPosition, order)
    {
    }

    /// <inheritdoc />
    public override string SeparatorBefore => " ";

    /// <summary>
    /// Updates the status color. Marks dirty on actual change.
    /// The label parameter is accepted for backward compatibility but ignored.
    /// </summary>
    public void SetStatus(StatusColor color)
    {
        if (_color != color)
        {
            _color = color;
            UpdateAnimationState();
            MarkDirty();
        }
    }

    /// <summary>
    /// Sets the status with an unused label (backward-compatible overload).
    /// </summary>
    public void SetStatus(string label, StatusColor color)
    {
        SetStatus(color);
    }

    /// <summary>
    /// Gets the current animation frame for a yellow dot.
    /// Public for testing.
    /// </summary>
    internal char CurrentYellowChar
    {
        get
        {
            if (_color == StatusColor.Yellow)
                return YellowFrames[_frameIndex];
            return '\u25CF'; // ●
        }
    }

    protected override void BuildText()
    {
        string dot;
        if (_color == StatusColor.Yellow)
        {
            dot = YellowFrames[_frameIndex].ToString();
        }
        else
        {
            dot = "\u25CF"; // ●
        }

        Builder.Append('[');
        Builder.Append(ToMarkupColor(_color));
        Builder.Append(']');
        Builder.Append(dot);
        Builder.Append("[/]");
    }

    private void UpdateAnimationState()
    {
        // Force rebuild on every GetText() when yellow for animation
        AlwaysRebuild = _color == StatusColor.Yellow;
        // Reset frame on state change
        if (_color != StatusColor.Yellow)
            _frameIndex = 0;
    }

    /// <summary>Advances the animation frame. Called externally before render.</summary>
    public void AdvanceFrame()
    {
        if (_color == StatusColor.Yellow)
        {
            _frameIndex = (_frameIndex + 1) % YellowFrames.Length;
            MarkDirty();
        }
    }

    private static string ToMarkupColor(StatusColor color) => color switch
    {
        StatusColor.Green => "green",
        StatusColor.Yellow => "yellow",
        StatusColor.Red => "red",
        _ => "yellow",
    };
}
