using System.Text.Json;

namespace OpenClawPTT.Services;

/// <summary>
/// Default renderer for unknown tools that were not registered with a specific renderer.
/// Falls back to printing the raw arguments.
/// </summary>
public sealed class DefaultToolRenderer : IToolRenderer
{
    public string ToolName => "";

    public void Render(JsonElement args, int rightMarginIndent)
    {
        bool first = true;
        foreach (var prop in args.EnumerateObject())
        {
            if (first)
            {
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.Write(prop.Value.GetString());
                first = false;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($", {prop.Name}: ");
                Console.ResetColor();
                Console.Write(prop.Value.GetString());
            }
        }
    }
}
