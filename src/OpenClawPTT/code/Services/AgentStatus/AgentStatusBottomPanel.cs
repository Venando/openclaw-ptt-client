using System.Text;
using System.Text.RegularExpressions;
using StreamShell;

namespace OpenClawPTT.Services;

/// <summary>
/// StreamShell bottom panel that displays all agents and their subagent statuses.
/// Fixed at 5 lines high.
/// - Lines 1-3: Active subagents (grouped by parent agent, with emoji + status).
/// - Line 4: All main agents centered, showing emoji, name, color, and status emoji.
/// </summary>
public sealed class AgentStatusBottomPanel : IBottomPanel
{
    private const int LineCountValue = 5;
    private readonly IAgentStatusTracker _tracker;
    private bool _isDirty;

    public AgentStatusBottomPanel(IAgentStatusTracker tracker)
    {
        _tracker = tracker;
        _tracker.Changed += OnTrackerChanged;
    }

    private void OnTrackerChanged() => _isDirty = true;

    public int LineCount => LineCountValue;
    public bool IsDirty => _isDirty;
    public void ClearDirty() => _isDirty = false;

    public IReadOnlyList<string> GetLines(string currentInput)
    {
        var lines = new string[LineCountValue];

        var all = _tracker.All;
        var mainAgents = all.Where(s => !s.IsSubagent).ToList();
        var activeSubs = all.Where(s => s.IsSubagent && !s.IsFinished).ToList();

        // Line 0: Tab autocomplete suggestion slot (must be empty)
        lines[0] = string.Empty;

        // Lines 1-3: Active subagents (up to 3)
        for (int i = 0; i < 3; i++)
        {
            lines[i + 1] = i < activeSubs.Count
                ? BuildSubagentLine(activeSubs[i], mainAgents)
                : string.Empty;
        }

        // Line 4: All main agents centered
        lines[4] = BuildCenteredMainAgentsLine(mainAgents);

        return lines;
    }

    // ─────────────────────────────────────────────────────────────────
    // Subagent lines
    // ─────────────────────────────────────────────────────────────────

    private static string BuildSubagentLine(AgentStatusSnapshot sub, List<AgentStatusSnapshot> mainAgents)
    {
        var parent = mainAgents.FirstOrDefault(m => m.SessionKey == sub.ParentSessionKey);
        var (parentEmoji, parentColor, parentName) = GetAgentDisplayInfo(parent);
        var subName = EscapeMarkup(ShortenSubagentName(sub.DisplayName));
        var statusEmoji = sub.GetStatusEmoji();

        // Parent indicator: colored emoji + short name
        var parentIndicator = $"[{parentColor}]{parentEmoji} {parentName}[/]";

        return $"  {parentIndicator} [grey]├─[/] {statusEmoji} {subName}";
    }

    // ─────────────────────────────────────────────────────────────────
    // Main agents line (centered)
    // ─────────────────────────────────────────────────────────────────

    private static string BuildCenteredMainAgentsLine(List<AgentStatusSnapshot> mainAgents)
    {
        if (mainAgents.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        for (int i = 0; i < mainAgents.Count; i++)
        {
            if (i > 0)
                sb.Append(" [grey]│[/] ");

            var agent = mainAgents[i];
            var (emoji, color, name) = GetAgentDisplayInfo(agent);
            var statusEmoji = agent.GetStatusEmoji();

            sb.Append($"[{color}]{emoji} {name}[/] {statusEmoji}");
        }

        return CenterMarkup(sb.ToString());
    }

    // ─────────────────────────────────────────────────────────────────
    // Centering helper
    // ─────────────────────────────────────────────────────────────────

    private static string CenterMarkup(string markup)
    {
        try
        {
            int consoleWidth = Console.WindowWidth > 0 ? Console.WindowWidth : 80;
            int visibleLen = GetVisibleLength(markup);
            int padding = Math.Max(0, (consoleWidth - visibleLen) / 2);
            return new string(' ', padding) + markup;
        }
        catch
        {
            return markup;
        }
    }

    /// <summary>
    /// Computes the visible character length of a Spectre markup string.
    /// Markup tags ([color], [/]) count as 0. Escaped brackets ([[, ]]) count as 1.
    /// </summary>
    private static int GetVisibleLength(string markup)
    {
        // Step 1: replace escaped brackets with single-char placeholders
        var temp = markup.Replace("[[", "\x01").Replace("]]", "\x02");

        // Step 2: strip markup tags
        temp = Regex.Replace(temp, @"\[[^\]]*\]", "");

        // Step 3: restore escaped brackets
        temp = temp.Replace("\x01", "[").Replace("\x02", "]");

        return temp.Length;
    }

    // ─────────────────────────────────────────────────────────────────
    // Agent display info (emoji, color, name) — pulls from AgentRegistry when available
    // ─────────────────────────────────────────────────────────────────

    private static (string emoji, string color, string name) GetAgentDisplayInfo(AgentStatusSnapshot? snapshot)
    {
        if (snapshot == null)
            return ("🤖", "grey", "Agent");

        // Try to match via AgentRegistry
        var registryAgent = AgentRegistry.Agents.FirstOrDefault(a => a.SessionKey == snapshot.SessionKey);
        if (registryAgent != null)
        {
            var emoji = AgentSettingsPersistenceLegacy.GetPersistedEmoji(registryAgent.AgentId) ?? "🤖";
            var color = AgentSettingsPersistenceLegacy.GetPersistedColor(registryAgent.AgentId) ?? "grey";
            var name = EscapeMarkup(registryAgent.Name);
            return (emoji, color, name);
        }

        // Fallback: use snapshot data
        var fallbackName = EscapeMarkup(ShortenMainAgentName(snapshot.DisplayName));
        return ("🤖", "grey", fallbackName);
    }

    // ─────────────────────────────────────────────────────────────────
    // Name helpers
    // ─────────────────────────────────────────────────────────────────

    private static string ShortenMainAgentName(string? displayName)
    {
        if (string.IsNullOrEmpty(displayName))
            return "Agent";
        // Extract the middle part from patterns like "webchat:g-agent-anime-main"
        if (displayName.Contains("-main", StringComparison.OrdinalIgnoreCase))
        {
            var parts = displayName.Split('-');
            if (parts.Length >= 3)
                return parts[^2]; // e.g. "anime" from "...-agent-anime-main"
        }
        return displayName.Length > 16 ? displayName[..16] : displayName;
    }

    private static string ShortenSubagentName(string? displayName)
    {
        if (string.IsNullOrEmpty(displayName))
            return "sub";
        // Extract UUID tail from "webchat:g-agent-anime-subagent-df70d2df..."
        var lastDash = displayName.LastIndexOf('-');
        if (lastDash >= 0 && lastDash < displayName.Length - 1)
        {
            var tail = displayName[(lastDash + 1)..];
            if (tail.Length >= 4)
                return tail.Length > 8 ? tail[..8] : tail;
        }
        return displayName.Length > 12 ? displayName[..12] : displayName;
    }

    /// <summary>
    /// Escapes Spectre markup characters in text so they don't break rendering.
    /// </summary>
    private static string EscapeMarkup(string text)
    {
        return text
            .Replace("[", "[[")
            .Replace("]", "]]");
    }
}
