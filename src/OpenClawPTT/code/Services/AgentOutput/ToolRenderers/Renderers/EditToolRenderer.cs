using System.Text.Json;
using Spectre.Console;

namespace OpenClawPTT.Services;

public sealed class EditToolRenderer : IToolRenderer
{
    private readonly IToolOutput _output;
    private readonly DiffRenderer _diffRenderer;

    public EditToolRenderer(IToolOutput output)
    {
        _output = output;
        _diffRenderer = new DiffRenderer(output);
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
                var diffResult = LineDiffEngine.ComputeDiff(oldText, newText);

                if (diffResult.Entries.Count == 0)
                {
                    // No differences — just show the content once
                    const string prefix = "  ";
                    _diffRenderer.RenderPlainText(oldText, prefix, rightMarginIndent, maxRows: 8);
                }
                else
                {
                    // Compact: prioritize changed lines, keep only 1-2 context lines
                    var displayLines = CompactDiff(diffResult.Entries);

                    int totalRemoved = diffResult.Removals;
                    int totalAdded = diffResult.Additions;

                    // Enforce max 8 displayed lines
                    bool hasMore = displayLines.Count > 8;
                    var shown = hasMore ? displayLines.Take(8).ToList() : displayLines;

                    foreach (var entry in shown)
                        _diffRenderer.RenderDiffLine(entry);

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
}
