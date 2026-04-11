using System.Text.Json;

namespace OpenClawPTT.Services;

public sealed class SessionStatusToolRenderer : IToolRenderer
{
    private readonly IToolOutput _output;

    public SessionStatusToolRenderer(IToolOutput output)
    {
        _output = output;
    }

    public string ToolName => "session_status";

    public void Render(JsonElement args, int rightMarginIndent)
    {
        if (args.TryGetProperty("sessionKey", out var keyProp))
        {
            _output.Print("key: ", ConsoleColor.DarkGray);
            _output.Print(keyProp.GetString() ?? "", ConsoleColor.White);
        }
        if (args.TryGetProperty("model", out var modelProp))
        {
            _output.Print(", model: ", ConsoleColor.DarkGray);
            _output.Print(modelProp.GetString() ?? "", ConsoleColor.White);
        }
    }
}
