using System.Text.Json.Serialization;
using OpenClawPTT.TTS;

namespace OpenClawPTT;

public enum ThinkingMode
{
    /// <summary>Display nothing.</summary>
    None = 0,
    /// <summary>Show a thinking emoji like a tool call with empty args.</summary>
    Emoji = 1,
    /// <summary>Show first N lines of thinking, tool-output style.</summary>
    FirstNLines = 2,
    /// <summary>Show all thinking, agent-reply style (supports future streaming).</summary>
    Full = 3,
}

public sealed class AppConfig
{
    public string GatewayUrl { get; set; } = "ws://localhost:18789";
    public string? AuthToken { get; set; }
    public string? DeviceToken { get; set; }
    public string? TlsFingerprint { get; set; }
    public string Locale { get; set; } = "en-US";
    public int SampleRate { get; set; } = 16_000;
    public int Channels { get; set; } = 1;
    public int BitsPerSample { get; set; } = 16;
    public int MaxRecordSeconds { get; set; } = 120;
    /// <summary>Controls diagnostic log output verbosity. Default = Error (only errors shown).</summary>
    public LogLevel DebugLevel { get; set; } = LogLevel.Error;
    public string GroqApiKey { get; set; } = "gsk_";
    public bool RealTimeReplyOutput { get; set; } = true;
    public ReplyDisplayMode ReplyDisplayMode { get; set; } = ReplyDisplayMode.Both;

    // STT Provider configuration
    public string? SttProvider { get; set; } // "groq", "openai", "whisper-cpp", null = default to groq
    public string? OpenAiApiKey { get; set; }
    public string? OpenAiModel { get; set; }
    public string? WhisperCppPath { get; set; }
    public string? WhisperCppModelPath { get; set; }
    public string? GroqModel { get; set; }

    // Shortcut settings
    public string HotkeyCombination { get; set; } = "Alt+=";
    public bool HoldToTalk { get; set; } = false;
    public ThinkingMode ThinkingDisplayMode { get; set; } = ThinkingMode.FirstNLines;
    public int ThinkingPreviewLines { get; set; } = 5;
    public bool RequireConfirmBeforeSend { get; set; } = false;

    public string AgentName { get; set; } = "Agent";
    public string TranscriptionPromptPrefix { get; set; } = "[It's a raw speech-to-text transcription]: ";
    // AudioWrapPrompt and IsAudioEnabled removed — no longer needed
    public int GroqRetryCount { get; set; } = 0;
    public int GroqRetryDelayMs { get; set; } = 1000;
    public double GroqRetryBackoffFactor { get; set; } = 2.0;
    public double ReconnectDelaySeconds { get; set; } = 1.5;

    // Text formatting
    public int RightMarginIndent { get; set; } = 5; // Minimum right margin indent in characters
    public bool EnableWordWrap { get; set; } = true; // Enable word wrapping and margin indent

    // Visual feedback settings
    public VisualMode VisualMode { get; set; } = VisualMode.SolidDot;

    // Visual feedback settings
    public bool VisualFeedbackEnabled { get; set; } = true;
    public string VisualFeedbackPosition { get; set; } = "TopRight";
    public int VisualFeedbackSize { get; set; } = 20;
    public double VisualFeedbackOpacity { get; set; } = 1.0;
    public string VisualFeedbackColor { get; set; } = "#FF0000";
    public int VisualFeedbackRimThickness { get; set; } = 8;

    // TTS settings
    public TtsProviderType TtsProvider { get; set; } = TtsProviderType.OpenAI;
    public string? TtsOpenAiApiKey { get; set; }
    public string? TtsSubscriptionKey { get; set; }
    public string? TtsRegion { get; set; }
    public string? TtsVoice { get; set; }  // Provider-specific voice name
    public string? TtsModel { get; set; }  // Provider-specific model

    // Coqui TTS settings
    public string? CoquiModelPath { get; set; }
    public string? CoquiModelName { get; set; }
    public string? CoquiConfigPath { get; set; }
    public string? PythonPath { get; set; }

    // Piper TTS settings
    public string? PiperPath { get; set; }
    public string? PiperModelPath { get; set; }
    public string? PiperVoice { get; set; }

    // eSpeak NG path for Coqui TTS (platform-specific default)
    public string? EspeakNgPath { get; set; }



    // Direct LLM settings (bypass agent for direct LLM calls)
    public string? DirectLlmToken { get; set; }
    public string? DirectLlmUrl { get; set; }
    public string? DirectLlmModelName { get; set; }
    public string DirectLlmApiType { get; set; } = "openai-completions"; // "openai-completions" or "anthropic-messages"

    // Audio response settings
    public string AudioResponseMode { get; set; } = "text-only"; // text-only, audio-only, both
    public string? TtsApiKey { get; set; } // Optional ElevenLabs API key
    public string? TtsVoiceId { get; set; } // Default ElevenLabs voice

    // TTS SISO settings
    public string TtsOutputMode { get; set; } = "siso"; // "always-on", "siso", "off"
    public int TtsDirectMaxChars { get; set; } = 300;   // Under this: speak directly
    public int TtsMaxChars { get; set; } = 1500;        // Upper limit for TTS output
    public string TtsCodeBlockMode { get; set; } = "smart"; // "summarize", "skip", "smart"
    public string TtsTooLongFallback { get; set; } = "truncate"; // "truncate" or "skip"
    public bool TtsUseDirectLlmSummary { get; set; } = true;

    [JsonIgnore]
    public string ClientVersion => "1.0.0";

    [JsonIgnore]
    public string? CustomDataDir { get; set; }

    [JsonIgnore]
    public string DataDir => CustomDataDir
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".openclaw-ptt");

}