using System.Text.Json;

namespace OpenClawPTT.Services;

public sealed class SessionsSpawnToolRenderer : ToolRendererBase
{
    public SessionsSpawnToolRenderer(IToolOutput output) : base(output)
    {
    }

    public override string ToolName => "sessions_spawn";

    public override void Render(JsonElement args, int rightMarginIndent)
    {
        if (args.TryGetProperty("label", out var labelProp))
        {
            PrintValue(labelProp.GetString() ?? "", Style.Label);
        }

        Output.PrintLine("", Style.Muted);

        if (args.TryGetProperty("task", out var taskProp))
        {
            string taskPrefix = "  ";
            Output.Print(taskPrefix, Style.Muted);
            Output.PrintTruncated(taskProp.GetString() ?? "", taskPrefix, rightMarginIndent, Style.Label, maxRows: 15);
        }
    }
}
