using System.Text;
using OpenClawPTT.Formatting;
using OpenClawPTT.Services.StatusParts;
using Spectre.Console;
using StreamShell;

namespace OpenClawPTT.Services;

/// <summary>
/// Table-style bottom panel displaying agent status with columns for
/// Name, Status, Model, and Context tokens.  Organised into an "Active"
/// section (the currently selected agent) and an "Others" section
/// (remaining main agents).
///
/// Keyboard navigation:
///   Arrow Down on empty input → selection mode (AllowUserField = false)
///   Arrow Up/Down        → move selection highlight among Others
///   Enter                → switch active agent to the selected one,
///                          exit selection mode
///   Escape / Arrow Up
///     at first row       → exit selection mode, deselect everything
/// </summary>
public sealed class AgentStatusBottomPanel : IBottomPanel, IDisposable
{
    // ── Margins ───────────────────────────────────────────────────────────
    private const int LeftMargin = 2;
    private const int BottomMargin = 1;
    private const string LeftPad = "  "; // LeftMargin spaces

    // ── Column constants ──────────────────────────────────────────────────
    private const int MinNameWidth    = 4;
    private const int MinStatusWidth  = 1;   // single emoji
    private const int MinModelWidth   = 3;   // "…"
    private const int MinContextWidth = 3;   // "…"
    private const int MaxColumnWidth  = 20;

    private const string HeaderName    = "Name";
    private const string HeaderStatus  = "Status";
    private const string HeaderModel   = "Model";
    private const string HeaderContext = "Context";

    private const string SectionLabelActive = " Active";
    private const string SectionLabelOthers = " Others";

    private const int MaxNameDisplayLength = 10;

    // ── Dependencies ──────────────────────────────────────────────────────
    private readonly IAgentStatusTracker _tracker;
    private readonly MainAgentsPart _agentsPart;

    // ── State ─────────────────────────────────────────────────────────────
    private readonly object _sync = new();
    private bool _disposed;

    private int _version;
    private int _renderedVersion;

    /// <summary>Whether keyboard selection mode is active.</summary>
    private bool _isSelectionMode;

    /// <summary>Index within the Others list currently highlighted.</summary>
    private int _selectedIndex;

    /// <summary>
    /// Ordered session keys of agents shown in the Others section.
    /// Filled during each render so TryHandleKey can map
    /// <see cref="_selectedIndex"/> to the correct session key.
    /// </summary>
    private readonly List<string> _otherSessionKeys = new();

    /// <summary>
    /// Last <c>currentInput</c> value passed to <see cref="GetLines"/>.
    /// Cached so <see cref="TryHandleKey"/> can gate selection mode
    /// entry on an empty input field.
    /// </summary>
    private string _lastCurrentInput = string.Empty;

    // ── Cached rendering ──────────────────────────────────────────────────
    private int _cachedConsoleWidth;
    private readonly List<string> _lines = new(16);

    // ── Construction ──────────────────────────────────────────────────────

    public AgentStatusBottomPanel(
        IAgentStatusTracker tracker,
        MainAgentsPart agentsPart)
    {
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        _agentsPart = agentsPart ?? throw new ArgumentNullException(nameof(agentsPart));

        _cachedConsoleWidth = ConsoleMetrics.GetWindowWidth();
        _version = 1;

        _tracker.Changed += OnTrackerChanged;
        AgentRegistry.ActiveSessionChanged += OnActiveSessionChanged;

        // Trigger an initial refresh so the first GetLines has data.
        _agentsPart.RefreshVisibleAgents();
    }

    // ── IBottomPanel ──────────────────────────────────────────────────────

    /// <summary>
    /// Number of lines returned by <see cref="GetLines"/>.  Always in
    /// sync — <see cref="GetLines"/> pads with empty strings to match.
    /// </summary>
    public int LineCount
    {
        get
        {
            var others = _agentsPart.GetVisibleAgents().Count;
            // left-margin spacer + header + spacer + Active label + Active row
            // + Others label + N other rows + selection hint? + bottom margin
            int hint = _isSelectionMode ? 1 : 0;
            return 1 + 1 + 1 + 1 + 1 + 1 + others + hint + BottomMargin;
        }
    }

    public bool IsDirty
    {
        get
        {
            lock (_sync)
            {
                if (_disposed) return false;
                return _version != _renderedVersion;
            }
        }
    }

    public string? CurrentSuggestion => null;

    public bool ShowBottomSeparator => true;

    /// <summary>
    /// <c>false</c> when selection mode is active (the user input field
    /// is hidden and arrow-key navigation takes over).
    /// </summary>
    public bool AllowUserField => !_isSelectionMode;

    public IReadOnlyList<string> GetLines(string currentInput)
    {
        lock (_sync)
        {
            var targetLineCount = LineCount;

            if (_disposed)
                return PadToLineCount(targetLineCount);

            try
            {
                _cachedConsoleWidth = ConsoleMetrics.GetWindowWidth();
                _lines.Clear();

                // Cache the current input for TryHandleKey gating
                _lastCurrentInput = currentInput ?? string.Empty;

                // Refresh data
                _agentsPart.RefreshVisibleAgents();

                // ── Gather data ───────────────────────────────────────────
                var activeSessionKey = AgentRegistry.ActiveSessionKey;
                var activeSnapshot = activeSessionKey is not null
                    ? _tracker.Get(activeSessionKey)
                    : null;
                var activeInfo = GetActiveAgentInfo();

                var visibleAgents = _agentsPart.GetVisibleAgents();
                _otherSessionKeys.Clear();
                foreach (var (snapshot, _) in visibleAgents)
                {
                    if (snapshot.SessionKey is not null)
                        _otherSessionKeys.Add(snapshot.SessionKey);
                }

                // Clamp selection index
                if (_selectedIndex >= _otherSessionKeys.Count)
                    _selectedIndex = Math.Max(0, _otherSessionKeys.Count - 1);

                // ── Build column data for all rows ────────────────────────
                var activeData = CellData.FromAgent(activeSnapshot, activeInfo);
                var otherData = new List<(CellData Cells, bool Selected)>(visibleAgents.Count);
                for (int i = 0; i < visibleAgents.Count; i++)
                {
                    var (snapshot, agent) = visibleAgents[i];
                    var cells = CellData.FromAgent(snapshot, agent);
                    bool sel = _isSelectionMode && i == _selectedIndex;
                    otherData.Add((cells, sel));
                }

                // ── Compute column widths from all rows ───────────────────
                var colW = ComputeColumnWidths(activeData, otherData);

                // ── Emit lines ────────────────────────────────────────────

                // Left-margin spacer
                _lines.Add(string.Empty);

                // Header row
                _lines.Add(LeftPad + RenderHeaderRow(colW));

                // Spacer
                _lines.Add(string.Empty);

                // Active section
                _lines.Add(LeftPad + SectionLabelActive);
                _lines.Add(LeftPad + RenderDataRow(activeData, colW, selected: false));

                // Others section
                _lines.Add(LeftPad + SectionLabelOthers);

                if (otherData.Count == 0)
                {
                    _lines.Add(LeftPad + "  [grey](none)[/]");
                }
                else
                {
                    foreach (var (cells, selected) in otherData)
                        _lines.Add(LeftPad + RenderDataRow(cells, colW, selected));
                }

                // Selection hint
                if (_isSelectionMode)
                    _lines.Add(LeftPad + "[grey]\u2191\u2193 navigate  Enter select  Esc back[/]");
            }
            catch
            {
                _lines.Clear();
            }
            finally
            {
                _renderedVersion = _version;
            }

            // ── Pad to exact LineCount ───────────────────────────────────
            return PadToLineCount(targetLineCount);
        }
    }

    public void ClearDirty()
    {
        lock (_sync) { _renderedVersion = _version; }
    }

    public bool TryHandleKey(ConsoleKeyInfo key)
    {
        lock (_sync)
        {
            if (!_isSelectionMode)
            {
                // Only enter selection mode when the input field is empty.
                if (key.Key == ConsoleKey.DownArrow
                    && _otherSessionKeys.Count > 0
                    && _lastCurrentInput.Length == 0)
                {
                    EnterSelectionMode();
                    return true;
                }

                return false;
            }

            switch (key.Key)
            {
                case ConsoleKey.DownArrow:
                    if (_selectedIndex < _otherSessionKeys.Count - 1)
                    {
                        _selectedIndex++;
                        MarkDirty();
                    }
                    return true;

                case ConsoleKey.UpArrow:
                    if (_selectedIndex > 0)
                    {
                        _selectedIndex--;
                        MarkDirty();
                    }
                    else
                    {
                        ExitSelectionMode();
                    }
                    return true;

                case ConsoleKey.Enter:
                    SelectCurrentAgent();
                    return true;

                case ConsoleKey.Escape:
                    ExitSelectionMode();
                    return true;

                default:
                    ExitSelectionMode();
                    return false;
            }
        }
    }

    public Task RunAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
        }

        _tracker.Changed -= OnTrackerChanged;
        AgentRegistry.ActiveSessionChanged -= OnActiveSessionChanged;
        _otherSessionKeys.Clear();
        _lines.Clear();
    }

    // ── Selection helpers ─────────────────────────────────────────────────

    private void EnterSelectionMode()
    {
        _isSelectionMode = true;
        _selectedIndex = 0;
        MarkDirty();
    }

    private void ExitSelectionMode()
    {
        _isSelectionMode = false;
        _selectedIndex = 0;
        MarkDirty();
    }

    private void SelectCurrentAgent()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _otherSessionKeys.Count)
        {
            ExitSelectionMode();
            return;
        }

        var sessionKey = _otherSessionKeys[_selectedIndex];
        ExitSelectionMode();

        _ = Task.Run(() =>
        {
            var visible = _agentsPart.GetVisibleAgents();
            var target = visible.FirstOrDefault(v =>
                v.Snapshot.SessionKey == sessionKey);
            if (target.Agent is not null)
                AgentRegistry.SetActiveAgent(target.Agent.AgentId);
        });
    }

    // ── Column width computation ─────────────────────────────────────────

    /// <summary>Holds raw display values for one row's four columns.</summary>
    private readonly record struct CellData(
        string Name,      // already Markup.Escape'd
        string Status,    // already Markup.Escape'd
        string Model,     // already Markup.Escape'd (or grey "…")
        string Context    // already formatted (or grey "…")
    )
    {
        public static CellData FromAgent(AgentStatusSnapshot? snapshot, AgentInfo? info)
        {
            if (snapshot is null || info is null)
                return new CellData("?", "—", "[grey]…[/]", "[grey]…[/]");

            var name = info.Name ?? "?";
            name = name.Length > MaxNameDisplayLength
                ? Markup.Escape(name[..MaxNameDisplayLength])
                : Markup.Escape(name);

            var status = Markup.Escape(snapshot.GetStatusEmoji());

            var model = !string.IsNullOrWhiteSpace(snapshot.Model)
                ? Markup.Escape(ModelPart.ShortenModelName(snapshot.Model))
                : "[grey]…[/]";

            var context = snapshot.ContextTokens is { } ct and > 0
                ? ContextPart.FormatTokenCount(ct)
                : "[grey]…[/]";

            return new CellData(name, status, model, context);
        }

        /// <summary>Display width of the value (ignoring Spectre markup tags).</summary>
        public int NameWidth    => CharacterWidth.GetDisplayWidth(StripMarkup(Name));
        public int StatusWidth  => CharacterWidth.GetDisplayWidth(StripMarkup(Status));
        public int ModelWidth   => CharacterWidth.GetDisplayWidth(StripMarkup(Model));
        public int ContextWidth => CharacterWidth.GetDisplayWidth(StripMarkup(Context));
    }

    /// <summary>Column widths for the four display columns.</summary>
    private readonly record struct ColumnWidths(int Name, int Status, int Model, int Context);

    private static ColumnWidths ComputeColumnWidths(
        CellData active,
        List<(CellData Cells, bool Selected)> others)
    {
        int nw = Math.Max(MinNameWidth,    Math.Min(MaxColumnWidth, HeaderName.Length));
        int sw = Math.Max(MinStatusWidth,  Math.Min(MaxColumnWidth, HeaderStatus.Length));
        int mw = Math.Max(MinModelWidth,   Math.Min(MaxColumnWidth, HeaderModel.Length));
        int cw = Math.Max(MinContextWidth, Math.Min(MaxColumnWidth, HeaderContext.Length));

        void Consider(CellData c)
        {
            nw = Math.Max(nw, Math.Min(MaxColumnWidth, c.NameWidth));
            sw = Math.Max(sw, Math.Min(MaxColumnWidth, c.StatusWidth));
            mw = Math.Max(mw, Math.Min(MaxColumnWidth, c.ModelWidth));
            cw = Math.Max(cw, Math.Min(MaxColumnWidth, c.ContextWidth));
        }

        Consider(active);
        foreach (var (cells, _) in others)
            Consider(cells);

        return new ColumnWidths(nw, sw, mw, cw);
    }

    // ── Row rendering ────────────────────────────────────────────────────

    private static string RenderHeaderRow(ColumnWidths w)
    {
        return $"│ [bold]{HeaderName.PadRight(w.Name)}[/] │ [bold]{HeaderStatus.PadRight(w.Status)}[/] │ [bold]{HeaderModel.PadRight(w.Model)}[/] │ [bold]{HeaderContext.PadRight(w.Context)}[/] │";
    }

    private static string RenderDataRow(CellData cells, ColumnWidths w, bool selected)
    {
        // Pad raw values (without markup) to column widths.
        // We need to pad the escaped values to the right width.
        var rawName    = StripMarkup(cells.Name);
        var rawStatus  = StripMarkup(cells.Status);
        var rawModel   = StripMarkup(cells.Model);
        var rawContext = StripMarkup(cells.Context);

        var row = $"│ {PadForTable(rawName, cells.Name, w.Name)} │ {PadForTable(rawStatus, cells.Status, w.Status)} │ {PadForTable(rawModel, cells.Model, w.Model)} │ {PadForTable(rawContext, cells.Context, w.Context)} │";

        return selected
            ? $"[default on gray17]{row}[/]"
            : row;
    }

    /// <summary>
    /// Pads a Spectre-markup value to the given display width.
    /// Appends spaces after the markup to reach <paramref name="targetWidth"/>.
    /// </summary>
    private static string PadForTable(string raw, string markup, int targetWidth)
    {
        int currentWidth = CharacterWidth.GetDisplayWidth(raw);
        int padding = targetWidth - currentWidth;
        return padding > 0 ? markup + new string(' ', padding) : markup;
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>Strips Spectre.Console markup tags from a string for width measurement.</summary>
    private static string StripMarkup(string markup)
    {
        if (string.IsNullOrEmpty(markup)) return string.Empty;

        var sb = new StringBuilder(markup.Length);
        bool inTag = false;
        for (int i = 0; i < markup.Length; i++)
        {
            char c = markup[i];
            if (c == '[' && i + 1 < markup.Length)
            {
                // Escape sequence [[ → literal [
                if (markup[i + 1] == '[')
                {
                    sb.Append('[');
                    i++;
                    continue;
                }
                inTag = true;
                continue;
            }
            if (c == ']' && inTag)
            {
                inTag = false;
                continue;
            }
            if (!inTag)
                sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Returns exactly <paramref name="count"/> lines, padding with empty
    /// strings and adding bottom margins as needed.
    /// </summary>
    private string[] PadToLineCount(int count)
    {
        while (_lines.Count < count)
            _lines.Add(string.Empty);

        // Trim excess if we somehow produced too many lines
        if (_lines.Count > count)
            _lines.RemoveRange(count, _lines.Count - count);

        return _lines.ToArray();
    }

    private static AgentInfo? GetActiveAgentInfo()
    {
        var sessionKey = AgentRegistry.ActiveSessionKey;
        if (sessionKey is null) return null;

        return AgentRegistry.Agents.FirstOrDefault(a => a.SessionKey == sessionKey);
    }

    private void OnTrackerChanged() => MarkDirty();
    private void OnActiveSessionChanged(string? _) => MarkDirty();

    private void MarkDirty()
    {
        lock (_sync) { _version++; }
    }
}
