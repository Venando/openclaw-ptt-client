using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.Services;
using OpenClawPTT.TTS;
using StreamShell;

namespace OpenClawPTT.ConfigWizard;

/// <summary>Configures Text-To-Speech settings.</summary>
public sealed class TtsConfigSection : IConfigSectionWizard
{
    public string Name => "Text-To-Speech";
    public string Description => "TTS provider and voice settings";

    public async Task<bool> RunAsync(IStreamShellHost host, AppConfig config, bool isInitialSetup, CancellationToken ct)
    {
        bool changed = false;

        // ── On initial setup: ask Yes/Skip ──
        if (isInitialSetup)
        {
            var setupTts = await PromptSelectionHelper.PromptBoolAsync(host,
                "Setup Text-To-Speech?", defaultValue: true, allowCancel: false, ct);
            if (!setupTts)
            {
                host.AddMessage("[grey]  Skipped TTS setup.[/]");
                return false;
            }
        }

        // ── Provider selection ──
        var providers = new (string Name, string Value)[]
        {
            ("OpenAI", "OpenAI"),
            ("Edge", "Edge"),
            ("Coqui", "Coqui"),
            ("Piper", "Piper"),
            ("Python", "Python"),
            ("ElevenLabs (not supported)", "ElevenLabs"),
        };

        string providerStr;
        if (isInitialSetup)
        {
            providerStr = await PromptSelectionHelper.PromptStringAsync(host,
                "Choose TTS provider:", providers, config.TtsProvider.ToString(), allowCancel: false, ct);
        }
        else
        {
            var providerResult = await PromptSelectionHelper.PromptStringWithBackAsync(host,
                "Choose TTS provider:", providers, config.TtsProvider.ToString(), ct);
            if (providerResult == null)
                return changed;
            providerStr = providerResult;
        }

        if (providerStr == "ElevenLabs")
        {
            host.AddMessage("[yellow]  ElevenLabs TTS is not yet supported.[/]");
            return changed;
        }

        if (Enum.TryParse<TtsProviderType>(providerStr, out var provider) && provider != config.TtsProvider)
        {
            config.TtsProvider = provider;
            changed = true;
        }

        // ── Provider-specific settings ──
        switch (provider)
        {
            case TtsProviderType.OpenAI:
                var openAiKey = await PromptTextAsync(host, "OpenAI API key for TTS",
                    config.TtsOpenAiApiKey ?? config.OpenAiApiKey ?? "",
                    _ => true, null, isInitialSetup, ct, isSecret: true, allowEmpty: true);
                if (openAiKey != null)
                {
                    var newKey = string.IsNullOrWhiteSpace(openAiKey) ? null : openAiKey;
                    if (newKey != config.TtsOpenAiApiKey)
                    {
                        config.TtsOpenAiApiKey = newKey;
                        changed = true;
                    }
                }
                break;

            case TtsProviderType.Edge:
                var subKey = await PromptTextAsync(host, "Azure TTS subscription key",
                    config.TtsSubscriptionKey ?? "",
                    _ => true, null, isInitialSetup, ct, isSecret: true, allowEmpty: true);
                if (subKey != null)
                {
                    var newKey = string.IsNullOrWhiteSpace(subKey) ? null : subKey;
                    if (newKey != config.TtsSubscriptionKey)
                    {
                        config.TtsSubscriptionKey = newKey;
                        changed = true;
                    }
                }
                var region = await PromptTextAsync(host, "Azure TTS region",
                    config.TtsRegion ?? "eastus",
                    _ => true, null, isInitialSetup, ct, allowEmpty: false);
                if (region != null && region != config.TtsRegion)
                {
                    config.TtsRegion = region;
                    changed = true;
                }
                break;

            case TtsProviderType.Coqui:
            case TtsProviderType.Python:
                var pythonPath = await PromptTextAsync(host, "Python path",
                    config.PythonPath ?? "python",
                    _ => true, null, isInitialSetup, ct, allowEmpty: false);
                if (pythonPath != null && pythonPath != config.PythonPath)
                {
                    config.PythonPath = pythonPath;
                    changed = true;
                }
                var coquiModel = await PromptTextAsync(host, "Coqui model name",
                    config.CoquiModelName ?? "tts_models/multilingual/mxtts/vits",
                    _ => true, null, isInitialSetup, ct, allowEmpty: false);
                if (coquiModel != null && coquiModel != config.CoquiModelName)
                {
                    config.CoquiModelName = coquiModel;
                    changed = true;
                }
                break;

            case TtsProviderType.Piper:
                var piperPath = await PromptTextAsync(host, "Piper binary path",
                    config.PiperPath ?? "piper",
                    _ => true, null, isInitialSetup, ct, allowEmpty: false);
                if (piperPath != null && piperPath != config.PiperPath)
                {
                    config.PiperPath = piperPath;
                    changed = true;
                }
                var piperModel = await PromptTextAsync(host, "Piper model path",
                    config.PiperModelPath ?? "",
                    _ => true, null, isInitialSetup, ct, allowEmpty: true);
                if (piperModel != null)
                {
                    var newPath = string.IsNullOrWhiteSpace(piperModel) ? null : piperModel;
                    if (newPath != config.PiperModelPath)
                    {
                        config.PiperModelPath = newPath;
                        changed = true;
                    }
                }
                break;
        }

        // ── Voice (optional) ──
        var voice = await PromptTextAsync(host, "Voice name (optional)",
            config.TtsVoice ?? "",
            _ => true, null, isInitialSetup, ct, allowEmpty: true);
        if (voice != null)
        {
            var newVoice = string.IsNullOrWhiteSpace(voice) ? null : voice;
            if (newVoice != config.TtsVoice)
            {
                config.TtsVoice = newVoice;
                changed = true;
            }
        }

        // ── TTS Output Mode ──
        var ttsMode = await PromptSelectionHelper.PromptEnumAsync<TtsOutputMode>(host,
            "TTS output mode:", ParseTtsOutputMode(config.TtsOutputMode), allowCancel: false, ct);
        var ttsModeStr = ttsMode.ToString().ToLowerInvariant();
        if (ttsModeStr != config.TtsOutputMode)
        {
            config.TtsOutputMode = ttsModeStr;
            changed = true;
        }

        return changed;
    }

    private static TtsOutputMode ParseTtsOutputMode(string value) => value.ToLowerInvariant() switch
    {
        "always-on" => TtsOutputMode.AlwaysOn,
        "siso" => TtsOutputMode.Siso,
        "off" => TtsOutputMode.Off,
        _ => TtsOutputMode.Siso,
    };

    private enum TtsOutputMode { AlwaysOn, Siso, Off }

    // ── Text prompt helpers (same pattern as HarnessConfigSection) ──

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
