using System.Text.Json;

namespace OpenClawPTT.Services;

public sealed class WebFetchToolRenderer : IToolRenderer
{
    public string ToolName => "web_fetch";

    public void Render(JsonElement args, int rightMarginIndent)
    {
        if (args.TryGetProperty("url", out var urlProp))
        {
            var url = urlProp.GetString() ?? "";
            url = url.Replace("https://", "").Replace("http://", "").Replace("www.", "");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write(url);
        }
        if (args.TryGetProperty("maxChars", out var maxCharsProp))
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($" (max {maxCharsProp.GetInt32()} chars)");
        }
    }
}
