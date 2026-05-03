using System.Text;
using Spectre.Console;

namespace OpenClawPTT;

/// <summary>
/// Formats streaming agent replies with word wrap and right margin indent.
/// Maintains state across delta chunks within a single reply.
/// </summary>
public sealed class AgentReplyFormatter : IAgentReplyFormatter
{
    private readonly int _rightMarginIndent;
    private readonly StringBuilder _wordBuffer = new StringBuilder();
    private int _currentLineLength; // length of current line excluding prefix
    private int _consoleWidth;
    private readonly IFormattedOutput _output;

    private string _prefix;
    private string _newlinePrefixLenght;
    private bool _prefixAlreadyPrinted;

    // Tracks currently open Spectre markup tags (e.g. "grey", "bold") for
    // re-emission on word-wrap line breaks, so markup is never split across lines.
    private readonly Stack<string> _openMarkupTags = new Stack<string>();

    /// <summary>
    /// Convenience constructor using default right-margin indent (10).
    /// </summary>
    public AgentReplyFormatter(string prefix, bool prefixAlreadyPrinted, IFormattedOutput output)
        : this(prefix, rightMarginIndent: 10, prefixAlreadyPrinted, output)
    {
    }

    /// <summary>
    /// Constructor with explicit word-wrap parameters.
    /// </summary>
    public AgentReplyFormatter(string prefix, int rightMarginIndent, bool prefixAlreadyPrinted, IFormattedOutput output)
    {
        _rightMarginIndent = rightMarginIndent;
        _output = output;
        Init(prefix, prefixAlreadyPrinted, output);
    }

    private void Init(string prefix, bool prefixAlreadyPrinted, IFormattedOutput output)
    {
        _prefix = Markup.Remove(prefix ?? string.Empty);
        _newlinePrefixLenght = new string(' ', _prefix.Length);
        _prefixAlreadyPrinted = prefixAlreadyPrinted;
        _consoleWidth = output.WindowWidth > 0 ? output.WindowWidth : 80;
    }

    /// <summary>
    /// Calculate available width for text based on configured console width.
    /// </summary>
    private int GetAvailableWidth()
    {
        int consoleWidth = _consoleWidth;

        int effectiveRightMargin = Math.Max(_rightMarginIndent, (int)(consoleWidth * 0.1));
        int available;
        if (_prefixAlreadyPrinted)
        {
            available = consoleWidth - _newlinePrefixLenght.Length - effectiveRightMargin;
        }
        else
        {
            available = consoleWidth - _prefix.Length - effectiveRightMargin;
        }
        return available > 0 ? available : consoleWidth / 2;
    }

    public void Reconfigure(string prefix, bool prefixAlreadyPrinted = false)
    {
        Init(prefix, prefixAlreadyPrinted, _output);
        _openMarkupTags.Clear();
        _wordBuffer.Clear();
        _currentLineLength = 0;
    }

    /// <summary>
    /// Process a plain-text delta chunk and write formatted output with word-wrap.
    /// </summary>
    public void ProcessDelta(string delta)
    {
        int availableWidth = GetAvailableWidth();

        foreach (char c in delta)
        {
            if (c == '\n')
            {
                FlushWordBuffer(availableWidth);
                _output.WriteLine();
                _output.Write(_newlinePrefixLenght);
                _currentLineLength = 0;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                FlushWordBuffer(availableWidth);
                if (_currentLineLength + 1 <= availableWidth)
                {
                    _output.Write(c.ToString());
                    _currentLineLength++;
                }
                else
                {
                    WriteNewLine();
                    _output.Write(c.ToString());
                    _currentLineLength = 1;
                }
            }
            else
            {
                _wordBuffer.Append(c);
                if (_wordBuffer.Length > availableWidth)
                {
                    int charsThatFit = availableWidth - _currentLineLength;
                    if (charsThatFit > 0)
                    {
                        string part = _wordBuffer.ToString(0, charsThatFit);
                        _output.Write(part);
                        _currentLineLength += charsThatFit;
                        _wordBuffer.Remove(0, charsThatFit);
                    }
                    if (_wordBuffer.Length > 0)
                    {
                        WriteNewLine();
                        _currentLineLength = 0;
                    }
                }
            }
        }
    }

    // ── helper: validate a Spectre tag name ──────────────────────────
    /// <summary>
    /// Returns true if <paramref name="tagContent"/> looks like a valid
    /// Spectre.Console tag name (alphanumeric, hyphens, dots, underscores,
    /// color names, and spaces for compound tags like "bold underline").
    /// Tags containing quotes or other special characters are likely
    /// literal bracket content.
    /// </summary>
    private static bool IsValidTagName(string tagContent)
    {
        if (string.IsNullOrEmpty(tagContent))
            return false;
        foreach (char ch in tagContent)
        {
            if (!char.IsLetterOrDigit(ch) && ch != '-' && ch != '.' && ch != '_' && ch != '#' && ch != ' ' && ch != '=' && ch != ':' && ch != '/')
                return false;
        }
        return true;
    }

    /// <summary>
    /// Returns true if <paramref name="tagName"/> is a known Spectre.Console
    /// style/tag that is intentionally used in this application.
    /// Unknown tags like "text" or "foo" appearing in content are more
    /// likely literal brackets than intentional markup.
    /// </summary>
    private static readonly HashSet<string> _knownSpectreTags = new(System.StringComparer.OrdinalIgnoreCase)
    {
        // ── Decoration keywords ──────────────────────────────────────
        "bold", "dim", "italic", "underline", "strikethrough", "invert", "conceal",
        "blink", "slowblink", "rapidblink",
        // Style shortcuts
        "default", "none",

        // ── Standard 16 colors ───────────────────────────────────────
        "black", "red", "green", "yellow", "blue", "magenta", "cyan", "white",
        "grey", "gray",
        "darkblack", "darkred", "darkgreen", "darkyellow", "darkblue",
        "darkmagenta", "darkcyan", "darkwhite",
        "darkgrey", "darkgray",
        "lightblack", "lightred", "lightgreen", "lightyellow", "lightblue",
        "lightmagenta", "lightcyan", "lightwhite",
        "lightgrey", "lightgray",

        // ── Extended named colors (Spectre 256-color palette) ────────
        "aqua", "aquamarine", "aquamarine1", "aquamarine2", "aquamarine3",
        "azure", "azure1", "azure2", "azure3", "blue1", "blue2", "blue3",
        "bisque", "beige", "blanchedalmond", "blueviolet",
        "brown", "burlywood", "burlywood1", "burlywood2", "burlywood3",
        "burlywood4", "cadetblue", "cadetblue1", "cadetblue2", "cadetblue3",
        "cadetblue4", "chartreuse", "chartreuse1", "chartreuse2", "chartreuse3",
        "chartreuse4", "chocolate", "chocolate1", "chocolate2", "chocolate3",
        "chocolate4", "coral", "coral1", "coral2", "coral3", "coral4",
        "cornflowerblue", "cornsilk", "cornsilk1", "cornsilk2", "cornsilk3", "cornsilk4",
        "crimson", "cyan1", "cyan2", "cyan3",
        "darkblue", "darkcyan", "darkgoldenrod", "darkgoldenrod1", "darkgoldenrod2",
        "darkgoldenrod3", "darkgoldenrod4", "darkgreen", "darkkhaki", "darkmagenta",
        "darkolivegreen", "darkolivegreen1", "darkolivegreen2", "darkolivegreen3",
        "darkolivegreen4", "darkorange", "darkorange1", "darkorange2", "darkorange3",
        "darkorange4", "darkorchid", "darkorchid1", "darkorchid2", "darkorchid3",
        "darkorchid4", "darkred", "darksalmon", "darkseagreen", "darkseagreen1",
        "darkseagreen2", "darkseagreen3", "darkseagreen4", "darkslateblue",
        "darkslategray", "darkslategray1", "darkslategray2", "darkslategray3",
        "darkslategray4", "darkslategrey", "darkturquoise", "darkviolet",
        "deeppink", "deeppink1", "deeppink2", "deeppink3", "deeppink4",
        "deepskyblue", "deepskyblue1", "deepskyblue2", "deepskyblue3", "deepskyblue4",
        "dimgray", "dimgrey", "dodgerblue", "dodgerblue1", "dodgerblue2", "dodgerblue3",
        "dodgerblue4",
        "firebrick", "firebrick1", "firebrick2", "firebrick3", "firebrick4",
        "floralwhite", "forestgreen",
        "fuchsia", "gainsboro", "ghostwhite", "gold", "gold1", "gold2", "gold3",
        "gold4", "goldenrod", "goldenrod1", "goldenrod2", "goldenrod3", "goldenrod4",
        "gray0", "gray1", "gray2", "gray3", "gray4", "gray5", "gray6", "gray7", "gray8",
        "gray9", "gray10", "gray11", "gray12", "gray13", "gray14", "gray15", "gray16",
        "gray17", "gray18", "gray19", "gray20", "gray21", "gray22", "gray23", "gray24",
        "gray25", "gray26", "gray27", "gray28", "gray29", "gray30", "gray31", "gray32",
        "gray33", "gray34", "gray35", "gray36", "gray37", "gray38", "gray39", "gray40",
        "gray41", "gray42", "gray43", "gray44", "gray45", "gray46", "gray47", "gray48",
        "gray49", "gray50", "gray51", "gray52", "gray53", "gray54", "gray55", "gray56",
        "gray57", "gray58", "gray59", "gray60", "gray61", "gray62", "gray63", "gray64",
        "gray65", "gray66", "gray67", "gray68", "gray69", "gray70", "gray71", "gray72",
        "gray73", "gray74", "gray75", "gray76", "gray77", "gray78", "gray79", "gray80",
        "gray81", "gray82", "gray83", "gray84", "gray85", "gray86", "gray87", "gray88",
        "gray89", "gray90", "gray91", "gray92", "gray93", "gray94", "gray95", "gray96",
        "gray97", "gray98", "gray99", "gray100",
        "green1", "green2", "green3", "green4", "greenyellow",
        "grey0", "grey1", "grey2", "grey3", "grey4", "grey5", "grey6", "grey7", "grey8",
        "grey9", "grey10", "grey11", "grey12", "grey13", "grey14", "grey15", "grey16",
        "grey17", "grey18", "grey19", "grey20", "grey21", "grey22", "grey23", "grey24",
        "grey25", "grey26", "grey27", "grey28", "grey29", "grey30", "grey31", "grey32",
        "grey33", "grey34", "grey35", "grey36", "grey37", "grey38", "grey39", "grey40",
        "grey41", "grey42", "grey43", "grey44", "grey45", "grey46", "grey47", "grey48",
        "grey49", "grey50", "grey51", "grey52", "grey53", "grey54", "grey55", "grey56",
        "grey57", "grey58", "grey59", "grey60", "grey61", "grey62", "grey63", "grey64",
        "grey65", "grey66", "grey67", "grey68", "grey69", "grey70", "grey71", "grey72",
        "grey73", "grey74", "grey75", "grey76", "grey77", "grey78", "grey79", "grey80",
        "grey81", "grey82", "grey83", "grey84", "grey85", "grey86", "grey87", "grey88",
        "grey89", "grey90", "grey91", "grey92", "grey93", "grey94", "grey95", "grey96",
        "grey97", "grey98", "grey99", "grey100",
        "honeydew", "honeydew1", "honeydew2", "honeydew3", "honeydew4",
        "hotpink", "hotpink1", "hotpink2", "hotpink3", "hotpink4",
        "indianred", "indianred1", "indianred2", "indianred3", "indianred4",
        "indigo", "ivory", "ivory1", "ivory2", "ivory3", "ivory4",
        "khaki", "khaki1", "khaki2", "khaki3", "khaki4",
        "lavender", "lavenderblush", "lavenderblush1", "lavenderblush2",
        "lavenderblush3", "lavenderblush4", "lawngreen", "lemonchiffon",
        "lemonchiffon1", "lemonchiffon2", "lemonchiffon3", "lemonchiffon4",
        "lightblue", "lightblue1", "lightblue2", "lightblue3", "lightblue4",
        "lightcoral", "lightcyan", "lightcyan1", "lightcyan2", "lightcyan3", "lightcyan4",
        "lightgoldenrod", "lightgoldenrod1", "lightgoldenrod2", "lightgoldenrod3",
        "lightgoldenrod4", "lightgoldenrodyellow", "lightgray", "lightgrey",
        "lightpink", "lightpink1", "lightpink2", "lightpink3", "lightpink4",
        "lightsalmon", "lightsalmon1", "lightsalmon2", "lightsalmon3", "lightsalmon4",
        "lightseagreen", "lightskyblue", "lightskyblue1", "lightskyblue2",
        "lightskyblue3", "lightskyblue4", "lightslateblue", "lightslategray",
        "lightslategrey", "lightsteelblue", "lightsteelblue1", "lightsteelblue2",
        "lightsteelblue3", "lightsteelblue4", "lightyellow", "lightyellow1",
        "lightyellow2", "lightyellow3", "lightyellow4",
        "lime", "limegreen",
        "linen",
        "magenta1", "magenta2", "magenta3",
        "maroon", "maroon1", "maroon2", "maroon3", "maroon4",
        "mediumaquamarine", "mediumblue", "mediumorchid", "mediumorchid1",
        "mediumorchid2", "mediumorchid3", "mediumorchid4", "mediumpurple",
        "mediumpurple1", "mediumpurple2", "mediumpurple3", "mediumpurple4",
        "mediumseagreen", "mediumslateblue", "mediumspringgreen",
        "mediumturquoise", "mediumvioletred", "midnightblue",
        "mintcream", "mistyrose", "mistyrose1", "mistyrose2", "mistyrose3", "mistyrose4",
        "moccasin", "navajowhite", "navajowhite1", "navajowhite2", "navajowhite3",
        "navajowhite4", "navy", "navyblue",
        "oldlace", "olive", "olivedrab", "olivedrab1", "olivedrab2", "olivedrab3",
        "olivedrab4", "orange", "orange1", "orange2", "orange3", "orange4",
        "orangered", "orangered1", "orangered2", "orangered3", "orangered4",
        "orchid", "orchid1", "orchid2", "orchid3", "orchid4",
        "palegoldenrod", "palegreen", "palegreen1", "palegreen2", "palegreen3",
        "palegreen4", "paleturquoise", "paleturquoise1", "paleturquoise2",
        "paleturquoise3", "paleturquoise4", "palevioletred", "palevioletred1",
        "palevioletred2", "palevioletred3", "palevioletred4", "papayawhip",
        "peachpuff", "peachpuff1", "peachpuff2", "peachpuff3", "peachpuff4",
        "peru", "pink", "pink1", "pink2", "pink3", "pink4",
        "plum", "plum1", "plum2", "plum3", "plum4",
        "powderblue", "purple", "purple1", "purple2", "purple3", "purple4",
        "rebeccapurple",
        "red1", "red2", "red3", "rosybrown", "rosybrown1", "rosybrown2", "rosybrown3",
        "rosybrown4", "royalblue", "royalblue1", "royalblue2", "royalblue3", "royalblue4",
        "saddlebrown", "salmon", "salmon1", "salmon2", "salmon3", "salmon4",
        "sandybrown", "seagreen", "seagreen1", "seagreen2", "seagreen3", "seagreen4",
        "seashell", "seashell1", "seashell2", "seashell3", "seashell4",
        "sienna", "sienna1", "sienna2", "sienna3", "sienna4",
        "silver", "skyblue", "skyblue1", "skyblue2", "skyblue3", "skyblue4",
        "slateblue", "slateblue1", "slateblue2", "slateblue3", "slateblue4",
        "slategray", "slategray1", "slategray2", "slategray3", "slategray4",
        "slategrey", "snow", "snow1", "snow2", "snow3", "snow4",
        "springgreen", "springgreen1", "springgreen2", "springgreen3", "springgreen4",
        "steelblue", "steelblue1", "steelblue2", "steelblue3", "steelblue4",
        "tan", "tan1", "tan2", "tan3", "tan4",
        "teal", "thistle", "thistle1", "thistle2", "thistle3", "thistle4",
        "tomato", "tomato1", "tomato2", "tomato3", "tomato4",
        "turquoise", "turquoise1", "turquoise2", "turquoise3", "turquoise4",
        "violet", "violetred", "violetred1", "violetred2", "violetred3", "violetred4",
        "wheat", "wheat1", "wheat2", "wheat3", "wheat4",
        "white", "whitesmoke", "yellow1", "yellow2", "yellow3", "yellow4",
        "yellowgreen",

        // Link
        "link",
    };

    /// <summary>
    /// Normalizes tag content to match Spectre.Console's expected format.
    /// Spectre requires link=url without spaces around the '=', but agent
    /// output may contain a space before '=' (e.g. "link = url").
    /// This normalization strips spaces adjacent to '=' so the tag is valid.
    /// </summary>
    private static string NormalizeTagContent(string tagContent)
    {
        if (string.IsNullOrEmpty(tagContent))
            return tagContent;
        int eqIdx = tagContent.IndexOf('=');
        if (eqIdx < 0)
            return tagContent;
        // Only normalize if there are spaces before '='
        if (eqIdx > 0 && tagContent[eqIdx - 1] == ' ')
        {
            // Remove spaces immediately before '='
            int trimEnd = eqIdx - 1;
            while (trimEnd >= 0 && tagContent[trimEnd] == ' ')
                trimEnd--;
            // Also remove spaces immediately after '='
            int trimStart = eqIdx + 1;
            while (trimStart < tagContent.Length && tagContent[trimStart] == ' ')
                trimStart++;
            // Rebuild: part before spaces + '=' + part after spaces
            string before = tagContent.Substring(0, trimEnd + 1);
            string after = tagContent.Substring(trimStart);
            return before + "=" + after;
        }
        return tagContent;
    }

    /// <summary>
    /// Returns true if <paramref name="tagName"/> is a known Spectre.Console
    /// tag/style. Only used for rejecting improbable tag names that appeared
    /// after whitespace in content.
    /// </summary>
    private static bool IsSpectreKnownTag(string tagName)
    {
        if (string.IsNullOrEmpty(tagName))
            return false;

        // Hex colors like #1a3a1a, #ff00ff are always valid Spectre tags
        if (tagName.Contains('#'))
            return true;

        // Strip any style attributes like "on color" or "link=url"
        if (tagName.StartsWith("link=", StringComparison.OrdinalIgnoreCase)
            || tagName.StartsWith("link ", StringComparison.OrdinalIgnoreCase))
            return true;

        // For combined styles like "bold on blue" or "lime on #1a3a1a",
        // check if there's an "on" keyword separating two style tokens.
        int spaceIdx = tagName.IndexOf(' ');
        if (spaceIdx > 0)
        {
            int onIdx = tagName.IndexOf(" on ", StringComparison.OrdinalIgnoreCase);
            if (onIdx > 0)
            {
                string beforeOn = tagName.Substring(0, onIdx);
                string afterOn = tagName.Substring(onIdx + 4);
                return IsKnownStyleToken(beforeOn) && IsKnownStyleToken(afterOn);
            }

            // Without "on", it may be a combined style like "bold italic".
            // Split by spaces and check each token individually.
            string[] tokens = tagName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            bool allKnown = true;
            foreach (var t in tokens)
            {
                if (!IsKnownStyleToken(t)) { allKnown = false; break; }
            }
            if (allKnown) return true;
        }

        string baseName = spaceIdx >= 0 ? tagName.Substring(0, spaceIdx) : tagName;
        return _knownSpectreTags.Contains(baseName);
    }

    /// <summary>
    /// Checks whether a style token string (which may contain multiple
    /// space-separated tokens like "bold gray89") is valid.
    /// Supports hex colors and known decoration/color keywords.
    /// </summary>
    private static bool IsKnownStyleToken(string token)
    {
        token = token.Trim();
        if (string.IsNullOrEmpty(token))
            return false;

        // If the token contains spaces, split and validate each part
        int spaceIdx = token.IndexOf(' ');
        if (spaceIdx >= 0)
        {
            string[] parts = token.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (!IsKnownSingleStyleToken(part))
                    return false;
            }
            return true;
        }

        return IsKnownSingleStyleToken(token);
    }

    /// <summary>
    /// Checks a single style token (no spaces) against known Spectre keywords.
    /// Hex colors and named colors/decoartions are accepted.
    /// </summary>
    private static bool IsKnownSingleStyleToken(string token)
    {
        token = token.Trim();
        if (string.IsNullOrEmpty(token))
            return false;
        // Hex color
        if (token.StartsWith('#'))
            return true;
        // Known keyword
        return _knownSpectreTags.Contains(token);
    }

    /// <summary>
    /// Process a pre-formatted markup string where [tag]…[/tag] sequences
    /// have zero visible width. Preserves markup tags in output.
    /// </summary>
    public void ProcessMarkupDelta(string markup)
    {
        int availableWidth = GetAvailableWidth();
        bool insideTag = false;
        int realVisibleWordLen = Markup.Remove(markup).Length;
        int visibleWordLen = 0;

        for (int i = 0; i < markup.Length; i++)
        {
            char c = markup[i];

            // ── tag boundary detection ───────────────────────────────
            if (!insideTag && c == '[')
            {
                // Flush any pending visible word before entering tag mode.
                // This prevents the visible word from being split mid-word
                // by the word-wrap logic when the NEXT characters are a tag
                // (like [/]) rather than visible text. Without this, the
                // visibleWordLen > remaining check can trigger a split right
                // at the '[' of a [/] close tag, corrupting the markup.
                FlushWordBuffer(availableWidth, visibleWordLen);
                visibleWordLen = 0;

                // Spectre uses [[ to represent a literal '['. Preserve
                // the double-bracket escape in the output so Spectre's
                // markup parser will render it as a literal '['.
                if (i + 1 < markup.Length && markup[i + 1] == '[')
                {
                    _wordBuffer.Append(c);
                    _wordBuffer.Append(c);
                    i++; // skip the second '['
                    visibleWordLen++;
                    continue;
                }

                // Enter tag mode and start accumulating tag content.
                // Let the tag validator at ']' decide if it's a valid
                // Spectre tag or literal content. This is safer than
                // heuristic context checks, which can fail for patterns
                // like "and[bold]" where the bracket follows a letter.
                insideTag = true;
                _wordBuffer.Append(c);
                continue;
            }

            if (insideTag && c == ']')
            {
                insideTag = false;
                _wordBuffer.Append(c);
                // Determine whether this is an opening tag or closing tag
                // by inspecting the tag content (everything between '[' and ']').
                // The buffer now ends with "...]", so we search backwards.
                int closePos = _wordBuffer.Length - 1;
                int openPos = _wordBuffer.ToString().LastIndexOf('[', closePos - 1);
                string tagContent = _wordBuffer.ToString(openPos + 1, closePos - openPos - 1);

                // ── Validate tag content ────────────────────────────────
                // If the content between [ and ] doesn't look like a valid
                // Spectre tag name (e.g. ["a"] is not valid), treat the
                // brackets as escaped literal content: remove the [ and ]
                // from the buffer and replace with [[ and ]].
                bool shouldEscape = false;

                if (tagContent != "/"
                    && tagContent.Length > 0
                    && !tagContent.StartsWith("/"))
                {
                    // Secondary guard: single-character tag names (like [x],
                    // [b], [5]) are almost certainly literal code content
                    // (array access, variable names), not intentional markup.
                    if (tagContent.Length <= 1)
                    {
                        shouldEscape = true;
                    }
                    else if (!IsValidTagName(tagContent))
                    {
                        // Original check: invalid characters in tag name
                        shouldEscape = true;
                    }
                    else if (!IsSpectreKnownTag(tagContent))
                    {
                        // Tertiary guard: unknown tag names (not in the known
                        // Spectre style set) are almost certainly literal
                        // content that happened to be bracketed. Escape them
                        // to avoid pushing bogus tags onto the stack.
                        shouldEscape = true;
                    }
                }

                if (shouldEscape)
                {
                    // Back out: replace raw brackets with escaped ones
                    // so they render as literal characters.
                    _wordBuffer.Remove(openPos, closePos - openPos + 1);
                    _wordBuffer.Length = openPos;
                    // Re-append with escaped brackets
                    _wordBuffer.Append("[[");
                    _wordBuffer.Append(tagContent);
                    _wordBuffer.Append("]]");
                    visibleWordLen += tagContent.Length + 4; // [[ + content + ]]
                    continue;
                }

                // ── Normalize Spectre tag format ────────────────────
                // Spectre.Console requires link=url without spaces around
                // the '=', but agent output may produce link = url.
                // Normalize by removing spaces adjacent to '=' in tag content.
                string normalizedTag = NormalizeTagContent(tagContent);
                if (normalizedTag != tagContent)
                {
                    // Update the buffer to use normalized tag
                    _wordBuffer.Remove(openPos, closePos - openPos + 1);
                    _wordBuffer.Length = openPos;
                    _wordBuffer.Append("[");
                    _wordBuffer.Append(normalizedTag);
                    _wordBuffer.Append("]");
                    tagContent = normalizedTag;
                    // Recalculate closePos since buffer changed
                    closePos = _wordBuffer.Length - 1;
                }

                if (tagContent == "/")
                {
                    // Generic close [/] — pop the most recent tag
                    if (_openMarkupTags.Count > 0)
                        _openMarkupTags.Pop();
                }
                else if (tagContent.StartsWith("/"))
                {
                    // Explicit close like [/dim], [/bold] — pop matching tag
                    string closeTagName = tagContent.Substring(1);
                    if (!string.IsNullOrEmpty(closeTagName) && _openMarkupTags.Count > 0)
                    {
                        var tempStack = new Stack<string>();
                        bool found = false;
                        while (_openMarkupTags.Count > 0)
                        {
                            string top = _openMarkupTags.Pop();
                            if (string.Equals(top, closeTagName, StringComparison.OrdinalIgnoreCase))
                            {
                                found = true;
                                break;
                            }
                            tempStack.Push(top);
                        }
                        while (tempStack.Count > 0)
                        {
                            _openMarkupTags.Push(tempStack.Pop());
                        }
                    }
                }
                else if (!string.IsNullOrEmpty(tagContent))
                {
                    // Opening tag like [dim], [bold] — push onto stack
                    _openMarkupTags.Push(tagContent);
                }

                // Flush the processed tag text to output immediately.
                // This prevents subsequent visible characters from being
                // combined with the tag in the word buffer, which could
                // cause the mid-word split logic to cut through the
                // middle of a tag (like splitting [bold yellow...] into
                // [bold yellow st
                // rikethrough]some text line).
                int tagVisibleLen = 0; // tags have zero visible width
                FlushWordBuffer(availableWidth, tagVisibleLen);
                continue;
            }

            if (insideTag)
            {
                _wordBuffer.Append(c);
                continue;
            }

            // ── visible (non-tag) characters below ──────────────────
            // Spectre uses ]] to represent a literal ']'. Preserve
            // the double-bracket escape in the output so Spectre's
            // markup parser will render it as a literal ']'.
            if (!insideTag && c == ']' && i + 1 < markup.Length && markup[i + 1] == ']')
            {
                _wordBuffer.Append("]]");
                i++; // skip the second ']'
                visibleWordLen++;
                continue;
            }

            if (c == '\n')
            {
                FlushWordBuffer(availableWidth, visibleWordLen);
                visibleWordLen = 0;
                WriteNewLine();
                _currentLineLength = 0;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                FlushWordBuffer(availableWidth, visibleWordLen);
                visibleWordLen = 0;
                if (_currentLineLength + 1 <= availableWidth)
                {
                    _output.Write(c.ToString());
                    _currentLineLength++;
                }
                else
                {
                    WriteNewLine();
                    _output.Write(c.ToString());
                    _currentLineLength = 1;
                }
                continue;
            }

            // Regular visible character
            _wordBuffer.Append(c);
            visibleWordLen++;

            // If remaining space on current line can't fit the visible word, split.
            int remaining = availableWidth - _currentLineLength;
            if (visibleWordLen > remaining)
            {
                string full = _wordBuffer.ToString();
                int charsToEmit = Math.Min(remaining, full.Length);

                if (charsToEmit > 0)
                {
                    string emitPart = full.Substring(0, charsToEmit);
                    _output.Write(emitPart);
                    _currentLineLength += Math.Min(charsToEmit, visibleWordLen);
                    _wordBuffer.Clear();
                    _wordBuffer.Append(full.Substring(charsToEmit));
                    visibleWordLen = Math.Max(0, visibleWordLen - Math.Min(charsToEmit, visibleWordLen));
                }

                if (_wordBuffer.Length > 0)
                {
                    // Before calling WriteNewLine, write any pending markup tags
                    // from the buffer to the current line output. This ensures
                    // WriteNewLine's close/reopen has matching open tags on the
                    // current line.
                    string fullBuf = _wordBuffer.ToString();
                    int rawLen = fullBuf.Length;
                    int tagLen = rawLen - visibleWordLen;

                    if (tagLen > 0 && remaining <= 0)
                    {
                        string pendingTags = fullBuf.Substring(0, tagLen);
                        _output.Write(pendingTags);
                        // Tags have zero visible width, so _currentLineLength unchanged.
                        // Remove the emitted tags from the buffer.
                        _wordBuffer.Remove(0, tagLen);
                    }

                    WriteNewLine();
                    _currentLineLength = 0;
                }
            }
        }

        FlushWordBuffer(availableWidth, visibleWordLen);
    }

    public void Finish()
    {
        int availableWidth = GetAvailableWidth();
        FlushWordBuffer(availableWidth);
        // Close all open markup tags so the output is valid self-contained markup.
        // This also resets the open-tag stack for reuse across multiple
        // ProcessMarkupDelta calls (e.g. one per code block line).
        foreach (string _ in _openMarkupTags)
        {
            _output.Write("[/]");
        }
        _openMarkupTags.Clear();
        _output.WriteLine();
    }

    private void FlushWordBuffer(int availableWidth)
        => FlushWordBuffer(availableWidth, _wordBuffer.Length);

    private void FlushWordBuffer(int availableWidth, int visibleLength)
    {
        if (_wordBuffer.Length == 0) return;

        string word = _wordBuffer.ToString();

        if (_currentLineLength + visibleLength <= availableWidth)
        {
            _output.Write(word);
            _currentLineLength += visibleLength;
        }
        else
        {
            if (_currentLineLength > 0)
                WriteNewLine();
            _output.Write(word);
            _currentLineLength = visibleLength;
        }

        _wordBuffer.Clear();
    }

    private void WriteNewLine()
    {
        // Close all currently open markup tags before the line break
        // so the current line is self-contained markup.
        foreach (string tag in _openMarkupTags)
        {
            _output.Write("[/]");
        }

        _output.WriteLine();
        _output.Write(_newlinePrefixLenght);

        // Re-emit all open markup tags after the newline and prefix
        // so the next line is also self-contained.
        foreach (string tag in _openMarkupTags)
        {
            _output.Write($"[{tag}]");
        }
    }
}
