using System.Text.Json;

namespace OpenClawPTT.Services;

/// <summary>
/// Default renderer for unknown tools that were not registered with a specific renderer.
/// Falls back to printing the raw arguments. NOTE: this renderer returns an empty
/// ToolName so it is filtered out of the default registry.
/// </summary>
public sealed class DefaultToolRenderer : IToolRenderer
{
    private readonly IToolOutput _output;

    public DefaultToolRenderer(IToolOutput output)
    {
        _output = output;
    }

    public string ToolName => "";

    public void Render(JsonElement args, int rightMarginIndent)
    {
        bool first = true;
        foreach (var prop in args.EnumerateObject())
        {
            if (first)
            {
                _output.Print(prop.Value.GetString() ?? "", ConsoleColor.Gray);
                first = false;
            }
            else
            {
                _output.Print($", {prop.Name}: ", ConsoleColor.DarkGray);
                _output.Print(prop.Value.GetString() ?? "", ConsoleColor.White);
            }
        }
    }
}
