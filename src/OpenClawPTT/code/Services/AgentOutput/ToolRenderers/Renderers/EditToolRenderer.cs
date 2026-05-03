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
                    // No differences, just show the content once
                    const string prefix = "  ";
                    _output.Print(prefix, ConsoleColor.DarkGray);
                    _output.PrintTruncated(oldText, prefix, rightMarginIndent, maxRows: 8);
                }
                else
                {
                    // Count removed/added lines to decide truncation
                    int totalDiffLines = diffResult.Count;
                    int removedLines = diffResult.Count(d => d.Operation == DiffOperation.Remove);
                    int addedLines = diffResult.Count(d => d.Operation == DiffOperation.Add);

                    // Show up to 8 lines of diff, then a summary
                    var displayLines = diffResult.Take(8).ToList();
                    bool hasMore = diffResult.Count > 8;

                    foreach (var entry in displayLines)
                    {
                        RenderDiffLine(entry);
                    }

                    if (hasMore)
                    {
                        int remainingRemoved = removedLines - displayLines.Count(d => d.Operation == DiffOperation.Remove);
                        int remainingAdded = addedLines - displayLines.Count(d => d.Operation == DiffOperation.Add);
                        _output.PrintMarkup($"  [default on yellow]... {diffResult.Count - 8} more changes (-{remainingRemoved} +{remainingAdded})[/]\n");
                    }

                    // If old is long, show truncated summary with more rows (8)
                    int oldLineCount = string.IsNullOrEmpty(oldText) ? 0 : oldText.Split('\n').Length;
                    int newLineCount = string.IsNullOrEmpty(newText) ? 0 : newText.Split('\n').Length;
                    if (oldLineCount > 8 || newLineCount > 8)
                    {
                        const string summaryPrefix = "  ";
                        _output.Print(summaryPrefix, ConsoleColor.DarkGray);
                        _output.PrintTruncated($"old: {oldLineCount} lines, new: {newLineCount} lines", summaryPrefix, rightMarginIndent, ConsoleColor.DarkGray, maxRows: 8);
                    }
                }
            }
        }
    }

    private void RenderDiffLine(DiffEntry entry)
    {
        string markup;
        switch (entry.Operation)
        {
            case DiffOperation.Add:
                markup = $"[default on green]+ {Markup.Escape(entry.Line)}[/]\n";
                break;
            case DiffOperation.Remove:
                markup = $"[default on red]- {Markup.Escape(entry.Line)}[/]\n";
                break;
            case DiffOperation.Equal:
            default:
                markup = $"  {Markup.Escape(entry.Line)}\n";
                break;
        }
        _output.PrintMarkup(markup);
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
