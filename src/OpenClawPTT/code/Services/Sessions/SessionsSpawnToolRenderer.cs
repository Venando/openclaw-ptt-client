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
            _output.Print(labelProp.GetString() ?? "", ConsoleColor.Gray);
        }
        if (args.TryGetProperty("runtime", out var runtimeProp))
        {
            _output.Print(", runtime: ", ConsoleColor.DarkGray);
            _output.Print(runtimeProp.GetString() ?? "", ConsoleColor.White);
        }
        if (args.TryGetProperty("mode", out var modeProp))
        {
            _output.Print(", mode: ", ConsoleColor.DarkGray);
            _output.Print(modeProp.GetString() ?? "", ConsoleColor.White);
        }
        if (args.TryGetProperty("runTimeoutSeconds", out var timeoutProp))
        {
            _output.Print(", timeout: ", ConsoleColor.DarkGray);
            _output.Print($"{timeoutProp.GetInt32()} seconds", ConsoleColor.White);
        }
        if (args.TryGetProperty("task", out var taskProp))
        {
            _output.PrintLine("", ConsoleColor.DarkGray);
            const string taskPrefix = "  Task: ";
            _output.Print(taskPrefix, ConsoleColor.DarkGray);
            _output.PrintTruncated(taskProp.GetString() ?? "", taskPrefix, rightMarginIndent, ConsoleColor.Gray);
        }
    }
}
