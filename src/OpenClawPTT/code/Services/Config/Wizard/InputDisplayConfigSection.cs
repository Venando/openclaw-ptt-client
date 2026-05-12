using System;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.Services;
using StreamShell;

namespace OpenClawPTT.ConfigWizard;

/// <summary>Configures input, display, and audio response settings.</summary>
public sealed class InputDisplayConfigSection : ConfigSectionBase
{
    public override string Name => "Input & Display";
    public override string Description => "Hotkey, display mode, and audio response settings";

    private static readonly (string Name, string Value)[] AudioModeOptions =
    {
        ("Text only", "text-only"),
        ("Audio only", "audio-only"),
        ("Both text and audio", "both"),
    };

    public InputDisplayConfigSection()
    {
        _configItems.AddRange(new[]
        {
            ConfigSetupItem.ForString(
                title: "PTT hotkey (e.g. Alt+= or Ctrl+Shift+Space)",
                fieldName: nameof(AppConfig.HotkeyCombination),
                validator: v =>
                {
                    try { HotkeyMapping.Parse(v); return true; }
                    catch { return false; }
                },
                validationHint: "Expected format like Alt+= or Ctrl+Shift+Space"),

            ConfigSetupItem.ForBool(
                title: "Hold-to-talk mode? (Hold = hold down, Release = send)",
                fieldName: nameof(AppConfig.HoldToTalk)),

            ConfigSetupItem.ForBool(
                title: "Show real-time reply streaming?",
                fieldName: nameof(AppConfig.RealTimeReplyOutput)),

            ConfigSetupItem.ForEnum<ReplyDisplayMode>(
                title: "Reply display mode",
                fieldName: nameof(AppConfig.ReplyDisplayMode)),

            ConfigSetupItem.ForString(
                title: "Your name / agent display prefix",
                fieldName: nameof(AppConfig.AgentName),
                validator: v => !string.IsNullOrWhiteSpace(v),
                validationHint: "Cannot be empty",
                allowClear: true),

            ConfigSetupItem.ForString(
                title: "Transcription context prefix",
                fieldName: nameof(AppConfig.TranscriptionPromptPrefix),
                isEmptyToDefault: true,
                allowClear: true),

            ConfigSetupItem.ForBool(
                title: "Require confirmation before sending messages?",
                fieldName: nameof(AppConfig.RequireConfirmBeforeSend)),
        });
    }

    public override async Task<ConfigSectionResult> RunAsync(
        IStreamShellHost host, AppConfig config, bool isInitialSetup, CancellationToken ct)
    {
        var result = new ConfigSectionResult();
        bool changed = false;

        // ── Universal config items ──
        if (await RunConfigItemsAsync(host, config, isInitialSetup, ct, result))
            changed = true;

        // ── Audio response mode (inline for Back button support) ──
        string audioMode;
        if (isInitialSetup)
        {
            audioMode = await PromptSelectionHelper.PromptStringAsync(host,
                "Audio response mode:", AudioModeOptions, config.AudioResponseMode, allowCancel: false, cancellationToken: ct);
        }
        else
        {
            var audioResult = await PromptSelectionHelper.PromptStringWithBackAsync(host,
                "Audio response mode:", AudioModeOptions, config.AudioResponseMode, cancellationToken: ct);
            if (audioResult == null)
            {
                result.IsChanged = changed;
                return result;
            }
            audioMode = audioResult;
        }
        if (audioMode != config.AudioResponseMode)
        {
            config.AudioResponseMode = audioMode;
            changed = true;
            result.Settings.Add(new ConfigSectionResult.SettingRecord("Audio response mode", audioMode));
        }

        result.IsChanged = changed;
        return result;
    }
}
