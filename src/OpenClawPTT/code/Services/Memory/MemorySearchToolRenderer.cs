using System.Text.Json;

namespace OpenClawPTT.Services;

public sealed class MemorySearchToolRenderer : IToolRenderer
{
    private readonly IToolOutput _output;

    public MemorySearchToolRenderer(IToolOutput output)
    {
        _output = output;
    }

    public string ToolName => "memory_search";

    public void Render(JsonElement args, int rightMarginIndent)
    {
        if (args.TryGetProperty("query", out var queryProp))
        {
            _output.Print(queryProp.GetString() ?? "", ConsoleColor.Gray);
        }
        if (args.TryGetProperty("maxResults", out var maxResultsProp))
        {
            _output.Print(", max results: ", ConsoleColor.DarkGray);
            _output.Print($"{maxResultsProp.GetInt32()}", ConsoleColor.White);
        }
        if (args.TryGetProperty("minScore", out var minScoreProp))
        {
            _output.Print(", min score: ", ConsoleColor.DarkGray);
            _output.Print($"{minScoreProp.GetDouble():F2}", ConsoleColor.White);
        }
    }
}
