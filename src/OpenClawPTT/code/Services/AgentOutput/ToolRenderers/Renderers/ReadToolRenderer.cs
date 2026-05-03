using System.Text.Json;
using System.IO;

namespace OpenClawPTT.Services;

public sealed class ReadToolRenderer : IToolRenderer
{
    private readonly IToolOutput _output;

    public ReadToolRenderer(IToolOutput output)
    {
        _output = output;
    }

    public string ToolName => "read";

    public void Render(JsonElement args, int rightMarginIndent)
    {
        const int maxLength = 50;
        const int ellipsisPrefixLength = 5; // ".." separator
        
        if (args.TryGetProperty("file", out var fileProp) || args.TryGetProperty("path", out fileProp))
        {
            string filePath = fileProp.GetString() ?? "";
            string displayPath;
            if (filePath.Length > maxLength)
            {
                string fileName = Path.GetFileName(filePath);
                string folder = Path.GetDirectoryName(filePath) ?? "";
                int availableFolderLength = maxLength - fileName.Length - ellipsisPrefixLength;
                string shortFolder = folder.Length > availableFolderLength
                    ? string.Concat("..", folder.AsSpan(folder.Length - availableFolderLength))
                    : folder;
                displayPath = shortFolder + Path.DirectorySeparatorChar + fileName;
            }
            else
            {
                displayPath = filePath;
            }
            _output.Print(displayPath, ConsoleColor.Gray);
        }
        if (args.TryGetProperty("offset", out var offsetProp) &&
            args.TryGetProperty("limit", out var limitProp))
        {
            int offset = offsetProp.GetInt32();
            int limit = limitProp.GetInt32();
            _output.Print($" (lines {offset}-{offset + limit - 1})", ConsoleColor.DarkGray);
        }
        else if (args.TryGetProperty("limit", out var limitProp2))
        {
            _output.Print($" (lines 1-{limitProp2.GetInt32()})", ConsoleColor.DarkGray);
        }
    }
}
