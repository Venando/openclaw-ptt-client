using System.Text.Json;

namespace OpenClawPTT.Services;

public sealed class MemoryGetToolRenderer : IToolRenderer
{
    private readonly IToolOutput _output;

    public MemoryGetToolRenderer(IToolOutput output)
    {
        _output = output;
    }

    public string ToolName => "memory_get";

    public void Render(JsonElement args, int rightMarginIndent)
    {
        if (args.TryGetProperty("path", out var pathProp))
        {
            _output.Print(pathProp.GetString() ?? "", ConsoleColor.Gray);
        }
        if (args.TryGetProperty("from", out var fromProp))
        {
            _output.Print(", from: ", ConsoleColor.DarkGray);
            _output.Print($"{fromProp.GetInt32()}", ConsoleColor.White);
        }
        if (args.TryGetProperty("lines", out var linesProp))
        {
            _output.Print(", lines: ", ConsoleColor.DarkGray);
            _output.Print($"{linesProp.GetInt32()}", ConsoleColor.White);
        }
    }
}
