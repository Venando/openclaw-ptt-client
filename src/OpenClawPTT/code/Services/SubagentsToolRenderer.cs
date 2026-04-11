using System.Text.Json;

namespace OpenClawPTT.Services;

public sealed class SubagentsToolRenderer : IToolRenderer
{
    public string ToolName => "subagents";

    public void Render(JsonElement args, int rightMarginIndent)
    {
        if (!args.TryGetProperty("action", out var actionProp)) return;
        string action = actionProp.GetString() ?? "";

        if (action == "list")
        {
            Console.Write("list");
            if (args.TryGetProperty("recentMinutes", out var rmProp) && rmProp.ValueKind == JsonValueKind.Number)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($", last {rmProp.GetInt32()} minutes");
            }
        }
        else if (action == "kill")
        {
            Console.Write("kill");
            if (args.TryGetProperty("target", out var targetProp))
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(", target: ");
                Console.ResetColor();
                Console.Write(targetProp.GetString());
            }
        }
        else if (action == "steer")
        {
            Console.Write("steer");
            if (args.TryGetProperty("target", out var targetProp))
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(", target: ");
                Console.ResetColor();
                Console.Write(targetProp.GetString());
            }
            if (args.TryGetProperty("message", out var msgProp))
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(", message: ");
                Console.ResetColor();
                Console.Write(msgProp.GetString());
            }
        }
        else
        {
            Console.Write(action);
        }
    }
}
