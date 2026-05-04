using Spectre.Console;

namespace OpenClawPTT.Services;

/// <summary>
/// Renders diff results to tool output with color coding.
/// </summary>
public sealed class DiffRenderer
{
    private readonly IToolOutput _output;

    public DiffRenderer(IToolOutput output)
    {
        _output = output;
    }

    /// <summary>
    /// Renders a diff result with color coding (red for removed, green for added).
    /// </summary>
    /// <param name="result">The diff result to render.</param>
    /// <param name="maxRows">Maximum number of rows to display.</param>
    public void RenderDiff(DiffResult result, int maxRows = 30)
    {
        if (result.Entries.Count == 0 || result.IsEmpty)
        {
            return;
        }

        // Compact: prioritize changed lines, keep only 1-2 context lines
        var displayLines = CompactDiff(result.Entries);

        // Enforce maxRows
        bool hasMore = displayLines.Count > maxRows;
        var shown = hasMore ? displayLines.Take(maxRows).ToList() : displayLines;

        foreach (var entry in shown)
        {
            RenderDiffLine(entry);
        }

        if (hasMore)
        {
            int shownRemoved = shown.Count(d => d.Operation == DiffOperation.Remove);
            int shownAdded = shown.Count(d => d.Operation == DiffOperation.Add);
            int remainingRemoved = result.Removals - shownRemoved;
            int remainingAdded = result.Additions - shownAdded;
            _output.PrintMarkup($"  [dim]... {displayLines.Count - maxRows} more changes (-{remainingRemoved} +{remainingAdded})[/]\n");
        }
    }

    /// <summary>
    /// Renders a single diff entry with appropriate color coding.
    /// </summary>
    public void RenderDiffLine(DiffEntry entry)
    {
        string markup = entry.Operation switch
        {
            DiffOperation.Add => $"[default on springgreen4]+ {Markup.Escape(entry.Line)}[/]\n",
            DiffOperation.Remove => $"[default on darkred]- {Markup.Escape(entry.Line)}[/]\n",
            _ => $"  {Markup.Escape(entry.Line)}\n"
        };
        _output.PrintMarkup(markup);
    }

    /// <summary>
    /// Renders text with a prefix and truncation.
    /// </summary>
    public void RenderPlainText(string text, string prefix, int rightMarginIndent, int maxRows = 8)
    {
        _output.Print(prefix, ConsoleColor.DarkGray);
        _output.PrintTruncated(text, prefix, rightMarginIndent, ConsoleColor.White, maxRows);
    }

    /// <summary>
    /// Compact diff entries for display: keeps all Add/Remove lines but
    /// limits unchanged context lines to at most 2 before/after each change.
    /// </summary>
    private static List<DiffEntry> CompactDiff(List<DiffEntry> diff)
    {
        int n = diff.Count;
        var keepEqual = new bool[n];

        for (int i = 0; i < n; i++)
        {
            if (diff[i].Operation == DiffOperation.Equal)
                continue;

            // Found a change — mark nearby Equal lines as context
            for (int j = Math.Max(0, i - 2); j < i; j++)
                if (diff[j].Operation == DiffOperation.Equal)
                    keepEqual[j] = true;

            for (int j = i + 1; j <= Math.Min(n - 1, i + 2); j++)
                if (diff[j].Operation == DiffOperation.Equal)
                    keepEqual[j] = true;
        }

        var result = new List<DiffEntry>();
        for (int i = 0; i < n; i++)
        {
            if (diff[i].Operation != DiffOperation.Equal || keepEqual[i])
                result.Add(diff[i]);
        }

        return result;
    }
}
