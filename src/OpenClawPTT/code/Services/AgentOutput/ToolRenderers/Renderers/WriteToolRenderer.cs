using System.Text.Json;

namespace OpenClawPTT.Services;

public sealed class WriteToolRenderer : IToolRenderer
{
    private readonly IToolOutput _output;

    public WriteToolRenderer(IToolOutput output)
    {
        _output = output;
    }

    public string ToolName => "write";

    public void Render(JsonElement args, int rightMarginIndent)
    {
        if (args.TryGetProperty("path", out var pathProp))
        {
            _output.PrintLine(FilePathDisplayHelper.FormatDisplayPath(pathProp.GetString() ?? ""), ConsoleColor.Gray);
        }
        if (args.TryGetProperty("content", out var contentProp))
        {
            var content = contentProp.GetString() ?? "";
            _output.PrintTruncated(content, "", rightMarginIndent, maxRows: 8);
        }
    }
}
