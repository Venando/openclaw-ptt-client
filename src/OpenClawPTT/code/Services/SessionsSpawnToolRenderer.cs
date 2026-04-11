using System.Text.Json;

namespace OpenClawPTT.Services;

public sealed class SessionsSpawnToolRenderer : IToolRenderer
{
    private readonly IToolOutput _output;

    public SessionsSpawnToolRenderer(IToolOutput output)
    {
        _output = output;
    }

    public string ToolName => "sessions_spawn";

    public void Render(JsonElement args, int rightMarginIndent)
    {
        if (args.TryGetProperty("label", out var labelProp))
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write(labelProp.GetString());
        }
        if (args.TryGetProperty("runtime", out var runtimeProp))
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(", runtime: ");
            Console.ResetColor();
            Console.Write(runtimeProp.GetString());
        }
        if (args.TryGetProperty("mode", out var modeProp))
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(", mode: ");
            Console.ResetColor();
            Console.Write(modeProp.GetString());
        }
        if (args.TryGetProperty("runTimeoutSeconds", out var timeoutProp))
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(", timeout: ");
            Console.ResetColor();
            Console.Write($"{timeoutProp.GetInt32()} seconds");
        }
        if (args.TryGetProperty("task", out var taskProp))
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            const string taskPrefix = "  Task: ";
            Console.Write(taskPrefix);
            Console.ResetColor();
            _output.PrintTruncated(taskProp.GetString() ?? "", taskPrefix, rightMarginIndent, ConsoleColor.Gray);
        }
    }
}
