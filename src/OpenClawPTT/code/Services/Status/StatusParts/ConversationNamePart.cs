namespace OpenClawPTT.Services.StatusParts;

/// <summary>
/// Renders the conversation / session name, e.g. "│ WireResetCheck │".
/// Caches the rendered value so it only rebuilds on actual name changes.
/// </summary>
public sealed class ConversationNamePart : StringStatusPartBase
{
    private const string RepeatedCharacterMarkup = "white";
    private const string ConversationNameMarkup = $"italic {RepeatedCharacterMarkup}";
    private const string ConvOpen = "[grey]\u2502[/] [" + ConversationNameMarkup + "]";
    private const string ConvClose = "[/] [grey]\u2502[/]";

    public ConversationNamePart(DisplayPosition defaultPosition = DisplayPosition.TopSeparatorLeft, int order = 40)
        : base(defaultPosition, order, " ")
    {
    }

    protected override void BuildText()
    {
        var name = Value;
        if (string.IsNullOrWhiteSpace(name))
            return;

        Builder.Append(ConvOpen);
        Builder.Append(name);
        Builder.Append(ConvClose);
    }
}
