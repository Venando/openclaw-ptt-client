using System;
using OpenClawPTT;

namespace OpenClawPTT.Transcriber;

/// <summary>
/// Factory for creating ITranscriber instances based on configuration.
/// </summary>
public static class TranscriberFactory
{
    public static ITranscriber Create(AppConfig config)
    {
        return config.SttProvider?.ToLowerInvariant() switch
        {
            "groq" => new GroqTranscriberAdapter(
                config.GroqApiKey,
                config.GroqModel ?? "whisper-large-v3-turbo",
                config.GroqRetryCount,
                config.GroqRetryDelayMs,
                config.GroqRetryBackoffFactor),
            
            "openai" => new OpenAiTranscriberAdapter(
                config.OpenAiApiKey ?? throw new ArgumentNullException(nameof(config.OpenAiApiKey), "OpenAI API key is required for OpenAI provider"),
                config.OpenAiModel ?? "whisper-1"),
            
            "whisper-cpp" => new WhisperCppTranscriberAdapter(
                config.WhisperCppPath ?? "whisper",
                config.WhisperCppModelPath ?? "models/ggml-base.bin"),
            
            null or "" => new GroqTranscriberAdapter(
                config.GroqApiKey,
                config.GroqModel ?? "whisper-large-v3-turbo",
                config.GroqRetryCount,
                config.GroqRetryDelayMs,
                config.GroqRetryBackoffFactor),
            
            _ => throw new ArgumentException($"Unknown STT provider: {config.SttProvider}")
        };
    }
}
