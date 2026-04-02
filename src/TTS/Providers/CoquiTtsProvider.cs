using System.Diagnostics;
using System.Text;

namespace OpenClawPTT.TTS.Providers;

/// <summary>
/// Coqui TTS provider (local open-source)
/// https://github.com/coqui-ai/TTS
/// </summary>
public sealed class CoquiTtsProvider : ITextToSpeech
{
    private readonly string _modelPath;
    private readonly string _modelName;
    private readonly string _pythonPath;
    private readonly string _espeakNgPath;

    public string ProviderName => "Coqui TTS";

    // Default models available with Coqui TTS
    public IReadOnlyList<string> AvailableVoices { get; } = new[]
    {
        "default"
    };

    // Popular Coqui TTS models
    public IReadOnlyList<string> AvailableModels { get; } = new[]
    {
        "tts_models/multilingual/mxtts/vits",      // VITS multilingual
        "tts_models/en/ljspeech/vits",              // English VITS
        "tts_models/en/vctk/vits",                  // English VCTK
        "tts_models/es/mai_speak/vits",             // Spanish
        "tts_models/fr/css10/vits",                // French
        "tts_models/de/thorsten/vits",              // German
    };

    public CoquiTtsProvider(string modelPath = "", string modelName = "tts_models/multilingual/mxtts/vits", string? pythonPath = null, string? espeakNgPath = null)
    {
        _modelPath = modelPath;
        _modelName = modelName;
        _pythonPath = pythonPath ?? "";
        _espeakNgPath = espeakNgPath ?? "";
    }

    public async Task<byte[]> SynthesizeAsync(string text, string? voice = null, string? model = null, CancellationToken ct = default)
    {
        var selectedModel = model ?? _modelName;
        var tempOutput = Path.Combine(Path.GetTempPath(), $"coqui_tts_{Guid.NewGuid()}.wav");

        try
        {
            var args = new StringBuilder();
            args.Append($" --model_name \"{selectedModel}\"");
            args.Append($" --text \"{text}\"");
            args.Append($" --out_path \"{tempOutput}\"");

            var ttsExe = Path.Combine(_pythonPath, "Scripts", "tts.exe");

            if (!File.Exists(ttsExe))
            {
                throw new InvalidOperationException(
                    $"Coqui TTS executable not found at: {ttsExe}. " +
                    "Set PythonPath in config to your Python installation directory " +
                    "(e.g. C:/Users/eldve/AppData/Local/Programs/Python/Python311).");
            }

            var psi = new ProcessStartInfo
            {
                FileName = ttsExe,
                Arguments = args.ToString(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            if (!string.IsNullOrEmpty(_espeakNgPath))
                psi.Environment["PATH"] = _espeakNgPath + ";" + psi.Environment["PATH"];

            using var process = Process.Start(psi) ?? throw new InvalidOperationException(
                "Failed to start Coqui TTS process. Check EspeakNgPath in config " +
                $"(currently: '{_espeakNgPath ?? "(not set)"}'). Ensure espeak-ng is installed and the path is correct.");

            await process.WaitForExitAsync(ct);

            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync(ct);

                // Provide actionable hints based on error content
                if (error.Contains("espeak", StringComparison.OrdinalIgnoreCase) ||
                    error.Contains("phonemizer", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Coqui TTS failed: espeak-ng not found. " +
                        "Install espeak-ng from https://github.com/espeak-ng/espeak-ng/releases " +
                        "and ensure it's in your system PATH.");
                }
                else if (error.Contains("No module", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Coqui TTS failed: Python module error. {error.Split('\n').LastOrDefault()?.Trim()}");
                }
                else if (error.Contains("model", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Coqui TTS failed: model not found. Verify CoquiModelName in config is correct " +
                        $"(e.g. tts_models/en/ljspeech/vits). Error: {error.Split('\n').FirstOrDefault()?.Trim()}");
                }
                else
                {
                    throw new InvalidOperationException($"Coqui TTS failed: {error.Split('\n').FirstOrDefault()?.Trim()}");
                }
            }

            if (!File.Exists(tempOutput))
            {
                throw new InvalidOperationException("Coqui TTS did not produce output file.");
            }

            var audio = await File.ReadAllBytesAsync(tempOutput, ct);
            return audio;
        }
        finally
        {
            if (File.Exists(tempOutput))
            {
                try { File.Delete(tempOutput); } catch { /* ignore */ }
            }
        }
    }
}
