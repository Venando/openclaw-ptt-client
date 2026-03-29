using System.Text.Json;

namespace OpenClawPTT;

public sealed class ConfigManager
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition =
            System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private string ConfigPath(AppConfig cfg) =>
        Path.Combine(cfg.DataDir, "config.json");

    public AppConfig? Load()
    {
        var probe = new AppConfig();
        var path = ConfigPath(probe);

        if (!File.Exists(path))
            return null;

        var json = File.ReadAllText(path);
        var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOpts);
        if (config != null)
        {
            // Backward compatibility: if VisualMode is missing from JSON, default to 1 (red dot)
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("VisualMode", out _))
            {
                config.VisualMode = 1;
            }
        }
        return config;
    }

    public void Save(AppConfig cfg)
    {
        var path = ConfigPath(cfg);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(cfg, JsonOpts));
    }

    public List<string> Validate(AppConfig cfg)
    {
        var issues = new List<string>();

        if (string.IsNullOrWhiteSpace(cfg.GatewayUrl))
            issues.Add("Gateway URL is required.");

        if (!Uri.TryCreate(cfg.GatewayUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != "ws" && uri.Scheme != "wss"))
            issues.Add("Gateway URL must start with ws:// or wss://");

        if (string.IsNullOrWhiteSpace(cfg.AuthToken)
            && string.IsNullOrWhiteSpace(cfg.DeviceToken))
            issues.Add("Auth token or device token is required.");

        if (cfg.SampleRate is < 8000 or > 48000)
            issues.Add("Sample rate must be between 8000 and 48000.");

        if (cfg.ReconnectDelaySeconds <= 0)
            issues.Add("Reconnect delay must be positive.");

        if (cfg.VisualMode < 0 || cfg.VisualMode > 3)
            issues.Add("VisualMode must be between 0 and 3.");

        return issues;
    }

    public async Task<AppConfig> RunSetup(AppConfig? existing = null)
    {
        var cfg = existing ?? new AppConfig();

        cfg.GatewayUrl = Prompt(
            "Gateway URL",
            cfg.GatewayUrl,
            v => Uri.TryCreate(v, UriKind.Absolute, out var u)
                 && (u.Scheme == "ws" || u.Scheme == "wss"));

        cfg.AuthToken = Prompt(
            "Auth token (OPENCLAW_GATEWAY_TOKEN)",
            cfg.AuthToken ?? Environment.GetEnvironmentVariable("OPENCLAW_GATEWAY_TOKEN") ?? "",
            _ => true);

        if (string.IsNullOrWhiteSpace(cfg.AuthToken))
            cfg.AuthToken = null;

        var useTls = cfg.GatewayUrl.StartsWith("wss://", StringComparison.OrdinalIgnoreCase);
        if (useTls)
        {
            cfg.TlsFingerprint = Prompt(
                "TLS cert fingerprint (blank to skip pinning)",
                cfg.TlsFingerprint ?? "",
                _ => true);

            if (string.IsNullOrWhiteSpace(cfg.TlsFingerprint))
                cfg.TlsFingerprint = null;
        }

        cfg.GroqApiKey = Prompt(
            "Groq API key",
            cfg.GroqApiKey,
            value => value.StartsWith("gsk_")
        );

        cfg.Locale = Prompt("Locale", cfg.Locale, v => v.Length >= 2);

        var rate = Prompt("Audio sample rate", cfg.SampleRate.ToString(),
            v => int.TryParse(v, out var n) && n is >= 8000 and <= 48000);
        cfg.SampleRate = int.Parse(rate);

        var maxSec = Prompt("Max recording seconds", cfg.MaxRecordSeconds.ToString(),
            v => int.TryParse(v, out var n) && n is >= 5 and <= 600);
        cfg.MaxRecordSeconds = int.Parse(maxSec);

        cfg.RealTimeReplyOutput = bool.Parse(Prompt(
            "Real-time reply output",
            cfg.RealTimeReplyOutput.ToString(),
            v => bool.TryParse(v, out _)));

        // Shortcut settings
        cfg.HotkeyCombination = Prompt(
            "Hotkey combination (e.g., Alt+=, Ctrl+Shift+Space)",
            cfg.HotkeyCombination,
            v =>
            {
                try
                {
                    HotkeyMapping.Parse(v);
                    return true;
                }
                catch
                {
                    return false;
                }
            });
        
        cfg.HoldToTalk = bool.Parse(Prompt(
            "Hold-to-talk mode (true/false)",
            cfg.HoldToTalk.ToString(),
            v => bool.TryParse(v, out _)));

        await Task.CompletedTask;
        return cfg;
    }

    private static string Prompt(string label, string defaultVal, Func<string, bool> validate)
    {
        while (true)
        {
            var def = string.IsNullOrEmpty(defaultVal) ? "" : $" [{defaultVal}]";
            Console.Write($"  {label}{def}: ");
            var input = Console.ReadLine()?.Trim() ?? "";

            if (string.IsNullOrEmpty(input) && !string.IsNullOrEmpty(defaultVal))
                input = defaultVal;

            if (validate(input))
                return input;

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("    Invalid value, try again.");
            Console.ResetColor();
        }
    }
}