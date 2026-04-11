using System.Text.Json;

namespace OpenClawPTT.Services;

public sealed class SessionsListToolRenderer : IToolRenderer
{
    public string ToolName => "sessions_list";

    public void Render(JsonElement args, int rightMarginIndent)
    {
        if (args.TryGetProperty("limit", out var limitProp))
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"limit: ");
            Console.ResetColor();
            Console.Write($"{limitProp.GetInt32()}");
        }
        if (args.TryGetProperty("kinds", out var kindsProp))
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($", kinds: ");
            Console.ResetColor();
            Console.Write(kindsProp.GetString());
        }
        if (args.TryGetProperty("messageLimit", out var msgLimitProp))
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($", messages: ");
            Console.ResetColor();
            Console.Write($"{msgLimitProp.GetInt32()}");
        }
        if (args.TryGetProperty("activeMinutes", out var activeMinProp))
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($", in last ");
            Console.ResetColor();
            Console.Write($"{activeMinProp.GetInt32()} minutes");
        }
    }
}
