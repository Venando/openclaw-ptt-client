using System.Text.Json;

namespace OpenClawPTT.Services;

public sealed class SubagentsToolRenderer : IToolRenderer
{
    private readonly IToolOutput _output;

    public SubagentsToolRenderer(IToolOutput output)
    {
        _output = output;
    }

    public string ToolName => "subagents";

    public void Render(JsonElement args, int rightMarginIndent)
    {
        if (!args.TryGetProperty("action", out var actionProp)) return;
        string action = actionProp.GetString() ?? "";

        if (action == "list")
        {
            _output.Print("list", ConsoleColor.White);
            if (args.TryGetProperty("recentMinutes", out var rmProp) && rmProp.ValueKind == JsonValueKind.Number)
            {
                _output.Print($", last {rmProp.GetInt32()} minutes", ConsoleColor.DarkGray);
            }
        }
        else if (action == "kill")
        {
            _output.Print("kill", ConsoleColor.White);
            if (args.TryGetProperty("target", out var targetProp))
            {
                _output.Print(", target: ", ConsoleColor.DarkGray);
                _output.Print(targetProp.GetString() ?? "", ConsoleColor.White);
            }
        }
        else if (action == "steer")
        {
            _output.Print("steer", ConsoleColor.White);
            if (args.TryGetProperty("target", out var targetProp))
            {
                _output.Print(", target: ", ConsoleColor.DarkGray);
                _output.Print(targetProp.GetString() ?? "", ConsoleColor.White);
            }
            if (args.TryGetProperty("message", out var msgProp))
            {
                _output.Print(", message: ", ConsoleColor.DarkGray);
                _output.Print(msgProp.GetString() ?? "", ConsoleColor.White);
            }
        }
        else
        {
            _output.Print(action, ConsoleColor.White);
        }
    }
}
