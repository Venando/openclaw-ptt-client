using System.Text.Json;

namespace OpenClawPTT.Services;

public sealed class WebFetchToolRenderer : IToolRenderer
{
    private readonly IToolOutput _output;

    public WebFetchToolRenderer(IToolOutput output)
    {
        _output = output;
    }

    public string ToolName => "web_fetch";

    public void Render(JsonElement args, int rightMarginIndent)
    {
        if (args.TryGetProperty("url", out var urlProp))
        {
            var url = urlProp.GetString() ?? "";
            url = url.Replace("https://", "").Replace("http://", "").Replace("www.", "");
            _output.Print(url, ConsoleColor.Gray);
        }
        if (args.TryGetProperty("maxChars", out var maxCharsProp))
        {
            _output.Print($" (max {maxCharsProp.GetInt32()} chars)", ConsoleColor.DarkGray);
        }
    }
}
