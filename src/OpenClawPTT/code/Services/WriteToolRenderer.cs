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
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write(pathProp.GetString());
        }
        if (args.TryGetProperty("content", out var contentProp))
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            const string contentPrefix = "Content:  ";
            Console.Write(contentPrefix);
            Console.ResetColor();
            var content = contentProp.GetString() ?? "";
            _output.PrintTruncated(content, contentPrefix, rightMarginIndent);
        }
    }
}
