using System.Text.Json;

namespace OpenClawPTT.Services;

public sealed class MemoryGetToolRenderer : IToolRenderer
{
    public string ToolName => "memory_get";

    public void Render(JsonElement args, int rightMarginIndent)
    {
        if (args.TryGetProperty("path", out var pathProp))
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write(pathProp.GetString());
        }
        if (args.TryGetProperty("from", out var fromProp))
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($", from: ");
            Console.ResetColor();
            Console.Write($"{fromProp.GetInt32()}");
        }
        if (args.TryGetProperty("lines", out var linesProp))
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($", lines: ");
            Console.ResetColor();
            Console.Write($"{linesProp.GetInt32()}");
        }
    }
}
