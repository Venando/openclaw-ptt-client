using System.Runtime.InteropServices;
using System.Text.Json;

namespace OpenClawPTT.Services;

/// <summary>
/// Abstraction for config persistence, enabling testability.
/// </summary>
public interface IConfigStorage
{
    AppConfig? Load();
    void Save(AppConfig config);
}

/// <summary>
/// Default implementation that persists config to disk as JSON.
/// </summary>
public sealed class FileConfigStorage : IConfigStorage
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public AppConfig? Load()
    {
        var cfg = new AppConfig();
        var path = ConfigPath(cfg);

        if (!File.Exists(path))
            return null;

        var json = File.ReadAllText(path);
        try
        {
            var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOpts);
            if (config != null)
            {
                using var doc = JsonDocument.Parse(json);

                // Backward compatibility: if VisualMode is missing or was None (0), disable via VisualFeedbackEnabled
                if (!doc.RootElement.TryGetProperty("VisualMode", out _) || config.VisualMode == 0)
                {
                    config.VisualFeedbackEnabled = false;
                    config.VisualMode = VisualMode.SolidDot;
                }

                // Set platform-specific default for EspeakNgPath if not configured
                if (string.IsNullOrEmpty(config.EspeakNgPath))
                {
                    config.EspeakNgPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                        ? @"C:\Program Files\eSpeak NG"
                        : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                            ? "/opt/homebrew/bin/espeak-ng"
                            : "/usr/bin/espeak-ng";
                }
            }
            return config;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Save(AppConfig cfg)
    {
        var path = ConfigPath(cfg);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(cfg, JsonOpts));
    }

    private static string ConfigPath(AppConfig cfg)
    {
        var filename = "config.json";
#if DEBUG
        filename = "config.debug.json";
#endif
        return Path.Combine(cfg.DataDir, filename);
    }
}
