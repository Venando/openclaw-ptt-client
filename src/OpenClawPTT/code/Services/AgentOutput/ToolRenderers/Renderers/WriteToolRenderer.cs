using System.Text.Json;

namespace OpenClawPTT.Services;

public sealed class WriteToolRenderer : ToolRendererBase
{
    public WriteToolRenderer(IToolOutput output) : base(output)
    {
    }

    public override string ToolName => "write";

    public override void Render(JsonElement args, int rightMarginIndent)
    {
        if (args.TryGetProperty("path", out var pathProp))
        {
            Output.PrintLine(FilePathDisplayHelper.FormatDisplayPath(pathProp.GetString() ?? ""), ConsoleColor.Gray);
        }
        if (args.TryGetProperty("content", out var contentProp))
        {
            var content = contentProp.GetString() ?? "";
            Output.PrintTruncated(content, "", rightMarginIndent, maxRows: 8);
        }
    }
}
