namespace OpenClawPTT.Services.Themes;

/// <summary>
/// Represents a complete theme configuration with all style domains.
/// Properties are mutable so a single instance can be updated at runtime
/// via <see cref="ThemeProvider.Current"/>.
/// </summary>
public sealed class ThemeConfig
{
    public string Name { get; set; } = "Default";
    public string Author { get; set; } = "OpenClaw PTT";
    public MarkdownTheme Markdown { get; set; } = new();
    public TableTheme Table { get; set; } = new();
    public ToolTheme Tools { get; set; } = new();
    public static ThemeConfig Default => new();
}

/// <summary>
/// Styles for Markdown block-level and inline rendering.
/// Each string is a Spectre.Console markup style tag (without the brackets),
/// e.g. <c>"bold gray89 on darkblue"</c> or <c>"dim"</c>.
/// </summary>
public sealed class MarkdownTheme
{
    public string CodeFenceStartMarkup { get; set; } = "[dim]\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500[italic]code[/]\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500[/]";
    public string CodeFenceEndMarkup { get; set; } = "[dim]\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500[/]";
    public string CodeContentStyle { get; set; } = "default on gray15";
    public string InlineCodeStyle { get; set; } = "bold gray89 on darkblue";
    public string HeadingH1Style { get; set; } = "bold underline default on gray27";
    public string HeadingH2Style { get; set; } = "bold underline";
    public string HeadingH3PlusStyle { get; set; } = "bold dim";
    public string BlockquoteStyle { get; set; } = "italic dim";
    public string ThematicBreakMarkup { get; set; } = "[dim]\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500[/]";
    public string BoldItalicStyle { get; set; } = "bold italic";
    public string BoldStyle { get; set; } = "bold";
    public string ItalicStyle { get; set; } = "italic";
    public string StrikethroughStyle { get; set; } = "strikethrough";
}

public sealed class TableTheme
{
    public string EdgeColor { get; set; } = "deepskyblue3";
}

/// <summary>
/// Styles for tool rendering (tool call display in the console).
/// Each property is a distinct Spectre.Console style string
/// (e.g. "grey", "bold cyan", "default on gray15").
/// </summary>
public sealed class ToolTheme
{
    public string HeaderStyle { get; set; } = "gray93 on #333333";

    // General
    public string Label { get; set; } = "grey";
    public string Muted { get; set; } = "grey";
    public string Value { get; set; } = "white";
    public string Separator { get; set; } = "grey";
    public string MutedSeparator { get; set; } = "black";
    public string TruncatedMore { get; set; } = "grey";

    // KVP rendering
    public string KvpSeparator { get; set; } = "grey";
    public string KvpKey { get; set; } = "grey";
    public string KvpValue { get; set; } = "white";
    public string KvpLabel { get; set; } = "grey";

    // Exec command type colors
    public string ExecFileSystem { get; set; } = "green";
    public string ExecFileContent { get; set; } = "blue";
    public string ExecBuild { get; set; } = "magenta";
    public string ExecPackageManager { get; set; } = "red";
    public string ExecNetwork { get; set; } = "cyan";
    public string ExecScripting { get; set; } = "yellow";
    public string ExecProcess { get; set; } = "olive";
    public string ExecHereDoc { get; set; } = "darkcyan";
    public string ExecVcs { get; set; } = "olive";

    // Exec structural
    public string ExecPositional { get; set; } = "cyan";
    public string ExecLongFlag { get; set; } = "green";
    public string ExecShortFlag { get; set; } = "olive";
    public string ExecEnvKey { get; set; } = "cyan";
    public string ExecEnvValue { get; set; } = "yellow";
    public string ExecScriptBody { get; set; } = "grey";
    public string ExecHereDocSummary { get; set; } = "grey";
    public string ExecPathIcon { get; set; } = "grey";
    public string ExecPathText { get; set; } = "grey";

    // Edit / Diff
    public string DiffAdded { get; set; } = "default on springgreen4";
    public string DiffRemoved { get; set; } = "default on darkred";
    public string DiffPrefix { get; set; } = "grey";

    // Read
    public string ReadLineInfo { get; set; } = "grey";

    // WebFetch
    public string FetchUrl { get; set; } = "grey";
    public string FetchMaxInfo { get; set; } = "grey";
}
