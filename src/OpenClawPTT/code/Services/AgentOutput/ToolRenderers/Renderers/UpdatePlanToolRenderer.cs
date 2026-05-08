using System.Text.Json;

namespace OpenClawPTT.Services;

/// <summary>
/// Renders the update_plan tool — displays plan steps with status indicators
/// using Spectre.Console markup for a clear, visually appealing layout.
/// </summary>
public sealed class UpdatePlanToolRenderer : ToolRendererBase
{
    private const int StatusWidth = 14;

    public UpdatePlanToolRenderer(IToolOutput output) : base(output)
    {
    }

    public override string ToolName => "update_plan";

    public override void Render(JsonElement args, int rightMarginIndent)
    {
        // Optional explanation
        if (args.TryGetProperty("explanation", out var explanationProp))
        {
            var explanation = explanationProp.GetString();
            if (!string.IsNullOrWhiteSpace(explanation))
            {
                Output.PrintMarkup($"  [gray] {MarkupEscape(explanation)}[/]");
                Output.PrintLine("", ConsoleColor.DarkGray);
            }
        }

        // Plan steps
        if (!args.TryGetProperty("plan", out var planProp) || planProp.ValueKind != JsonValueKind.Array)
        {
            Output.PrintLine("  (no plan steps)", ConsoleColor.DarkGray);
            return;
        }

        var stepIndex = 1;
        foreach (var step in planProp.EnumerateArray())
        {
            if (!step.TryGetProperty("step", out var stepTextProp))
                continue;

            var stepText = stepTextProp.GetString() ?? "";
            var status = step.TryGetProperty("status", out var statusProp)
                ? statusProp.GetString() ?? "pending"
                : "pending";

            RenderStep(stepIndex, stepText, status);
            stepIndex++;
        }
    }

    private void RenderStep(int index, string stepText, string status)
    {
        var (icon, label, color) = GetStatusDisplay(status);
        var statusTag = FormatStatusTag(icon, label, color);

        Output.PrintLine("", ConsoleColor.DarkGray);

        // Step number + status tag
        Output.PrintMarkup($"  [gray]#{index,2}[/] ");
        Output.PrintMarkup(statusTag);

        // Step description — fill remaining visible width using the status block width
        Output.PrintMarkup($"  [white]{MarkupEscape(stepText)}[/]");
    }

    private static (string icon, string label, string color) GetStatusDisplay(string status)
    {
        return status.ToLowerInvariant() switch
        {
            "completed"    => ("✅", "Completed",   "green"),
            "in_progress"  => ("➡️", "In Progress", "gold3"),
            "pending"      => ("⭕", "Pending",     "grey"),
            "skipped"      => ("◇", "Skipped",     "grey42"),
            _              => ("·", status,        "grey"),
        };
    }

    private static string FormatStatusTag(string icon, string label, string color)
    {
        // Pad the label so all status tags are the same width (icon + space + zero-width space + label + right-pad)
        var paddedLabel = label.PadRight(StatusWidth);
        return $"[bold {color}]{icon} {paddedLabel}[/]";
    }

    private static string MarkupEscape(string text)
    {
        return text.Replace("[", "[[").Replace("]", "]]");
    }
}
