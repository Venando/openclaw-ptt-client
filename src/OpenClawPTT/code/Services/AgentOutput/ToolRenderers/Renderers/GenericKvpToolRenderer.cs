using System.Text.Json;

namespace OpenClawPTT.Services;

/// <summary>
/// Renders tools that use simple key-value pair display (process, web_search, image_generate).
/// </summary>
public sealed class GenericKvpToolRenderer : IToolRenderer
{
    private readonly IToolOutput _output;

    public GenericKvpToolRenderer(IToolOutput output)
    {
        _output = output;
    }

    public string ToolName => "generic_kvp";

    public void Render(JsonElement args, int rightMarginIndent)
    {
        bool first = true;
        foreach (var prop in args.EnumerateObject())
        {
            if (first)
            {
                _output.Print(GetValueString(prop.Value), ConsoleColor.Gray);
                first = false;
            }
            else
            {
                _output.Print($", {prop.Name}: ", ConsoleColor.DarkGray);
                _output.Print(GetValueString(prop.Value), ConsoleColor.White);
            }
        }
    }

    /// <summary>
    /// Converts a JsonElement to its string representation, handling all value types.
    /// </summary>
    private static string GetValueString(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "null",
            JsonValueKind.Array => element.GetRawText(),
            JsonValueKind.Object => element.GetRawText(),
            _ => element.GetRawText()
        };
    }
}
