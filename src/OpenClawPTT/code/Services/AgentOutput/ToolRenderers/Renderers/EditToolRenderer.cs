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
        if (args.TryGetProperty("file_path", out var fileProp))
        {
            _output.Print(fileProp.GetString() ?? "", ConsoleColor.Gray);
        }
        if (args.TryGetProperty("old_string", out var oldProp))
        {
            _output.PrintLine("");
            const string oldPrefix = "  old: ";
            _output.Print(oldPrefix, ConsoleColor.DarkGray);
            _output.PrintTruncated(oldProp.GetString() ?? "", oldPrefix, rightMarginIndent);
        }
        if (args.TryGetProperty("newString", out var newProp))
        {
            _output.PrintLine("");
            const string newPrefix = "  new: ";
            _output.Print(newPrefix, ConsoleColor.DarkGray);
            _output.PrintTruncated(newProp.GetString() ?? "", newPrefix, rightMarginIndent);
        }
    }
}
