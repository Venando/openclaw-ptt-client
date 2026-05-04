using System.Text.Json;

namespace OpenClawPTT.Services;

public sealed class SubagentsToolRenderer : ToolRendererBase
{
    public SubagentsToolRenderer(IToolOutput output) : base(output)
    {
    }

    public override string ToolName => "subagents";

    public override void Render(JsonElement args, int rightMarginIndent)
    {
        if (!args.TryGetProperty("action", out var actionProp)) return;
        string action = actionProp.GetString() ?? "";

        if (action == "list")
        {
            PrintValue("list", ConsoleColor.White);
            if (args.TryGetProperty("recentMinutes", out var rmProp) && rmProp.ValueKind == JsonValueKind.Number)
            {
                Output.Print($", last {rmProp.GetInt32()} minutes", ConsoleColor.DarkGray);
            }
        }
        else if (action == "kill")
        {
            PrintValue("kill", ConsoleColor.White);
            PrintPropertyIfExists(args, "target", "target: ", prependComma: true);
        }
        else if (action == "steer")
        {
            PrintValue("steer", ConsoleColor.White);
            bool hasPrinted = PrintPropertyIfExists(args, "target", "target: ", prependComma: true);
            PrintPropertyIfExists(args, "message", "message: ", prependComma: hasPrinted);
        }
        else
        {
            PrintValue(action, ConsoleColor.White);
        }
    }
}
