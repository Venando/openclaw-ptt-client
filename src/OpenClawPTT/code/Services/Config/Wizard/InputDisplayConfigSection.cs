using System;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.Services;
using StreamShell;

namespace OpenClawPTT.ConfigWizard;

/// <summary>Configures input, display, and audio response settings.</summary>
public sealed class InputDisplayConfigSection : IConfigSectionWizard
{
    public string Name => "Input & Display";
    public string Description => "Hotkey, display mode, and audio response settings";

    public async Task<bool> RunAsync(IStreamShellHost host, AppConfig config, bool isInitialSetup, CancellationToken ct)
    {
        bool changed = false;

        // ── Hotkey ──
        var hotkey = await PromptTextAsync(host, "PTT hotkey (e.g. Alt+= or Ctrl+Shift+Space)",
            config.HotkeyCombination,
            v => { try { HotkeyMapping.Parse(v); return true; } catch { return false; } },
            "Expected format like Alt+= or Ctrl+Shift+Space",
            isInitialSetup, ct, allowEmpty: false);
        if (hotkey != null && hotkey != config.HotkeyCombination)
        {
            config.HotkeyCombination = hotkey;
            changed = true;
        }

        // ── Hold to talk ──
        var holdToTalk = await PromptSelectionHelper.PromptBoolAsync(host,
            "Hold-to-talk mode? (Hold = hold down, Release = send)",
            config.HoldToTalk, allowCancel: false, ct);
        if (holdToTalk != config.HoldToTalk)
        {
            config.HoldToTalk = holdToTalk;
            changed = true;
        }

        // ── Real-time reply ──
        var realTime = await PromptSelectionHelper.PromptBoolAsync(host,
            "Show real-time reply streaming?",
            config.RealTimeReplyOutput, allowCancel: false, ct);
        if (realTime != config.RealTimeReplyOutput)
        {
            config.RealTimeReplyOutput = realTime;
            changed = true;
        }

        // ── Reply display mode ──
        var replyMode = await PromptSelectionHelper.PromptEnumAsync<ReplyDisplayMode>(host,
            "Reply display mode:", config.ReplyDisplayMode, allowCancel: false, ct);
        if (replyMode != config.ReplyDisplayMode)
        {
            config.ReplyDisplayMode = replyMode;
            changed = true;
        }

        // ── Audio response mode ──
        var audioModes = new (string Name, string Value)[]
        {
            ("Text only", "text-only"),
            ("Audio only", "audio-only"),
            ("Both text and audio", "both"),
        };
        string audioMode;
        if (isInitialSetup)
        {
            audioMode = await PromptSelectionHelper.PromptStringAsync(host,
                "Audio response mode:", audioModes, config.AudioResponseMode, allowCancel: false, ct);
        }
        else
        {
            var audioResult = await PromptSelectionHelper.PromptStringWithBackAsync(host,
                "Audio response mode:", audioModes, config.AudioResponseMode, ct);
            if (audioResult == null)
                return changed;
            audioMode = audioResult;
        }
        if (audioMode != config.AudioResponseMode)
        {
            config.AudioResponseMode = audioMode;
            changed = true;
        }

        // ── Agent name ──
        var agentName = await PromptTextAsync(host, "Your name / agent display prefix (-- to clear)",
            config.AgentName,
            v => !string.IsNullOrWhiteSpace(v), "Cannot be empty",
            isInitialSetup, ct, allowEmpty: false);
        if (agentName != null && agentName != config.AgentName)
        {
            config.AgentName = agentName;
            changed = true;
        }

        // ── Transcription prefix ──
        var prefix = await PromptTextAsync(host, "Transcription context prefix (-- to clear)",
            config.TranscriptionPromptPrefix,
            _ => true, null,
            isInitialSetup, ct, allowEmpty: true);
        if (prefix != null && prefix != config.TranscriptionPromptPrefix)
        {
            config.TranscriptionPromptPrefix = prefix;
            changed = true;
        }

        // ── Require confirm before send ──
        var requireConfirm = await PromptSelectionHelper.PromptBoolAsync(host,
            "Require confirmation before sending messages?",
            config.RequireConfirmBeforeSend, allowCancel: false, ct);
        if (requireConfirm != config.RequireConfirmBeforeSend)
        {
            config.RequireConfirmBeforeSend = requireConfirm;
            changed = true;
        }

        return changed;
    }

    // ── Text prompt helpers ──

    private static async Task<string?> PromptTextAsync(
        IStreamShellHost host,
        string description,
        string defaultValue,
        Func<string, bool> validate,
        string? validationHint,
        bool isInitialSetup,
        CancellationToken ct,
        bool isSecret = false,
        bool allowEmpty = false)
    {
        var tcs = new TaskCompletionSource<string?>();

        void OnInput(StreamShell.UserInputSubmittedEventArgs e)
        {
            var input = (e.TextWithoutAttachments ?? e.RawOutput).Trim();

            if (input == "--")
            {
                tcs.TrySetResult("");
                return;
            }

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
                SendTextPrompt(host, description, defaultValue, isSecret);
                return;
            }

            var displayValue = isSecret ? MaskSecret(input) : input;
            host.AddMessage($"[green]  ✓ {Spectre.Console.Markup.Escape(displayValue)}[/]");
            tcs.TrySetResult(input);
        }

        host.UserInputSubmitted += OnInput;
        try
        {
            SendTextPrompt(host, description, defaultValue, isSecret);
            using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
            return await tcs.Task;
        }
        finally
        {
            host.UserInputSubmitted -= OnInput;
        }
    }

    private static void SendTextPrompt(IStreamShellHost host, string description, string defaultValue, bool isSecret)
    {
        host.AddMessage($"[cyan2]▸ {Spectre.Console.Markup.Escape(description)}[/]");
        var displayDefault = isSecret ? MaskSecret(defaultValue) : defaultValue;
        if (!string.IsNullOrEmpty(displayDefault))
            host.AddMessage($"  [grey](current: {Spectre.Console.Markup.Escape(displayDefault)}, press Enter to keep)[/]");
    }

    private static string MaskSecret(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "(not set)";
        if (value.Length <= 4)
            return new string('*', value.Length);
        return value[..4] + new string('*', Math.Min(value.Length - 4, 12));
    }
}
