using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.Services;
using StreamShell;

namespace OpenClawPTT.ConfigWizard;

/// <summary>Configures visual feedback indicator settings.</summary>
public sealed class VisualFeedbackConfigSection : IConfigSectionWizard
{
    public string Name => "Visual Feedback";
    public string Description => "Recording indicator appearance and position";

    private static readonly Regex HexColorPattern = new(@"^#?([0-9A-Fa-f]{6})$", RegexOptions.Compiled);

    public async Task<bool> RunAsync(IStreamShellHost host, AppConfig config, bool isInitialSetup, CancellationToken ct)
    {
        bool changed = false;

        // ── Enabled ──
        var enabled = await PromptSelectionHelper.PromptBoolAsync(host,
            "Show visual feedback indicator?",
            config.VisualFeedbackEnabled, allowCancel: false, ct);
        if (enabled != config.VisualFeedbackEnabled)
        {
            config.VisualFeedbackEnabled = enabled;
            changed = true;
        }

        if (!enabled)
            return changed;

        // ── Visual mode ──
        var visualMode = await PromptSelectionHelper.PromptEnumAsync<VisualMode>(host,
            "Visual indicator style:", config.VisualMode, allowCancel: false, ct);
        if (visualMode != config.VisualMode)
        {
            config.VisualMode = visualMode;
            changed = true;
        }

        // ── Position ──
        var positions = new (string Name, string Value)[]
        {
            ("Top Left", "TopLeft"),
            ("Top Right", "TopRight"),
            ("Bottom Left", "BottomLeft"),
            ("Bottom Right", "BottomRight"),
        };
        string position;
        if (isInitialSetup)
        {
            position = await PromptSelectionHelper.PromptStringAsync(host,
                "Indicator position:", positions, config.VisualFeedbackPosition, allowCancel: false, ct);
        }
        else
        {
            var posResult = await PromptSelectionHelper.PromptStringWithBackAsync(host,
                "Indicator position:", positions, config.VisualFeedbackPosition, ct);
            if (posResult == null)
                return changed;
            position = posResult;
        }
        if (position != config.VisualFeedbackPosition)
        {
            config.VisualFeedbackPosition = position;
            changed = true;
        }

        // ── Size ──
        var size = await PromptIntAsync(host, "Indicator size (1–200 pixels)",
            config.VisualFeedbackSize, 1, 200,
            isInitialSetup, ct);
        if (size.HasValue && size.Value != config.VisualFeedbackSize)
        {
            config.VisualFeedbackSize = size.Value;
            changed = true;
        }

        // ── Opacity ──
        var opacity = await PromptDoubleAsync(host, "Indicator opacity (0.0–1.0)",
            config.VisualFeedbackOpacity, 0.0, 1.0,
            isInitialSetup, ct);
        if (opacity.HasValue && Math.Abs(opacity.Value - config.VisualFeedbackOpacity) > 0.001)
        {
            config.VisualFeedbackOpacity = opacity.Value;
            changed = true;
        }

        // ── Color ──
        var color = await PromptTextAsync(host, "Indicator color (hex, e.g. #FF0000)",
            config.VisualFeedbackColor,
            v => HexColorPattern.IsMatch(v), "Expected hex color like #00FF00",
            isInitialSetup, ct, allowEmpty: false);
        if (color != null && color != config.VisualFeedbackColor)
        {
            config.VisualFeedbackColor = color.StartsWith("#") ? color : $"#{color}";
            changed = true;
        }

        // ── Rim thickness ──
        var rim = await PromptIntAsync(host, "Rim thickness (0 = off, 1–50)",
            config.VisualFeedbackRimThickness, 0, 50,
            isInitialSetup, ct);
        if (rim.HasValue && rim.Value != config.VisualFeedbackRimThickness)
        {
            config.VisualFeedbackRimThickness = rim.Value;
            changed = true;
        }

        return changed;
    }

    // ── Text / numeric prompt helpers ──

    private static async Task<string?> PromptTextAsync(
        IStreamShellHost host,
        string description,
        string defaultValue,
        Func<string, bool> validate,
        string? validationHint,
        bool isInitialSetup,
        CancellationToken ct,
        bool allowEmpty = false)
    {
        var tcs = new TaskCompletionSource<string?>();

        void OnInput(StreamShell.UserInputSubmittedEventArgs e)
        {
            var input = (e.TextWithoutAttachments ?? e.RawOutput).Trim();

            if (string.IsNullOrEmpty(input))
            {
                if (allowEmpty)
                {
                    tcs.TrySetResult("");
                    return;
                }
                tcs.TrySetResult(defaultValue);
                return;
            }

            if (!validate(input))
            {
                host.AddMessage($"[red]  ✗ Invalid value.{(validationHint != null ? " " + validationHint : "")}[/]");
                SendTextPrompt(host, description, defaultValue);
                return;
            }

            host.AddMessage($"[green]  ✓ {Spectre.Console.Markup.Escape(input)}[/]");
            tcs.TrySetResult(input);
        }

        host.UserInputSubmitted += OnInput;
        try
        {
            SendTextPrompt(host, description, defaultValue);
            using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
            return await tcs.Task;
        }
        finally
        {
            host.UserInputSubmitted -= OnInput;
        }
    }

    private static async Task<int?> PromptIntAsync(
        IStreamShellHost host,
        string description,
        int defaultValue,
        int min,
        int max,
        bool isInitialSetup,
        CancellationToken ct)
    {
        var result = await PromptTextAsync(host, description,
            defaultValue.ToString(),
            v => int.TryParse(v, out var n) && n >= min && n <= max,
            $"Expected a number between {min} and {max}",
            isInitialSetup, ct);
        return result != null && int.TryParse(result, out var n) ? n : null;
    }

    private static async Task<double?> PromptDoubleAsync(
        IStreamShellHost host,
        string description,
        double defaultValue,
        double min,
        double max,
        bool isInitialSetup,
        CancellationToken ct)
    {
        var result = await PromptTextAsync(host, description,
            defaultValue.ToString("F2"),
            v => double.TryParse(v, out var d) && d >= min && d <= max,
            $"Expected a number between {min} and {max}",
            isInitialSetup, ct);
        return result != null && double.TryParse(result, out var d) ? d : null;
    }

    private static void SendTextPrompt(IStreamShellHost host, string description, string defaultValue)
    {
        host.AddMessage($"[cyan2]▸ {Spectre.Console.Markup.Escape(description)}[/]");
        if (!string.IsNullOrEmpty(defaultValue))
            host.AddMessage($"  [grey](current: {Spectre.Console.Markup.Escape(defaultValue)}, press Enter to keep)[/]");
    }
}
