using System.Text.Json;

namespace OpenClawPTT.Services;

public sealed class ReadToolRenderer : IToolRenderer
{
    public string ToolName => "read";

    public void Render(JsonElement args, int rightMarginIndent)
    {
        if (args.TryGetProperty("file", out var fileProp))
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write(fileProp.GetString());
        }
        if (args.TryGetProperty("offset", out var offsetProp) &&
            args.TryGetProperty("limit", out var limitProp))
        {
            int offset = offsetProp.GetInt32();
            int limit = limitProp.GetInt32();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($" (lines {offset}-{offset + limit - 1})");
        }
        else if (args.TryGetProperty("limit", out var limitProp2))
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($" (lines 1-{limitProp2.GetInt32()})");
        }
    }
}
