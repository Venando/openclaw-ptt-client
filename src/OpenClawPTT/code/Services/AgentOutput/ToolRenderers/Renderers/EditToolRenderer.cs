using System.Text.Json;
using Spectre.Console;

namespace OpenClawPTT.Services;

public sealed class EditToolRenderer : IToolRenderer
{
    private readonly IToolOutput _output;

    public EditToolRenderer(IToolOutput output)
    {
        _output = output;
    }

    public string ToolName => "edit";

    public void Render(JsonElement args, int rightMarginIndent)
    {
        // Schema: { "path": "...", "edits": [{ "oldText": "...", "newText": "..." }] }
        if (args.TryGetProperty("path", out var pathProp))
        {
            string displayPath = FilePathDisplayHelper.FormatDisplayPath(pathProp.GetString() ?? "");
            _output.Print(displayPath, ConsoleColor.Gray);
        }

        if (args.TryGetProperty("edits", out var editsProp) && editsProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var edit in editsProp.EnumerateArray())
            {
                string oldText = edit.TryGetProperty("oldText", out var oldProp) ? oldProp.GetString() ?? "" : "";
                string newText = edit.TryGetProperty("newText", out var newProp) ? newProp.GetString() ?? "" : "";

                _output.PrintLine("");

                // Compute and render diff between oldText and newText
                var diffResult = ComputeLineDiff(oldText, newText);

                if (diffResult.Count == 0)
                {
                    // No differences — just show the content once
                    const string prefix = "  ";
                    _output.Print(prefix, ConsoleColor.DarkGray);
                    _output.PrintTruncated(oldText, prefix, rightMarginIndent, maxRows: 8);
                }
                else
                {
                    // Compact: prioritize changed lines, keep only 1-2 context lines
                    var displayLines = CompactDiff(diffResult);

                    int totalRemoved = diffResult.Count(d => d.Operation == DiffOperation.Remove);
                    int totalAdded = diffResult.Count(d => d.Operation == DiffOperation.Add);

                    // Enforce max 8 displayed lines
                    bool hasMore = displayLines.Count > 8;
                    var shown = hasMore ? displayLines.Take(8).ToList() : displayLines;

                    foreach (var entry in shown)
                        RenderDiffLine(entry);

                    if (hasMore)
                    {
                        int remainingRemoved = totalRemoved - shown.Count(d => d.Operation == DiffOperation.Remove);
                        int remainingAdded = totalAdded - shown.Count(d => d.Operation == DiffOperation.Add);
                        _output.PrintMarkup($"  [dim]... {displayLines.Count - 8} more changes (-{remainingRemoved} +{remainingAdded})[/]\n");
                    }
                }
            }
        }
    }

    private void RenderDiffLine(DiffEntry entry)
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
    /// Compact diff entries for display: keeps all Add/Remove lines but
    /// limits unchanged context lines to at most 2 before/after each change.
    /// </summary>
    private static List<DiffEntry> CompactDiff(List<DiffEntry> diff)
    {
        // Mark Equal entries that are within 2 positions of a change
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

    /// <summary>
    /// Computes a line-based diff between two texts.
    /// Returns ordered diff entries showing equal, removed, and added lines.
    /// </summary>
    private static List<DiffEntry> ComputeLineDiff(string oldText, string newText)
    {
        if (oldText == newText)
            return new List<DiffEntry>();

        var oldLines = string.IsNullOrEmpty(oldText)
            ? Array.Empty<string>()
            : oldText.Split('\n');
        var newLines = string.IsNullOrEmpty(newText)
            ? Array.Empty<string>()
            : newText.Split('\n');

        // Build LCS table
        int m = oldLines.Length;
        int n = newLines.Length;
        var dp = new int[m + 1, n + 1];

        for (int i = 1; i <= m; i++)
        {
            for (int j = 1; j <= n; j++)
            {
                if (oldLines[i - 1] == newLines[j - 1])
                    dp[i, j] = dp[i - 1, j - 1] + 1;
                else
                    dp[i, j] = Math.Max(dp[i - 1, j], dp[i, j - 1]);
            }
        }

        // Backtrack to build diff
        var result = new List<DiffEntry>();
        int x = m, y = n;

        while (x > 0 || y > 0)
        {
            if (x > 0 && y > 0 && oldLines[x - 1] == newLines[y - 1])
            {
                result.Add(new DiffEntry(DiffOperation.Equal, oldLines[x - 1]));
                x--;
                y--;
            }
            else if (y > 0 && (x == 0 || dp[x, y - 1] >= dp[x - 1, y]))
            {
                result.Add(new DiffEntry(DiffOperation.Add, newLines[y - 1]));
                y--;
            }
            else
            {
                result.Add(new DiffEntry(DiffOperation.Remove, oldLines[x - 1]));
                x--;
            }
        }

        result.Reverse();
        return result;
    }

    private enum DiffOperation { Equal, Add, Remove }

    private sealed record DiffEntry(DiffOperation Operation, string Line);
}
