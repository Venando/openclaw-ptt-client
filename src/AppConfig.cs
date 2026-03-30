using System.Text.Json.Serialization;

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
    public int VisualMode { get; set; } = 0;

    // Visual feedback settings
    public int VisualMode { get; set; } = 1; // 0 = none, 1 = red dot, 2 = green dot, 3 = blue dot

    [JsonIgnore]
    public string? SessionKey { get; set; }

    [JsonIgnore]
    public string ClientVersion => "1.0.0";


    [JsonIgnore]
    public string DataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".openclaw-ptt");

}