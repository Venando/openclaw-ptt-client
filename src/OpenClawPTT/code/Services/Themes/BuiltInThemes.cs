namespace OpenClawPTT.Services.Themes;

/// <summary>
/// Factory methods for in-code-only themes (no JSON file required).
/// Each theme is registered in <see cref="ThemeService"/> and appears
/// in <c>/theme</c> listings alongside file-based themes.
/// Properties not explicitly set inherit <see cref="ThemeConfig.Default"/> values.
/// </summary>
public static class BuiltInThemes
{
    // ── Drakula ───────────────────────────────────────────────────────

    /// <summary>Dracula-inspired dark theme with purple/magenta accents.</summary>
    public static ThemeConfig Drakula => new()
    {
        Name = "drakula",
        Author = "OpenClaw PTT",
        Markdown = new MarkdownTheme
        {
            CodeFenceStartMarkup = "[#6272a4]\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500[#ff79c6 italic]code[/]\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500[/]",
            CodeFenceEndMarkup = "[#6272a4]\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500[/]",
            CodeContentStyle = "#f8f8f2 on #282a36",
            InlineCodeStyle = "bold #f8f8f2 on #44475a",
            HeadingH1Style = "bold underline #ffb86c",
            HeadingH2Style = "bold underline #bd93f9",
            HeadingH3PlusStyle = "bold #ff79c6",
            BlockquoteStyle = "italic #6272a4",
            ThematicBreakMarkup = "[#6272a4]\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500[/]",
            BoldItalicStyle = "bold italic #ff79c6",
            BoldStyle = "bold #ff79c6",
            ItalicStyle = "italic #f8f8f2",
            StrikethroughStyle = "strikethrough #6272a4",
        },
        Table = new TableTheme { EdgeColor = "#bd93f9" },
        Tools = DrakTools,
    };

    private static readonly ToolTheme DrakTools = new()
    {
        HeaderStyle = "bold #f8f8f2 on #44475a",
        Label = "#f8f8f2", Muted = "#6272a4", Value = "#f8f8f2",
        Separator = "#6272a4", MutedSeparator = "#44475a", TruncatedMore = "#6272a4",
        KvpSeparator = "#6272a4", KvpKey = "#6272a4", KvpValue = "#f8f8f2", KvpLabel = "#6272a4",
        ExecFileSystem = "#50fa7b", ExecFileContent = "#8be9fd", ExecBuild = "#ff79c6",
        ExecPackageManager = "#ff5555", ExecNetwork = "#8be9fd", ExecScripting = "#f1fa8c",
        ExecProcess = "#ffb86c", ExecHereDoc = "#6272a4", ExecVcs = "#ffb86c",
        ExecPositional = "#8be9fd", ExecLongFlag = "#50fa7b", ExecShortFlag = "#ffb86c",
        ExecEnvKey = "#8be9fd", ExecEnvValue = "#f1fa8c", ExecScriptBody = "#6272a4",
        ExecHereDocSummary = "#6272a4", ExecPathIcon = "#6272a4", ExecPathText = "#6272a4",
        DiffAdded = "#f8f8f2 on #3b8055", DiffRemoved = "#f8f8f2 on #8b3a3a", DiffPrefix = "#6272a4",
        ReadLineInfo = "#6272a4", FetchUrl = "#f8f8f2", FetchMaxInfo = "#6272a4",
        SeparatorChar = "\u2500", SeparatorCharMarkup = "#6272a4",
        TopLeftSeparatorPrefix = "\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500 ",
        StatusVerticalPipe = "#6272a4", StatusSegmentPipe = "#6272a4 bold",
        StatusNoAgentsText = "#6272a4", ConversationNameStyle = "italic #bd93f9",
        UserMessagePrefix = "[#50fa7b] Me:[/] ",
        ThinkingHeaderStyle = "bold #f8f8f2 on #44475a", ThinkingTextStyle = "#f8f8f2", ThinkingMoreStyle = "#6272a4",
        BannerBorderStyle = "#bd93f9", HelpCommandStyle = "#6272a4",
        InfoStyle = "#6272a4", SuccessStyle = "#50fa7b", WarningStyle = "#ffb86c", ErrorStyle = "#ff5555",
        RecordingIndicatorStyle = "#ff5555", GatewayErrorStyle = "#ff5555",
        LogTagStyle = "#6272a4", LogOkStyle = "#50fa7b", LogErrorStyle = "#ff5555",
        FallbackWarningStyle = "#ffb86c", FallbackFromStyle = "#ff5555", FallbackToStyle = "#50fa7b",
        ModelFailedStyle = "#ff5555", AgentBadgeStyle = "#f8f8f2 on #44475a", IntroductionBorderStyle = "#bd93f9",
        PanelCapStyle = "#6272a4", PanelHintStyle = "#6272a4", PanelSelectedBg = "#44475a",
        PanelSelectedNameStyle = "bold #f8f8f2", PanelActionStyle = "#6272a4",
        PanelActionSelectedStyle = "#f8f8f2", PanelTimeStyle = "#6272a4",
        StreamCursorMarkup = "bold #282a36 on #f8f8f2", StreamSelectionMarkup = "bold #f8f8f2 on #44475a",
        StreamCommandSlashMarkup = "#ff79c6", StreamInputPrefixStyle = "bold #bd93f9",
    };

    // ── Dark Modern ──────────────────────────────────────────────────

    /// <summary>VS Code Dark Modern — clean blue-on-dark-gray palette.</summary>
    public static ThemeConfig DarkModern => new()
    {
        Name = "Dark Modern",
        Author = "OpenClaw PTT",
        Tools = DarkModernTools,
    };

    private static readonly ToolTheme DarkModernTools = new()
    {
        HeaderStyle = "bold #cccccc on #2d2d2d",
        Label = "#cccccc", Muted = "#808080", Value = "#cccccc",
        Separator = "#808080", MutedSeparator = "#2d2d2d",
        KvpSeparator = "#808080", KvpKey = "#808080", KvpValue = "#cccccc", KvpLabel = "#808080",
        ExecFileSystem = "#4ec9b0", ExecFileContent = "#569cd6", ExecBuild = "#c586c0",
        ExecPackageManager = "#d16969", ExecNetwork = "#569cd6", ExecScripting = "#dcdcaa",
        ExecProcess = "#d7ba7d", ExecHereDoc = "#808080", ExecVcs = "#d7ba7d",
        ExecPositional = "#569cd6", ExecLongFlag = "#4ec9b0", ExecShortFlag = "#d7ba7d",
        ExecEnvKey = "#569cd6", ExecEnvValue = "#dcdcaa",
        DiffAdded = "#cccccc on #1b4d1b", DiffRemoved = "#cccccc on #5a1d1d",
        SeparatorCharMarkup = "#808080", TopLeftSeparatorPrefix = "\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500 ",
        StatusVerticalPipe = "#808080", StatusSegmentPipe = "#808080 bold",
        StatusNoAgentsText = "#808080", ConversationNameStyle = "italic #569cd6",
        UserMessagePrefix = "[#4ec9b0] Me:[/] ",
        ThinkingHeaderStyle = "bold #cccccc on #2d2d2d", ThinkingTextStyle = "#cccccc", ThinkingMoreStyle = "#808080",
        BannerBorderStyle = "#569cd6", HelpCommandStyle = "#808080",
        InfoStyle = "#808080", SuccessStyle = "#4ec9b0", WarningStyle = "#dcdcaa", ErrorStyle = "#d16969",
        RecordingIndicatorStyle = "#d16969", GatewayErrorStyle = "#d16969",
        LogTagStyle = "#808080", LogOkStyle = "#4ec9b0", LogErrorStyle = "#d16969",
        FallbackWarningStyle = "#dcdcaa", FallbackFromStyle = "#d16969", FallbackToStyle = "#4ec9b0",
        ModelFailedStyle = "#d16969", AgentBadgeStyle = "#cccccc on #2d2d2d", IntroductionBorderStyle = "#569cd6",
        PanelCapStyle = "#808080", PanelHintStyle = "#808080", PanelSelectedBg = "#3c3c3c",
        PanelSelectedNameStyle = "bold #cccccc", PanelActionStyle = "#808080",
        PanelActionSelectedStyle = "#cccccc", PanelTimeStyle = "#808080",
        StreamCursorMarkup = "bold #1e1e1e on #cccccc", StreamSelectionMarkup = "bold #cccccc on #3c3c3c",
        StreamCommandSlashMarkup = "#c586c0", StreamInputPrefixStyle = "bold #569cd6",
    };

    // ── Dark+ ─────────────────────────────────────────────────────────

    /// <summary>VS Code Dark+ — vibrant version with punchier accents.</summary>
    public static ThemeConfig DarkPlus => new()
    {
        Name = "Dark+",
        Author = "OpenClaw PTT",
        Markdown = new MarkdownTheme
        {
            HeadingH1Style = "bold underline #569cd6",
            HeadingH2Style = "bold underline #4ec9b0",
            HeadingH3PlusStyle = "bold #c586c0",
            InlineCodeStyle = "bold #d4d4d4 on #3c3c3c",
        },
        Table = new TableTheme { EdgeColor = "#569cd6" },
        Tools = DarkPlusTools,
    };

    private static readonly ToolTheme DarkPlusTools = new()
    {
        HeaderStyle = "bold #d4d4d4 on #333333",
        Label = "#d4d4d4", Muted = "#808080", Value = "#d4d4d4",
        Separator = "#808080", MutedSeparator = "#333333",
        KvpSeparator = "#808080", KvpKey = "#808080", KvpValue = "#d4d4d4", KvpLabel = "#808080",
        ExecFileSystem = "#4ec9b0", ExecFileContent = "#569cd6", ExecBuild = "#c586c0",
        ExecPackageManager = "#d16969", ExecNetwork = "#569cd6", ExecScripting = "#dcdcaa",
        ExecProcess = "#d7ba7d", ExecHereDoc = "#808080", ExecVcs = "#d7ba7d",
        ExecPositional = "#569cd6", ExecLongFlag = "#4ec9b0", ExecShortFlag = "#d7ba7d",
        ExecEnvKey = "#569cd6", ExecEnvValue = "#dcdcaa",
        DiffAdded = "#d4d4d4 on #1b4d1b", DiffRemoved = "#d4d4d4 on #5a1d1d",
        SeparatorCharMarkup = "#808080",
        TopLeftSeparatorPrefix = "\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500 ",
        StatusVerticalPipe = "#808080", StatusSegmentPipe = "#808080 bold",
        StatusNoAgentsText = "#808080", ConversationNameStyle = "italic #569cd6",
        UserMessagePrefix = "[#4ec9b0] Me:[/] ",
        ThinkingHeaderStyle = "bold #d4d4d4 on #333333", ThinkingTextStyle = "#d4d4d4", ThinkingMoreStyle = "#808080",
        BannerBorderStyle = "#569cd6", HelpCommandStyle = "#808080",
        InfoStyle = "#808080", SuccessStyle = "#4ec9b0", WarningStyle = "#dcdcaa", ErrorStyle = "#d16969",
        RecordingIndicatorStyle = "#d16969", GatewayErrorStyle = "#d16969",
        LogTagStyle = "#808080", LogOkStyle = "#4ec9b0", LogErrorStyle = "#d16969",
        FallbackWarningStyle = "#dcdcaa", FallbackFromStyle = "#d16969", FallbackToStyle = "#4ec9b0",
        ModelFailedStyle = "#d16969", AgentBadgeStyle = "#d4d4d4 on #3c3c3c", IntroductionBorderStyle = "#569cd6",
        PanelCapStyle = "#808080", PanelHintStyle = "#808080", PanelSelectedBg = "#04395e",
        PanelSelectedNameStyle = "bold #d4d4d4", PanelActionStyle = "#808080",
        PanelActionSelectedStyle = "#d4d4d4", PanelTimeStyle = "#808080",
        StreamCursorMarkup = "bold #1e1e1e on #d4d4d4", StreamSelectionMarkup = "bold #d4d4d4 on #264f78",
        StreamCommandSlashMarkup = "#c586c0", StreamInputPrefixStyle = "bold #569cd6",
    };

    // ── Monokai ───────────────────────────────────────────────────────

    /// <summary>Classic Monokai — yellow/pink/orange on dark olive background.</summary>
    public static ThemeConfig Monokai => new()
    {
        Name = "Monokai",
        Author = "OpenClaw PTT",
        Markdown = new MarkdownTheme
        {
            CodeContentStyle = "#f8f8f2 on #272822",
            InlineCodeStyle = "bold #f8f8f2 on #49483e",
            HeadingH1Style = "bold underline #f92672",
            HeadingH2Style = "bold underline #a6e22e",
            HeadingH3PlusStyle = "bold #fd971f",
            BlockquoteStyle = "italic #75715e",
            BoldItalicStyle = "bold italic #f92672",
            BoldStyle = "bold #f92672",
        },
        Table = new TableTheme { EdgeColor = "#66d9ef" },
        Tools = MonokaiTools,
    };

    private static readonly ToolTheme MonokaiTools = new()
    {
        HeaderStyle = "bold #f8f8f2 on #49483e",
        Label = "#f8f8f2", Muted = "#75715e", Value = "#f8f8f2",
        Separator = "#75715e", MutedSeparator = "#49483e",
        KvpSeparator = "#75715e", KvpKey = "#75715e", KvpValue = "#f8f8f2", KvpLabel = "#75715e",
        ExecFileSystem = "#a6e22e", ExecFileContent = "#66d9ef", ExecBuild = "#f92672",
        ExecPackageManager = "#f92672", ExecNetwork = "#66d9ef", ExecScripting = "#e6db74",
        ExecProcess = "#fd971f", ExecHereDoc = "#75715e", ExecVcs = "#fd971f",
        ExecPositional = "#66d9ef", ExecLongFlag = "#a6e22e", ExecShortFlag = "#fd971f",
        ExecEnvKey = "#66d9ef", ExecEnvValue = "#e6db74",
        DiffAdded = "#f8f8f2 on #3b8055", DiffRemoved = "#f8f8f2 on #8b3a3a",
        SeparatorCharMarkup = "#75715e",
        TopLeftSeparatorPrefix = "\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500 ",
        StatusVerticalPipe = "#75715e", StatusSegmentPipe = "#75715e bold",
        StatusNoAgentsText = "#75715e", ConversationNameStyle = "italic #66d9ef",
        UserMessagePrefix = "[#a6e22e] Me:[/] ",
        ThinkingHeaderStyle = "bold #f8f8f2 on #49483e", ThinkingTextStyle = "#f8f8f2", ThinkingMoreStyle = "#75715e",
        BannerBorderStyle = "#fd971f", HelpCommandStyle = "#75715e",
        InfoStyle = "#75715e", SuccessStyle = "#a6e22e", WarningStyle = "#e6db74", ErrorStyle = "#f92672",
        RecordingIndicatorStyle = "#f92672", GatewayErrorStyle = "#f92672",
        LogTagStyle = "#75715e", LogOkStyle = "#a6e22e", LogErrorStyle = "#f92672",
        FallbackWarningStyle = "#e6db74", FallbackFromStyle = "#f92672", FallbackToStyle = "#a6e22e",
        ModelFailedStyle = "#f92672", AgentBadgeStyle = "#f8f8f2 on #49483e", IntroductionBorderStyle = "#fd971f",
        PanelCapStyle = "#75715e", PanelHintStyle = "#75715e", PanelSelectedBg = "#49483e",
        PanelSelectedNameStyle = "bold #f8f8f2", PanelActionStyle = "#75715e",
        PanelActionSelectedStyle = "#f8f8f2", PanelTimeStyle = "#75715e",
        StreamCursorMarkup = "bold #272822 on #f8f8f2", StreamSelectionMarkup = "bold #f8f8f2 on #49483e",
        StreamCommandSlashMarkup = "#f92672", StreamInputPrefixStyle = "bold #66d9ef",
    };

    // ── Monokai Dimmed ────────────────────────────────────────────────

    /// <summary>Monokai Dimmed — desaturated, muted version of Monokai.</summary>
    public static ThemeConfig MonokaiDimmed => new()
    {
        Name = "Monokai Dimmed",
        Author = "OpenClaw PTT",
        Markdown = new MarkdownTheme
        {
            CodeContentStyle = "#c0c0c0 on #1e1e1e",
            InlineCodeStyle = "bold #c0c0c0 on #3a3a3a",
            HeadingH1Style = "bold underline #c7444a",
            HeadingH2Style = "bold underline #60805a",
            HeadingH3PlusStyle = "bold #d0874a",
            BlockquoteStyle = "italic #676767",
            BoldItalicStyle = "bold italic #c7444a",
            BoldStyle = "bold #c7444a",
        },
        Table = new TableTheme { EdgeColor = "#50808a" },
        Tools = MonokaiDimmedTools,
    };

    private static readonly ToolTheme MonokaiDimmedTools = new()
    {
        HeaderStyle = "bold #c0c0c0 on #3a3a3a",
        Label = "#c0c0c0", Muted = "#676767", Value = "#c0c0c0",
        Separator = "#676767", MutedSeparator = "#3a3a3a",
        KvpSeparator = "#676767", KvpKey = "#676767", KvpValue = "#c0c0c0", KvpLabel = "#676767",
        ExecFileSystem = "#60805a", ExecFileContent = "#50808a", ExecBuild = "#c7444a",
        ExecPackageManager = "#c7444a", ExecNetwork = "#50808a", ExecScripting = "#b8a85a",
        ExecProcess = "#d0874a", ExecHereDoc = "#676767", ExecVcs = "#d0874a",
        ExecPositional = "#50808a", ExecLongFlag = "#60805a", ExecShortFlag = "#d0874a",
        ExecEnvKey = "#50808a", ExecEnvValue = "#b8a85a",
        DiffAdded = "#c0c0c0 on #2d4a2d", DiffRemoved = "#c0c0c0 on #5a3030",
        SeparatorCharMarkup = "#676767",
        TopLeftSeparatorPrefix = "\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500 ",
        StatusVerticalPipe = "#676767", StatusSegmentPipe = "#676767 bold",
        StatusNoAgentsText = "#676767", ConversationNameStyle = "italic #50808a",
        UserMessagePrefix = "[#60805a] Me:[/] ",
        ThinkingHeaderStyle = "bold #c0c0c0 on #3a3a3a", ThinkingTextStyle = "#c0c0c0", ThinkingMoreStyle = "#676767",
        BannerBorderStyle = "#d0874a", HelpCommandStyle = "#676767",
        InfoStyle = "#676767", SuccessStyle = "#60805a", WarningStyle = "#b8a85a", ErrorStyle = "#c7444a",
        RecordingIndicatorStyle = "#c7444a", GatewayErrorStyle = "#c7444a",
        LogTagStyle = "#676767", LogOkStyle = "#60805a", LogErrorStyle = "#c7444a",
        FallbackWarningStyle = "#b8a85a", FallbackFromStyle = "#c7444a", FallbackToStyle = "#60805a",
        ModelFailedStyle = "#c7444a", AgentBadgeStyle = "#c0c0c0 on #3a3a3a", IntroductionBorderStyle = "#d0874a",
        PanelCapStyle = "#676767", PanelHintStyle = "#676767", PanelSelectedBg = "#373737",
        PanelSelectedNameStyle = "bold #c0c0c0", PanelActionStyle = "#676767",
        PanelActionSelectedStyle = "#c0c0c0", PanelTimeStyle = "#676767",
        StreamCursorMarkup = "bold #1e1e1e on #c0c0c0", StreamSelectionMarkup = "bold #c0c0c0 on #3a3a3a",
        StreamCommandSlashMarkup = "#c7444a", StreamInputPrefixStyle = "bold #50808a",
    };

    // ── Solarized Dark ────────────────────────────────────────────────

    /// <summary>Solarized Dark — earthy base02/base03 palette with teal/blue accents.</summary>
    public static ThemeConfig SolarizedDark => new()
    {
        Name = "Solarized Dark",
        Author = "OpenClaw PTT",
        Markdown = new MarkdownTheme
        {
            CodeContentStyle = "#839496 on #002b36",
            InlineCodeStyle = "bold #839496 on #073642",
            HeadingH1Style = "bold underline #268bd2",
            HeadingH2Style = "bold underline #859900",
            HeadingH3PlusStyle = "bold #d33682",
            BlockquoteStyle = "italic #586e75",
            BoldItalicStyle = "bold italic #d33682",
            BoldStyle = "bold #d33682",
        },
        Table = new TableTheme { EdgeColor = "#2aa198" },
        Tools = SolarizedTools,
    };

    private static readonly ToolTheme SolarizedTools = new()
    {
        HeaderStyle = "bold #839496 on #073642",
        Label = "#839496", Muted = "#586e75", Value = "#839496",
        Separator = "#586e75", MutedSeparator = "#073642",
        KvpSeparator = "#586e75", KvpKey = "#586e75", KvpValue = "#839496", KvpLabel = "#586e75",
        ExecFileSystem = "#859900", ExecFileContent = "#268bd2", ExecBuild = "#d33682",
        ExecPackageManager = "#dc322f", ExecNetwork = "#268bd2", ExecScripting = "#b58900",
        ExecProcess = "#cb4b16", ExecHereDoc = "#586e75", ExecVcs = "#cb4b16",
        ExecPositional = "#268bd2", ExecLongFlag = "#859900", ExecShortFlag = "#cb4b16",
        ExecEnvKey = "#268bd2", ExecEnvValue = "#b58900",
        DiffAdded = "#839496 on #1b3b1b", DiffRemoved = "#839496 on #5a1d1d",
        SeparatorCharMarkup = "#586e75",
        TopLeftSeparatorPrefix = "\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500 ",
        StatusVerticalPipe = "#586e75", StatusSegmentPipe = "#586e75 bold",
        StatusNoAgentsText = "#586e75", ConversationNameStyle = "italic #2aa198",
        UserMessagePrefix = "[#859900] Me:[/] ",
        ThinkingHeaderStyle = "bold #839496 on #073642", ThinkingTextStyle = "#839496", ThinkingMoreStyle = "#586e75",
        BannerBorderStyle = "#268bd2", HelpCommandStyle = "#586e75",
        InfoStyle = "#586e75", SuccessStyle = "#859900", WarningStyle = "#b58900", ErrorStyle = "#dc322f",
        RecordingIndicatorStyle = "#dc322f", GatewayErrorStyle = "#dc322f",
        LogTagStyle = "#586e75", LogOkStyle = "#859900", LogErrorStyle = "#dc322f",
        FallbackWarningStyle = "#b58900", FallbackFromStyle = "#dc322f", FallbackToStyle = "#859900",
        ModelFailedStyle = "#dc322f", AgentBadgeStyle = "#839496 on #073642", IntroductionBorderStyle = "#268bd2",
        PanelCapStyle = "#586e75", PanelHintStyle = "#586e75", PanelSelectedBg = "#073642",
        PanelSelectedNameStyle = "bold #93a1a1", PanelActionStyle = "#586e75",
        PanelActionSelectedStyle = "#93a1a1", PanelTimeStyle = "#586e75",
        StreamCursorMarkup = "bold #002b36 on #93a1a1", StreamSelectionMarkup = "bold #93a1a1 on #073642",
        StreamCommandSlashMarkup = "#d33682", StreamInputPrefixStyle = "bold #268bd2",
    };

    // ── Abyss ─────────────────────────────────────────────────────────

    /// <summary>Abyss — deep blue-black background, light blue accents, limited palette.</summary>
    public static ThemeConfig Abyss => new()
    {
        Name = "Abyss",
        Author = "OpenClaw PTT",
        Markdown = new MarkdownTheme
        {
            CodeContentStyle = "#c0c0c0 on #000c18",
            InlineCodeStyle = "bold #c0c0c0 on #0a1a2a",
            HeadingH1Style = "bold underline #80a0ff",
            HeadingH2Style = "bold underline #c080a0",
            HeadingH3PlusStyle = "bold #eaeaea",
            BlockquoteStyle = "italic #5a6a7a",
            BoldItalicStyle = "bold italic #80a0ff",
            BoldStyle = "bold #80a0ff",
        },
        Table = new TableTheme { EdgeColor = "#80a0ff" },
        Tools = AbyssTools,
    };

    private static readonly ToolTheme AbyssTools = new()
    {
        HeaderStyle = "bold #c0c0c0 on #0a1a2a",
        Label = "#c0c0c0", Muted = "#5a6a7a", Value = "#c0c0c0",
        Separator = "#5a6a7a", MutedSeparator = "#0a1a2a",
        KvpSeparator = "#5a6a7a", KvpKey = "#5a6a7a", KvpValue = "#c0c0c0", KvpLabel = "#5a6a7a",
        ExecFileSystem = "#c080a0", ExecFileContent = "#80a0ff", ExecBuild = "#eaeaea",
        ExecPackageManager = "#c080a0", ExecNetwork = "#80a0ff", ExecScripting = "#eaeaea",
        ExecProcess = "#c080a0", ExecHereDoc = "#5a6a7a", ExecVcs = "#c080a0",
        ExecPositional = "#80a0ff", ExecLongFlag = "#c080a0", ExecShortFlag = "#c080a0",
        ExecEnvKey = "#80a0ff", ExecEnvValue = "#eaeaea",
        DiffAdded = "#c0c0c0 on #1b3b5a", DiffRemoved = "#c0c0c0 on #5a2a3a",
        SeparatorCharMarkup = "#5a6a7a",
        TopLeftSeparatorPrefix = "\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500 ",
        StatusVerticalPipe = "#5a6a7a", StatusSegmentPipe = "#5a6a7a bold",
        StatusNoAgentsText = "#5a6a7a", ConversationNameStyle = "italic #80a0ff",
        UserMessagePrefix = "[#c080a0] Me:[/] ",
        ThinkingHeaderStyle = "bold #c0c0c0 on #0a1a2a", ThinkingTextStyle = "#c0c0c0", ThinkingMoreStyle = "#5a6a7a",
        BannerBorderStyle = "#80a0ff", HelpCommandStyle = "#5a6a7a",
        InfoStyle = "#5a6a7a", SuccessStyle = "#c080a0", WarningStyle = "#eaeaea", ErrorStyle = "#c080a0",
        RecordingIndicatorStyle = "#c080a0", GatewayErrorStyle = "#c080a0",
        LogTagStyle = "#5a6a7a", LogOkStyle = "#c080a0", LogErrorStyle = "#c080a0",
        FallbackWarningStyle = "#eaeaea", FallbackFromStyle = "#c080a0", FallbackToStyle = "#c080a0",
        ModelFailedStyle = "#c080a0", AgentBadgeStyle = "#c0c0c0 on #0a1a2a", IntroductionBorderStyle = "#80a0ff",
        PanelCapStyle = "#5a6a7a", PanelHintStyle = "#5a6a7a", PanelSelectedBg = "#0a1a2a",
        PanelSelectedNameStyle = "bold #c0c0c0", PanelActionStyle = "#5a6a7a",
        PanelActionSelectedStyle = "#c0c0c0", PanelTimeStyle = "#5a6a7a",
        StreamCursorMarkup = "bold #000c18 on #c0c0c0", StreamSelectionMarkup = "bold #c0c0c0 on #0a1a2a",
        StreamCommandSlashMarkup = "#80a0ff", StreamInputPrefixStyle = "bold #80a0ff",
    };
}
