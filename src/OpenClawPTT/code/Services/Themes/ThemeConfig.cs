using System.Collections.Generic;

namespace OpenClawPTT.Services.Themes;

/// <summary>
/// Represents a complete theme configuration with all style domains.
/// Properties are mutable so a single instance can be updated at runtime
/// via <see cref="ThemeProvider.Current"/>.
/// </summary>
public sealed class ThemeConfig
{
    /// <summary>Theme display name.</summary>
    public string Name { get; set; } = "Default";

    /// <summary>Author or source label.</summary>
    public string Author { get; set; } = "OpenClaw PTT";

    /// <summary>Markdown rendering styles (headings, code fences, blockquotes, etc.).</summary>
    public MarkdownTheme Markdown { get; set; } = new();

    /// <summary>Table rendering styles.</summary>
    public TableTheme Table { get; set; } = new();

    /// <summary>Tool rendering styles (headers, ConsoleColor overrides).</summary>
    public ToolTheme Tools { get; set; } = new();

    /// <summary>Returns the default hardcoded theme configuration.</summary>
    public static ThemeConfig Default => new();
}

/// <summary>
/// Styles for Markdown block-level and inline rendering.
/// Each string is a Spectre.Console markup style tag (without the brackets),
/// e.g. <c>"bold gray89 on darkblue"</c> or <c>"dim"</c>.
/// </summary>
public sealed class MarkdownTheme
{
    // ── Code fences ────────────────────────────────────────────────────────

    /// <summary>
    /// Full Spectre markup for the opening code fence border.
    /// Includes decorative characters and any label.
    /// </summary>
    public string CodeFenceStartMarkup { get; set; } = "[dim]─────────────────[italic]code[/]─────────────────[/]";

    /// <summary>
    /// Full Spectre markup for the closing code fence border.
    /// Includes decorative characters.
    /// </summary>
    public string CodeFenceEndMarkup { get; set; } = "[dim]──────────────────────────────────────[/]";

    /// <summary>Style for each line of fenced code content (e.g. "default on gray15").</summary>
    public string CodeContentStyle { get; set; } = "default on gray15";

    // ── Inline code ────────────────────────────────────────────────────────

    /// <summary>Style for inline `code` spans (e.g. "bold gray89 on darkblue").</summary>
    public string InlineCodeStyle { get; set; } = "bold gray89 on darkblue";

    // ── Headings ───────────────────────────────────────────────────────────

    /// <summary>Style for H1 headings (e.g. "bold underline default on gray27").</summary>
    public string HeadingH1Style { get; set; } = "bold underline default on gray27";

    /// <summary>Style for H2 headings (e.g. "bold underline").</summary>
    public string HeadingH2Style { get; set; } = "bold underline";

    /// <summary>Style for H3–H6 headings (e.g. "bold dim").</summary>
    public string HeadingH3PlusStyle { get; set; } = "bold dim";

    // ── Blockquotes ────────────────────────────────────────────────────────

    /// <summary>Style for blockquote lines (e.g. "italic dim").</summary>
    public string BlockquoteStyle { get; set; } = "italic dim";

    // ── Thematic break ─────────────────────────────────────────────────────

    /// <summary>
    /// Full Spectre markup for thematic break / horizontal rules.
    /// Includes decorative characters.
    /// </summary>
    public string ThematicBreakMarkup { get; set; } = "[dim]────────────────────────────────────────[/]";

    // ── Inline formatting (shared by all levels) ───────────────────────────

    /// <summary>Style for bold + italic combined text (e.g. "bold italic").</summary>
    public string BoldItalicStyle { get; set; } = "bold italic";

    /// <summary>Style for bold text (e.g. "bold").</summary>
    public string BoldStyle { get; set; } = "bold";

    /// <summary>Style for italic text (e.g. "italic").</summary>
    public string ItalicStyle { get; set; } = "italic";

    /// <summary>Style for strikethrough text (e.g. "strikethrough").</summary>
    public string StrikethroughStyle { get; set; } = "strikethrough";
}

/// <summary>
/// Styles for table rendering.
/// </summary>
public sealed class TableTheme
{
    /// <summary>Spectre color name for table edges and borders (e.g. "deepskyblue3").</summary>
    public string EdgeColor { get; set; } = "deepskyblue3";
}

/// <summary>
/// Styles for tool rendering (tool call display in the console).
/// </summary>
public sealed class ToolTheme
{
    /// <summary>Style for the tool header bar (e.g. "gray93 on #333333").</summary>
    public string HeaderStyle { get; set; } = "gray93 on #333333";

    /// <summary>
    /// Override mappings from <see cref="System.ConsoleColor"/> names to
    /// Spectre.Console color names. Only entries present in this dictionary
    /// replace the default mapping in <c>ConsoleColorMapper</c>.
    /// Key: ConsoleColor enum name (e.g. "Gray", "White", "Cyan").
    /// Value: Spectre color name (e.g. "grey", "white", "cyan").
    /// </summary>
    public Dictionary<string, string> ConsoleColorOverrides { get; set; } = new();
}
