using System.Text.Json.Serialization;
using OpenClawPTT.TTS;

namespace OpenClawPTT;

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
    public int GroqRetryCount { get; set; } = 0;
    public int GroqRetryDelayMs { get; set; } = 1000;
    public double GroqRetryBackoffFactor { get; set; } = 2.0;
    public double ReconnectDelaySeconds { get; set; } = 1.5;

    // Text formatting
    public int RightMarginIndent { get; set; } = 5; // Minimum right margin indent in characters
    public bool EnableWordWrap { get; set; } = true; // Enable word wrapping and margin indent

    // Visual feedback settings
    public int VisualMode { get; set; } = 1; // 0 = none, 1 = red dot, 2 = green dot, 3 = blue dot

    // Visual feedback settings
    public bool VisualFeedbackEnabled { get; set; } = true;
    public string VisualFeedbackPosition { get; set; } = "TopRight";
    public int VisualFeedbackSize { get; set; } = 20;
    public double VisualFeedbackOpacity { get; set; } = 1.0;
    public string VisualFeedbackColor { get; set; } = "#FF0000";

    // TTS settings
    public TtsProviderType TtsProvider { get; set; } = TtsProviderType.OpenAI;
    public string? TtsOpenAiApiKey { get; set; }
    public string? TtsSubscriptionKey { get; set; }
    public string TtsRegion { get; set; } = "eastus";
    public string TtsVoice { get; set; } = "alloy";  // Provider-specific voice name
    public string TtsModel { get; set; } = "tts-1";  // Provider-specific model

    // Coqui TTS settings
    public string CoquiModelPath { get; set; } = "";
    public string CoquiModelName { get; set; } = "tts_models/multilingual/mxtts/vits谈";

    // Python TTS + uv bootstrap settings
    public bool UseUvPython { get; set; } = false;
    public string? UvToolsPath { get; set; }  // null = default to DataDir/tools/uv.exe
    public string? TtsServiceScriptPath { get; set; }

    // Piper TTS settings
    public string PiperPath { get; set; } = "piper";
    public string PiperModelPath { get; set; } = "";
    public string PiperVoice { get; set; } = "en_US-lessac";

    // Audio response settings
    public string AudioResponseMode { get; set; } = "text-only"; // text-only, audio-only, both
    public string? TtsApiKey { get; set; } // Optional ElevenLabs API key
    public string TtsVoiceId { get; set; } = "pNInz6obpgDQGcFmaJgB"; // Default ElevenLabs voice

    [JsonIgnore]
    public string? SessionKey { get; set; }

    [JsonIgnore]
    public string ClientVersion => "1.0.0";


    [JsonIgnore]
    public string DataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".openclaw-ptt");

}