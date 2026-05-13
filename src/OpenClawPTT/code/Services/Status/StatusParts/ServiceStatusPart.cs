using System;

namespace OpenClawPTT.Services.StatusParts;

/// <summary>
/// Renders a single service status as a label prefix with a colored dot, e.g.
/// "GW:●" (green), "TTS:·" (yellow animating), "STT:●" (red).
/// No status text label — just the service prefix + colored dot.
/// When yellow, the dot animates by cycling through [·, •, ●, •] on each render.
///
/// Not sealed — subclasses (e.g. <see cref="DirectLlmStatusPart"/>) may
/// override <see cref="StatusPartBase.BuildText"/> to customize rendering
/// while inheriting the color/animation infrastructure.
/// </summary>
public class ServiceStatusPart : StatusPartBase
{
    // Animation frames for yellow/transitional state: thin → medium → full → medium
    private static readonly char[] YellowFrames = ['•', '●', '•', '●'];

    private readonly string _label;
    private StatusColor _color = StatusColor.Yellow;
    private int _frameIndex;

    /// <summary>
    /// Creates a service status part with a label prefix (e.g. "GW:", "TTS:", "STT:").
    /// </summary>
    public ServiceStatusPart(string label, DisplayPosition defaultPosition = DisplayPosition.TopSeparatorRight, int order = 0)
        : base(defaultPosition, order)
    {
        _label = label ?? throw new ArgumentNullException(nameof(label));
    }

    /// <inheritdoc />
    public override string SeparatorBefore => " ";

    /// <summary>The current status color.</summary>
    protected StatusColor Color => _color;

    /// <summary>The label prefix (e.g. "GW:", "TTS:").</summary>
    protected string Label => _label;

    /// <summary>The current animation frame index for yellow state.</summary>
    protected int FrameIndex => _frameIndex;

    /// <summary>Whether this part is currently in the yellow (transitional) state.</summary>
    internal bool IsYellow => _color == StatusColor.Yellow;

    /// <summary>Gets the current animation frame character for a yellow dot.</summary>
    internal char CurrentYellowChar
    {
        get
        {
            if (_color == StatusColor.Yellow)
                return YellowFrames[_frameIndex];
            return '\u25CF'; // ●
        }
    }

    /// <summary>Updates the status color. Marks dirty on actual change.</summary>
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
    /// Sets the status with a text label.
    /// Base implementation ignores the label and uses only the color.
    /// Subclasses (e.g. <see cref="DirectLlmStatusPart"/>) override to track the label.
    /// </summary>
    public virtual void SetStatus(string label, StatusColor color)
    {
        SetStatus(color);
    }

    /// <summary>
    /// Returns the yellow frame character at the given index.
    /// </summary>
    protected static char GetYellowFrame(int index) => YellowFrames[index % YellowFrames.Length];

    /// <summary>
    /// Returns the solid dot character.
    /// </summary>
    protected static char SolidDot => '\u25CF';

    protected override void BuildText()
    {
        // Leading space before label (e.g. " GW:●") — ensures gap after separator fill
        Builder.Append(' ');
        // Label prefix (e.g. "GW:", "TTS:", "STT:")
        Builder.Append(_label);

        // Colored dot
        string dot = _color == StatusColor.Yellow
            ? YellowFrames[_frameIndex].ToString()
            : "\u25CF"; // ●

        Builder.Append('[');
        Builder.Append(_color.ToMarkupColor());
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

}
