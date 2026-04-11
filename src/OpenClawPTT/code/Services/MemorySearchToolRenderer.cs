using System.Text.Json;

namespace OpenClawPTT.Services;

public sealed class MemorySearchToolRenderer : IToolRenderer
{
    public string ToolName => "memory_search";

    public void Render(JsonElement args, int rightMarginIndent)
    {
        if (args.TryGetProperty("query", out var queryProp))
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write(queryProp.GetString());
        }
        if (args.TryGetProperty("maxResults", out var maxResultsProp))
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($", max results: ");
            Console.ResetColor();
            Console.Write($"{maxResultsProp.GetInt32()}");
        }
        if (args.TryGetProperty("minScore", out var minScoreProp))
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($", min score: ");
            Console.ResetColor();
            Console.Write($"{minScoreProp.GetDouble():F2}");
        }
    }
}
