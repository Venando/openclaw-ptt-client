using System;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.Services;
using StreamShell;

namespace OpenClawPTT.ConfigWizard;

/// <summary>Configures Speech-To-Text settings.</summary>
public sealed class SttConfigSection : IConfigSectionWizard
{
    public string Name => "Speech-To-Text";
    public string Description => "STT provider and transcription settings";

    public async Task<bool> RunAsync(IStreamShellHost host, AppConfig config, bool isInitialSetup, CancellationToken ct)
    {
        bool changed = false;

        // ── On initial setup: ask Yes/Skip ──
        if (isInitialSetup)
        {
            var setupStt = await PromptSelectionHelper.PromptBoolAsync(host,
                "Setup Speech-To-Text?", defaultValue: true, allowCancel: false, ct);
            if (!setupStt)
            {
                host.AddMessage("[grey]  Skipped STT setup.[/]");
                return false;
            }
        }

        // ── Provider selection ──
        var providers = new (string Name, string Value)[]
        {
            ("Groq", "groq"),
            ("OpenAI", "openai"),
            ("Whisper.cpp (local)", "whisper-cpp"),
        };

        string provider;
        if (isInitialSetup)
        {
            provider = await PromptSelectionHelper.PromptStringAsync(host,
                "Choose STT provider:", providers, config.SttProvider ?? "groq", allowCancel: false, ct);
        }
        else
        {
            var providerResult = await PromptSelectionHelper.PromptStringWithBackAsync(host,
                "Choose STT provider:", providers, config.SttProvider ?? "groq", ct);
            if (providerResult == null)
                return changed;
            provider = providerResult;
        }

        if (provider != config.SttProvider)
        {
            config.SttProvider = provider;
            changed = true;
        }

        // ── Provider-specific settings ──
        switch (provider)
        {
            case "groq":
                var groqKey = await PromptTextAsync(host, "Groq API key (starts with gsk_)",
                    config.GroqApiKey,
                    v => v.StartsWith("gsk_"), "Must start with gsk_",
                    isInitialSetup, ct, isSecret: true, allowEmpty: false);
                if (groqKey != null && groqKey != config.GroqApiKey)
                {
                    config.GroqApiKey = groqKey;
                    changed = true;
                }
                break;

            case "openai":
                var openAiKey = await PromptTextAsync(host, "OpenAI API key for STT",
                    config.OpenAiApiKey ?? "",
                    _ => true, null,
                    isInitialSetup, ct, isSecret: true, allowEmpty: true);
                if (openAiKey != null)
                {
                    var newKey = string.IsNullOrWhiteSpace(openAiKey) ? null : openAiKey;
                    if (newKey != config.OpenAiApiKey)
                    {
                        config.OpenAiApiKey = newKey;
                        changed = true;
                    }
                }
                var openAiModel = await PromptTextAsync(host, "OpenAI STT model",
                    config.OpenAiModel ?? "whisper-1",
                    _ => true, null,
                    isInitialSetup, ct, allowEmpty: false);
                if (openAiModel != null && openAiModel != config.OpenAiModel)
                {
                    config.OpenAiModel = openAiModel;
                    changed = true;
                }
                break;

            case "whisper-cpp":
                var whisperPath = await PromptTextAsync(host, "Path to whisper-cpp executable",
                    config.WhisperCppPath ?? "",
                    _ => true, null,
                    isInitialSetup, ct, allowEmpty: true);
                if (whisperPath != null)
                {
                    var newPath = string.IsNullOrWhiteSpace(whisperPath) ? null : whisperPath;
                    if (newPath != config.WhisperCppPath)
                    {
                        config.WhisperCppPath = newPath;
                        changed = true;
                    }
                }
                var whisperModel = await PromptTextAsync(host, "Path to whisper-cpp model file",
                    config.WhisperCppModelPath ?? "",
                    _ => true, null,
                    isInitialSetup, ct, allowEmpty: true);
                if (whisperModel != null)
                {
                    var newPath = string.IsNullOrWhiteSpace(whisperModel) ? null : whisperModel;
                    if (newPath != config.WhisperCppModelPath)
                    {
                        config.WhisperCppModelPath = newPath;
                        changed = true;
                    }
                }
                break;
        }

        // ── Locale ──
        var locale = await PromptTextAsync(host, "Locale (e.g. en-US, ja-JP, ru-RU)",
            config.Locale,
            v => v.Length >= 2, "At least 2 characters",
            isInitialSetup, ct, allowEmpty: false);
        if (locale != null && locale != config.Locale)
        {
            config.Locale = locale;
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
