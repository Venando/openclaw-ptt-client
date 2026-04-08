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

    public string ProviderName => "Coqui TTS";

    // Default models available with Coqui TTS
    public IReadOnlyList<string> AvailableVoices { get; } = new[]
    {
        "default"
    };

    // Popular Coqui TTS models
    public IReadOnlyList<string> AvailableModels { get; } = new[]
    {
        "tts_models/multilingual/mxtts/vits谈",     // VITS multilingual
        "tts_models/en/ljspeech/vits",              // English VITS
        "tts_models/en/vctk/vits",                  // English VCTK
        "tts_models/es/mai_speak/vits",             // Spanish
        "tts_models/fr/css10/vits",                // French
        "tts_models/de/thorsten/vits",              // German
    };

    public CoquiTtsProvider(string modelPath = "", string modelName = "tts_models/multilingual/mxtts/vits谈")
    {
        _modelPath = modelPath;
        _modelName = modelName;
    }

    public async Task<byte[]> SynthesizeAsync(string text, string? voice = null, string? model = null, CancellationToken ct = default)
    {
        var selectedModel = model ?? _modelName;
        var tempOutput = Path.Combine(Path.GetTempPath(), $"coqui_tts_{Guid.NewGuid()}.wav");

        try
        {
            var args = new StringBuilder();
            args.Append($"--model_name \"{selectedModel}\"");
            args.Append($"--text \"{text}\"");
            args.Append($"--output_path \"{tempOutput}\"");

            if (!string.IsNullOrEmpty(_modelPath))
            {
                args.Append($"--model_dir \"{_modelPath}\"");
            }

            var psi = new ProcessStartInfo
            {
                FileName = "tts",
                Arguments = args.ToString(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start tts process");

            await process.WaitForExitAsync(ct);

            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync(ct);
                throw new InvalidOperationException($"Coqui TTS failed: {error}");
            }

            if (!File.Exists(tempOutput))
            {
                throw new InvalidOperationException($"Coqui TTS did not produce output file: {tempOutput}");
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
