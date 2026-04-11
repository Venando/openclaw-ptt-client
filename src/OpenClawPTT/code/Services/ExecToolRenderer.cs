using System.Text.Json;

namespace OpenClawPTT.Services;

public sealed class ExecToolRenderer : IToolRenderer
{
    public string ToolName => "exec";

    public void Render(JsonElement args, int rightMarginIndent)
    {
        if (args.TryGetProperty("command", out var cmdProp))
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write(cmdProp.GetString());
        }
    }
}
