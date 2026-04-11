using System.Text.Json;

namespace OpenClawPTT.Services;

public sealed class SessionsListToolRenderer : IToolRenderer
{
    private readonly IToolOutput _output;

    public SessionsListToolRenderer(IToolOutput output)
    {
        _output = output;
    }

    public string ToolName => "sessions_list";

    public void Render(JsonElement args, int rightMarginIndent)
    {
        if (args.TryGetProperty("limit", out var limitProp))
        {
            _output.Print("limit: ", ConsoleColor.DarkGray);
            _output.Print($"{limitProp.GetInt32()}", ConsoleColor.White);
        }
        if (args.TryGetProperty("kinds", out var kindsProp))
        {
            _output.Print(", kinds: ", ConsoleColor.DarkGray);
            _output.Print(kindsProp.GetString() ?? "", ConsoleColor.White);
        }
        if (args.TryGetProperty("messageLimit", out var msgLimitProp))
        {
            _output.Print(", messages: ", ConsoleColor.DarkGray);
            _output.Print($"{msgLimitProp.GetInt32()}", ConsoleColor.White);
        }
        if (args.TryGetProperty("activeMinutes", out var activeMinProp))
        {
            _output.Print(", in last ", ConsoleColor.DarkGray);
            _output.Print($"{activeMinProp.GetInt32()} minutes", ConsoleColor.White);
        }
    }
}
