using System.Text.Json.Serialization;
using OpenClawPTT.TTS;

namespace OpenClawPTT;

public enum VisualMode : int
{
    SolidDot = 1,
    GlowDot = 2,
}

public enum ReplyDisplayMode
{
    /// <summary>Use streaming delta events only (AgentReplyDeltaStart/Delta/End). Suppresses AgentReplyFull.</summary>
    Delta = 0,
    /// <summary>Use full reply events only (AgentReplyFull). Suppresses delta events.</summary>
    Full = 1,
    /// <summary>Both delta and full reply fire (default). Use this if unsure.</summary>
    Both = 2,
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
    public bool LogConnect { get; set; } = false;
    public bool LogHello { get; set; } = false;
    public bool LogSnapshot { get; set; } = false;
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
    public bool ShowThinking { get; set; } = false;
    public bool DebugToolCalls { get; set; } = false;
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

    // Python TTS debug settings
    public bool PythonTtsDebugLog { get; set; } = false;

    // Audio response settings
    public string AudioResponseMode { get; set; } = "text-only"; // text-only, audio-only, both
    public string? TtsApiKey { get; set; } // Optional ElevenLabs API key
    public string? TtsVoiceId { get; set; } // Default ElevenLabs voice

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