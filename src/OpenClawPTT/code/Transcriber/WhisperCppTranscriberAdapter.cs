using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OpenClawPTT.Transcriber;

/// <summary>
/// Adapter for local Whisper.cpp transcription.
/// Executes the whisper CLI tool with the audio file.
/// </summary>
public sealed class WhisperCppTranscriberAdapter : ITranscriber
{
    private readonly string _whisperPath;
    private readonly string _modelPath;
    private bool _disposed;

    public WhisperCppTranscriberAdapter(string whisperPath = "whisper", string modelPath = "models/ggml-base.bin")
    {
        _whisperPath = whisperPath ?? throw new ArgumentNullException(nameof(whisperPath));
        _modelPath = modelPath ?? throw new ArgumentNullException(nameof(modelPath));
    }

    public async Task<string> TranscribeAsync(byte[] wavBytes, string fileName = "audio.wav", CancellationToken ct = default)
    {
        if (wavBytes == null || wavBytes.Length == 0)
            throw new ArgumentNullException(nameof(wavBytes), "WAV bytes must not be null or empty.");

        // Write to a temp file for whisper CLI to process
        var tempDir = Path.Combine(Path.GetTempPath(), "openclaw-ptt");
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, fileName);

        try
        {
            await File.WriteAllBytesAsync(tempFile, wavBytes, ct).ConfigureAwait(false);

            var psi = new ProcessStartInfo
            {
                FileName = _whisperPath,
                Arguments = $"--model {_modelPath} --file \"{tempFile}\" --output-txt --no-timestamps",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi) ?? throw new TranscriberException("Failed to start whisper process");
            var output = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            var error = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
            
            await process.WaitForExitAsync(ct).ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                throw new TranscriberException($"whisper.cpp exited with code {process.ExitCode}: {error}");
            }

            // Output file will be same name but .txt extension
            var outputFile = Path.ChangeExtension(tempFile, ".txt");
            if (File.Exists(outputFile))
            {
                var result = await File.ReadAllTextAsync(outputFile, ct).ConfigureAwait(false);
                return result.Trim();
            }

            // If no output file, return stdout (some whisper versions output to stdout)
            return output.Trim();
        }
        finally
        {
            // Cleanup temp files
            try
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
                var txtFile = Path.ChangeExtension(tempFile, ".txt");
                if (File.Exists(txtFile))
                    File.Delete(txtFile);
            }
            catch { /* best effort cleanup */ }
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            // Nothing else to dispose for process-based transcriber
        }
    }
}
