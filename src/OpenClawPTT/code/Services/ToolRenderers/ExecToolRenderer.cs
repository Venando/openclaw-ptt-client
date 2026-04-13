using System.Text.Json;

namespace OpenClawPTT.Services;

public sealed class ExecToolRenderer : IToolRenderer
{
    private readonly IToolOutput _output;

    public ExecToolRenderer(IToolOutput output)
    {
        _output = output;
    }

    public string ToolName => "exec";

    public void Render(JsonElement args, int rightMarginIndent)
    {
        if (args.TryGetProperty("command", out var cmdProp))
        {
            _output.Print(cmdProp.GetString() ?? "", ConsoleColor.Gray);
        }
    }
}
