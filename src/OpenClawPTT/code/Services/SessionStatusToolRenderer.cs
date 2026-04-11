using System.Text.Json;

namespace OpenClawPTT.Services;

public sealed class SessionStatusToolRenderer : IToolRenderer
{
    public string ToolName => "session_status";

    public void Render(JsonElement args, int rightMarginIndent)
    {
        if (args.TryGetProperty("sessionKey", out var keyProp))
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"key: ");
            Console.ResetColor();
            Console.Write(keyProp.GetString());
        }
        if (args.TryGetProperty("model", out var modelProp))
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($", model: ");
            Console.ResetColor();
            Console.Write(modelProp.GetString());
        }
    }
}
