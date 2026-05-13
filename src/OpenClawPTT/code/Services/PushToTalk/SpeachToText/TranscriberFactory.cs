using System;
using OpenClawPTT;
using OpenClawPTT.Services;

namespace OpenClawPTT.Transcriber;

/// <summary>
/// Factory for creating ITranscriber instances based on configuration.
/// </summary>
public static class TranscriberFactory
{
    public static ITranscriber Create(AppConfig config, IColorConsole colorConsole)
    {
        return (string.IsNullOrEmpty(config.SttProvider) ? AppConfig.ProviderGroq : config.SttProvider).ToLowerInvariant() switch
        {
            AppConfig.ProviderGroq => new GroqTranscriberAdapter(
                config.GroqApiKey,
                config.GroqModel!,  // null-safe: constructors handle null internally
                config.GroqRetryCount,
                config.GroqRetryDelayMs,
                config.GroqRetryBackoffFactor),

            AppConfig.ProviderOpenAi => new OpenAiTranscriberAdapter(
                config.OpenAiApiKey ?? throw new InvalidOperationException("OpenAI API key is required for OpenAI STT provider"),
                config.OpenAiModel!),  // null-safe: constructors handle null internally

            AppConfig.ProviderWhisperCpp => CreateWhisperCpp(config, colorConsole.GetStreamShellHost()
                ?? throw new InvalidOperationException("Cannot initialize whisper-cpp STT: terminal integration unavailable.")),

            _ => throw new ArgumentException($"Unknown STT provider: {config.SttProvider}")
        };
    }

    /// <summary>
    /// Creates a WhisperCppTranscriberAdapter using the WhisperCppModelManager
    /// and the configured model name. The model must be downloaded before use.
    /// </summary>
    private static ITranscriber CreateWhisperCpp(AppConfig config, IStreamShellHost host)
    {
        var modelManager = new WhisperCppModelManager(host, config.CustomDataDir ?? config.DataDir);
        var modelName = config.WhisperCppModel ?? "base";

        return new WhisperCppTranscriberAdapter(modelManager, modelName,
            whisperBinaryPath: config.WhisperCppBinaryPath);
    }
}
