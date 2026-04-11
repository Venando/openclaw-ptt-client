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
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write(fileProp.GetString());
        }
        if (args.TryGetProperty("old_string", out var oldProp))
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            const string oldPrefix = "  old: ";
            Console.Write(oldPrefix);
            Console.ResetColor();
            _output.PrintTruncated(oldProp.GetString() ?? "", oldPrefix, rightMarginIndent);
        }
        if (args.TryGetProperty("newString", out var newProp))
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            const string newPrefix = "  new: ";
            Console.Write(newPrefix);
            Console.ResetColor();
            _output.PrintTruncated(newProp.GetString() ?? "", newPrefix, rightMarginIndent);
        }
    }
}
