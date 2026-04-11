using System.Text.Json;

namespace OpenClawPTT.Services;

/// <summary>
/// Renders tools that use simple key-value pair display (process, web_search, image_generate).
/// </summary>
public sealed class GenericKvpToolRenderer : IToolRenderer
{
    public string ToolName => "generic_kvp";

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
