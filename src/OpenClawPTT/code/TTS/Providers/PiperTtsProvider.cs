using System.Diagnostics;
using System.Text;

namespace OpenClawPTT.TTS.Providers;

/// <summary>
/// Piper TTS provider (local, fast)
/// https://github.com/rhasspy/piper
/// </summary>
public sealed class PiperTtsProvider : ITextToSpeech
{
    private readonly string _piperPath;
    private readonly string _modelPath;
    private readonly string _voice;

    public string ProviderName => "Piper TTS";

    public IReadOnlyList<string> AvailableVoices { get; } = new[]
    {
        // These are example voices - actual voices depend on installed models
        "en_US-lessac", "en_US-lessac-medium",
        "en_GB-sue-medium", "en_GB-alba-medium",
        "de_DE-thorsten-medium", "de_DE-kerstin-medium",
        "fr_FR-siwis-medium", "fr_FR-siwis-medium"
    };

    public IReadOnlyList<string> AvailableModels { get; } = Array.Empty<string>(); // Models are file-based

    public PiperTtsProvider(string piperPath = "piper", string modelPath = "", string voice = "en_US-lessac")
    {
        _piperPath = piperPath;
        _modelPath = modelPath;
        _voice = voice;
    }

    public async Task<byte[]> SynthesizeAsync(string text, string? voice = null, string? model = null, CancellationToken ct = default)
    {
        var selectedVoice = voice ?? _voice;
        var tempOutput = Path.Combine(Path.GetTempPath(), $"piper_tts_{Guid.NewGuid()}.wav");

        try
        {
            var modelFile = string.IsNullOrEmpty(model) 
                ? FindModelFile(selectedVoice) 
                : model;

            if (!File.Exists(modelFile))
            {
                throw new FileNotFoundException($"Piper model not found: {modelFile}. " +
                    $"Download models from https://github.com/rhasspy/piper/tree/master/src/pythonFrontend/sample_models.md");
            }

            var psi = new ProcessStartInfo
            {
                FileName = _piperPath,
                Arguments = $"--model_file \"{modelFile}\" --output_file \"{tempOutput}\"",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start piper process");

            await process.StandardInput.WriteLineAsync(text);
            process.StandardInput.Close();

            await process.WaitForExitAsync(ct);

            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync(ct);
                throw new InvalidOperationException($"Piper TTS failed: {error}");
            }

            if (!File.Exists(tempOutput))
            {
                throw new InvalidOperationException($"Piper TTS did not produce output file: {tempOutput}");
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

    private string FindModelFile(string voice)
    {
        // Look for model in model path
        var onnxFile = Path.Combine(_modelPath, $"{voice}.onnx");
        if (File.Exists(onnxFile))
            return onnxFile;

        var onnxJsonFile = Path.Combine(_modelPath, $"{voice}.onnx.json");
        if (File.Exists(onnxJsonFile))
            return onnxJsonFile;

        // Default: assume voice is the full path to model file
        return voice;
    }
}
