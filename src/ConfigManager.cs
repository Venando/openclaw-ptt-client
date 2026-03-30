using System;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

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

    public async Task<AppConfig> RunSetup(AppConfig? existing = null, CancellationToken cancellationToken = default)
    {
        var cfg = existing ?? new AppConfig();

        cfg.GatewayUrl = await Prompt(
            "Gateway URL",
            cfg.GatewayUrl,
            v => Uri.TryCreate(v, UriKind.Absolute, out var u)
                 && (u.Scheme == "ws" || u.Scheme == "wss"),
            cancellationToken);

        cfg.AuthToken = await Prompt(
            "Auth token (OPENCLAW_GATEWAY_TOKEN)",
            cfg.AuthToken ?? Environment.GetEnvironmentVariable("OPENCLAW_GATEWAY_TOKEN") ?? "",
            _ => true,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(cfg.AuthToken))
            cfg.AuthToken = null;

        var useTls = cfg.GatewayUrl.StartsWith("wss://", StringComparison.OrdinalIgnoreCase);
        if (useTls)
        {
            cfg.TlsFingerprint = await Prompt(
                "TLS cert fingerprint (blank to skip pinning)",
                cfg.TlsFingerprint ?? "",
                _ => true,
                cancellationToken);

            if (string.IsNullOrWhiteSpace(cfg.TlsFingerprint))
                cfg.TlsFingerprint = null;
        }

        cfg.GroqApiKey = await Prompt(
            "Groq API key",
            cfg.GroqApiKey,
            value => value.StartsWith("gsk_"),
            cancellationToken);

        cfg.Locale = await Prompt("Locale", cfg.Locale, v => v.Length >= 2, cancellationToken);

        var rate = await Prompt("Audio sample rate", cfg.SampleRate.ToString(),
            v => int.TryParse(v, out var n) && n is >= 8000 and <= 48000,
            cancellationToken);
        cfg.SampleRate = int.Parse(rate);

        var maxSec = await Prompt("Max recording seconds", cfg.MaxRecordSeconds.ToString(),
            v => int.TryParse(v, out var n) && n is >= 5 and <= 600,
            cancellationToken);
        cfg.MaxRecordSeconds = int.Parse(maxSec);

        cfg.RealTimeReplyOutput = bool.Parse(await Prompt(
            "Real-time reply output",
            cfg.RealTimeReplyOutput.ToString(),
            v => bool.TryParse(v, out _),
            cancellationToken));

        // Shortcut settings
        cfg.HotkeyCombination = await Prompt(
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
        
        cfg.HoldToTalk = bool.Parse(await Prompt(
            "Hold-to-talk mode (true/false)",
            cfg.HoldToTalk.ToString(),
            v => bool.TryParse(v, out _)));

        // Visual feedback settings
        cfg.VisualFeedbackEnabled = bool.Parse(await Prompt(
            "Visual feedback enabled (true/false)",
            cfg.VisualFeedbackEnabled.ToString(),
            v => bool.TryParse(v, out _)));
        
        var positionInput = await Prompt(
            "Visual feedback position (TopLeft, TopRight, BottomLeft, BottomRight)",
            cfg.VisualFeedbackPosition,
            v => new[] { "TopLeft", "TopRight", "BottomLeft", "BottomRight" }.Contains(v, StringComparer.OrdinalIgnoreCase));
        cfg.VisualFeedbackPosition = new[] { "TopLeft", "TopRight", "BottomLeft", "BottomRight" }
            .First(p => p.Equals(positionInput, StringComparison.OrdinalIgnoreCase));
        
        cfg.VisualFeedbackSize = int.Parse(await Prompt(
            "Visual feedback dot size (pixels)",
            cfg.VisualFeedbackSize.ToString(),
            v => int.TryParse(v, out var n) && n > 0 && n <= 200));
        
        cfg.VisualFeedbackOpacity = double.Parse(await Prompt(
            "Visual feedback opacity (0.0 to 1.0)",
            cfg.VisualFeedbackOpacity.ToString("F2"),
            v => double.TryParse(v, out var d) && d >= 0.0 && d <= 1.0));
        
        cfg.VisualFeedbackColor = await Prompt(
            "Visual feedback color (hex #RRGGBB)",
            cfg.VisualFeedbackColor,
            v => System.Text.RegularExpressions.Regex.IsMatch(v, @"^#?([0-9A-Fa-f]{6})$"));

        await Task.CompletedTask;
        return cfg;
    }

    private static async Task<string> Prompt(string label, string defaultVal, Func<string, bool> validate, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            var def = string.IsNullOrEmpty(defaultVal) ? "" : $" [{defaultVal}]";
            Console.Write($"  {label}{def}: ");
            string input;
            try
            {
                input = (await Console.In.ReadLineAsync(cancellationToken))?.Trim() ?? "";
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine(); // move to new line after ^C
                throw;
            }

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