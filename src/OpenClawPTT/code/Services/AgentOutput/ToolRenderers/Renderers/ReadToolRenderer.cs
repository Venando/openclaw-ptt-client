using System.Text.Json;

namespace OpenClawPTT.Services;

public sealed class ReadToolRenderer : IToolRenderer
{
    private readonly IToolOutput _output;

    public ReadToolRenderer(IToolOutput output)
    {
        _output = output;
    }

    public string ToolName => "read";

    public void Render(JsonElement args, int rightMarginIndent)
    {
        if (args.TryGetProperty("file", out var fileProp) || args.TryGetProperty("path", out fileProp))
        {
            _output.Print(fileProp.GetString() ?? "", ConsoleColor.Gray);
        }
        if (args.TryGetProperty("offset", out var offsetProp) &&
            args.TryGetProperty("limit", out var limitProp))
        {
            int offset = offsetProp.GetInt32();
            int limit = limitProp.GetInt32();
            _output.Print($" (lines {offset}-{offset + limit - 1})", ConsoleColor.DarkGray);
        }
        else if (args.TryGetProperty("limit", out var limitProp2))
        {
            _output.Print($" (lines 1-{limitProp2.GetInt32()})", ConsoleColor.DarkGray);
        }
    }
}
