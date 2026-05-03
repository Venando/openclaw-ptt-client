using System.Text.Json;

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
                if (edit.TryGetProperty("oldText", out var oldProp))
                {
                    _output.PrintLine("");
                    const string oldPrefix = "  old: ";
                    _output.Print(oldPrefix, ConsoleColor.DarkGray);
                    _output.PrintTruncated(oldProp.GetString() ?? "", oldPrefix, rightMarginIndent);
                }
                if (edit.TryGetProperty("newText", out var newProp))
                {
                    _output.PrintLine("");
                    const string newPrefix = "  new: ";
                    _output.Print(newPrefix, ConsoleColor.DarkGray);
                    _output.PrintTruncated(newProp.GetString() ?? "", newPrefix, rightMarginIndent);
                }
            }
        }
    }
}
