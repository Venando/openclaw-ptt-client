using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.Services;

namespace OpenClawPTT.Transcriber;

/// <summary>
/// Manages whisper.cpp model files: listing, downloading, and deleting.
/// Models are stored in <c>~/.openclaw-ptt/whisper-models/</c>.
/// </summary>
public sealed class WhisperCppModelManager
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(30) };
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _downloadLocks = new();

    /// <summary>All known whisper.cpp models available on HuggingFace.</summary>
    public static readonly IReadOnlyList<WhisperModelInfo> AvailableModels = new List<WhisperModelInfo>
    {
        new("tiny",        "Tiny (78 MB) — fastest, lowest accuracy"),
        new("tiny.en",     "Tiny English (78 MB) — English-only, faster"),
        new("base",        "Base (148 MB) — good balance for quick use"),
        new("base.en",     "Base English (148 MB) — English-only"),
        new("small",       "Small (488 MB) — decent accuracy"),
        new("small.en",    "Small English (488 MB) — English-only"),
        new("medium",      "Medium (1.5 GB) — good accuracy"),
        new("medium.en",   "Medium English (1.5 GB) — English-only"),
        new("large-v1",    "Large v1 (3.1 GB) — high accuracy"),
        new("large-v2",    "Large v2 (3.1 GB) — improved"),
        new("large-v3",    "Large v3 (3.1 GB) — latest large model"),
        new("large-v3-turbo", "Large v3 Turbo (1.6 GB) — fast, high accuracy"),
    };

    private readonly string _modelsDir;
    private readonly IStreamShellHost _host;

    public WhisperCppModelManager(IStreamShellHost host, string? dataDir = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        var baseDir = dataDir
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".openclaw-ptt");
        _modelsDir = Path.Combine(baseDir, "whisper-models");
        Directory.CreateDirectory(_modelsDir);
    }

    /// <summary>Directory where model files are stored.</summary>
    public string ModelsDir => _modelsDir;

    /// <summary>Get the full path to a model file by name.</summary>
    public string GetModelPath(string modelName)
    {
        var fileName = $"ggml-{modelName}.bin";
        return Path.Combine(_modelsDir, fileName);
    }

    /// <summary>Check if a model is already downloaded.</summary>
    public bool IsDownloaded(string modelName)
    {
        return File.Exists(GetModelPath(modelName));
    }

    /// <summary>Lists the names of all currently downloaded models.</summary>
    public IReadOnlyList<string> GetDownloadedModels()
    {
        if (!Directory.Exists(_modelsDir))
            return Array.Empty<string>();

        return Directory.GetFiles(_modelsDir, "ggml-*.bin")
            .Select(f => Path.GetFileNameWithoutExtension(f)!.Replace("ggml-", ""))
            .ToList();
    }

    /// <summary>
    /// Downloads a whisper model from HuggingFace.
    /// Reports progress via <paramref name="progressCallback"/>:
    /// (fileName, status, downloadedBytes, totalBytes, isComplete).
    /// </summary>
    public async Task DownloadModelAsync(
        string modelName,
        Action<string, string, long?, long?, bool>? progressCallback = null,
        CancellationToken ct = default)
    {
        var fileName = $"ggml-{modelName}.bin";
        var url = $"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/{fileName}";
        var destPath = GetModelPath(modelName);

        // If already downloaded, just report complete
        if (File.Exists(destPath))
        {
            var existingInfo = new FileInfo(destPath);
            progressCallback?.Invoke(fileName, "Already downloaded", existingInfo.Length, existingInfo.Length, true);
            return;
        }

        progressCallback?.Invoke(fileName, "Starting download...", null, null, false);

        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        var tempPath = destPath + ".download";

        // C6: Serialize per-model downloads to avoid TOCTOU race
        var semaphore = _downloadLocks.GetOrAdd(modelName, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            long totalRead = 0;
            
            // 1. Create a nested scope for the FileStream
            {
                await using var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
                using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

                var buffer = new byte[8192];
                int bytesRead;
                var lastReportTime = Environment.TickCount64;

                while ((bytesRead = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    ct.ThrowIfCancellationRequested(); // MEDIUM: check cancellation in loop
                    await fs.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(false);
                    totalRead += bytesRead;

                    var now = Environment.TickCount64;
                    if (now - lastReportTime >= 100)
                    {
                        progressCallback?.Invoke(fileName, "Downloading...", totalRead, totalBytes, false);
                        lastReportTime = now;
                    }
                }
            }

            // 2. Atomic move with overwrite
            File.Move(tempPath, destPath, overwrite: true);

            progressCallback?.Invoke(fileName, "Download complete", totalRead, totalRead, true);
        }
        catch (OperationCanceledException)
        {
            // H10: Rethrow silently — caller handles cancellation display
            try { File.Delete(tempPath); } catch { /* best effort */ }
            throw;
        }
        catch (Exception ex)
        {
            _host.AddMessage($"[red][download] failed to download: {ex.Message} [/]");
            // Clean up temp file on failure
            try { File.Delete(tempPath); } catch { /* best effort */ }
            throw;
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>Deletes a downloaded model file.</summary>
    /// <returns>True if the model existed and was deleted.</returns>
    public bool DeleteModel(string modelName)
    {
        var path = GetModelPath(modelName);
        if (!File.Exists(path))
            return false;

        File.Delete(path);
        return true;
    }

    /// <summary>
    /// Attempts to find the whisper CLI binary on the system.
    /// Checks PATH, common install locations.
    /// </summary>
    /// <returns>The path to the first whisper binary found, or null if not found.</returns>
    public static string? FindWhisperBinary()
    {
        return FindAllWhisperBinaries().FirstOrDefault()?.Path;
    }

    /// <summary>
    /// Finds all whisper CLI binaries on the system with their detected type.
    /// </summary>
    public static IReadOnlyList<WhisperBinaryInfo> FindAllWhisperBinaries()
    {
        var results = new List<WhisperBinaryInfo>();

        // Check common binary names on PATH
        var names = new[] { "whisper", "whisper-cli", "whisper.cpp" };
        foreach (var name in names)
        {
            var path = FindOnPath(name);
            if (path != null && !results.Any(r => r.Path == path))
                results.Add(new WhisperBinaryInfo(path, IsPythonOpenAiWhisper(path) ? WhisperType.Python : WhisperType.Cpp));
        }

        // Check common locations
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var commonPaths = new[]
        {
            Path.Combine(homeDir, "bin", "whisper"),
            Path.Combine(homeDir, "bin", "whisper-cli"),
            "/usr/local/bin/whisper",
            "/usr/local/bin/whisper-cli",
            "/usr/bin/whisper",
            "/usr/bin/whisper-cli",
        };

        foreach (var path in commonPaths)
        {
            if (File.Exists(path) && !results.Any(r => r.Path == path))
                results.Add(new WhisperBinaryInfo(path, IsPythonOpenAiWhisper(path) ? WhisperType.Python : WhisperType.Cpp));
        }

        // Also check the configured binary path if set
        // (handled separately — this is discovery only)

        return results;
    }

    /// <summary>
    /// Detects whether the given binary is Python openai-whisper (uses model names,
    /// auto-downloads from HuggingFace) vs native C++ whisper.cpp (uses .bin files).
    /// </summary>
    public static bool IsPythonOpenAiWhisper(string binaryPath)
    {
        try
        {
            if (!File.Exists(binaryPath))
                return false;

            using var reader = new StreamReader(binaryPath);
            var firstLine = reader.ReadLine();
            if (firstLine != null && (firstLine.StartsWith("#!") || firstLine.Contains("python", StringComparison.OrdinalIgnoreCase)))
                return true;

            var dir = Path.GetDirectoryName(binaryPath);
            if (dir != null && (dir.Contains("Python") || dir.Contains("python")))
                return true;

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static string? FindOnPath(string name)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
            return null;

        var nameWithExt = OperatingSystem.IsWindows() ? name + ".exe" : name;

        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            var fullPath = Path.Combine(dir, nameWithExt);
            if (File.Exists(fullPath))
                return fullPath;
        }

        return null;
    }

    // ── Python openai-whisper cache management ───────────────────────

    /// <summary>
    /// Returns the standard Python openai-whisper model cache directory.
    /// Linux: ~/.cache/whisper/
    /// macOS: ~/Library/Caches/whisper/
    /// Windows: %USERPROFILE%\.cache\whisper\
    /// </summary>
    public static string GetPythonCacheDir()
    {
        if (OperatingSystem.IsWindows())
        {
            var userProfile = Environment.GetEnvironmentVariable("USERPROFILE") ?? "C:\\Users\\default";
            return Path.Combine(userProfile, ".cache", "whisper");
        }

        if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Caches", "whisper");
        }

        // Linux / other Unix
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(homeDir, ".cache", "whisper");
    }

    /// <summary>
    /// Checks if a Python openai-whisper model is already cached locally.
    /// Python models use .pt (PyTorch) or .en.pt files in the Whisper cache directory.
    /// </summary>
    public static bool IsPythonModelCached(string modelName)
    {
        var cacheDir = GetPythonCacheDir();
        if (!Directory.Exists(cacheDir))
            return false;

        // Check for the standard PyTorch model file (model_name.pt or model_name.en.pt)
        if (File.Exists(Path.Combine(cacheDir, $"{modelName}.pt")))
            return true;

        return false;
    }

    /// <summary>
    /// Triggers model download for Python openai-whisper by running the whisper binary
    /// with a tiny silent WAV file. The model is auto-downloaded by the Python process
    /// to ~/.cache/whisper/ before transcription begins.
    /// Reports progress via <paramref name="progressCallback"/> (indeterminate).
    /// </summary>
    public static async Task DownloadPythonModelAsync(
        string binaryPath,
        string modelName,
        Action<string, string, long?, long?, bool>? progressCallback = null,
        CancellationToken ct = default)
    {
        // If already cached, just report complete
        if (IsPythonModelCached(modelName))
        {
            progressCallback?.Invoke(modelName, "Already cached", null, null, true);
            return;
        }

        progressCallback?.Invoke(modelName, "Downloading model...", null, null, false);

        // Create a tiny valid WAV file (44-byte header + minimal silence)
        var tempDir = Path.Combine(Path.GetTempPath(), "openclaw-ptt-whisper");
        Directory.CreateDirectory(tempDir);
        var dummyWav = Path.Combine(tempDir, $"whisper-dl-{modelName}.wav");

        try
        {
            // Write a minimal 16-bit mono WAV at 16kHz, 0.1 seconds of silence
            CreateSilentWav(dummyWav, sampleRate: 16000, durationSeconds: 0.1f);

            var psi = new ProcessStartInfo
            {
                FileName = binaryPath,
                Arguments = $"--model {modelName} --output_dir \"{tempDir}\" --output_format txt \"{dummyWav}\"" + (OperatingSystem.IsWindows() ? " 2>nul" : " 2>/dev/null"),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException($"Failed to start whisper binary: {binaryPath}");

            // Wait for the process to complete (model downloads then transcribes silence)
            try
            {
                await process.WaitForExitAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            }

            if (process.ExitCode == 0 && IsPythonModelCached(modelName))
            {
                progressCallback?.Invoke(modelName, "Download complete", null, null, true);
            }
            else
            {
                progressCallback?.Invoke(modelName,
                    IsPythonModelCached(modelName) ? "Download complete" : $"whisper exited with code {process.ExitCode}",
                    null, null, IsPythonModelCached(modelName));
            }
        }
        catch (OperationCanceledException)
        {
            try { File.Delete(dummyWav); } catch { /* best effort */ }
            throw;
        }
        catch (Exception ex)
        {
            progressCallback?.Invoke(modelName, $"Failed: {ex.Message}", null, null, false);
            throw;
        }
        finally
        {
            // Clean up the dummy WAV file
            try { File.Delete(dummyWav); } catch { /* best effort */ }
            try
            {
                var txtFile = Path.ChangeExtension(dummyWav, ".txt");
                if (File.Exists(txtFile)) File.Delete(txtFile);
            }
            catch { /* best effort */ }
            // Clean up the temp output directory if empty
            try
            {
                var txtDir = Path.Combine(tempDir, modelName);
                if (Directory.Exists(txtDir)) Directory.Delete(txtDir, recursive: true);
            }
            catch { /* best effort */ }
        }
    }

    /// <summary>Creates a minimal valid WAV file with silent audio.</summary>
    private static void CreateSilentWav(string path, int sampleRate, float durationSeconds)
    {
        var numChannels = (short)1;  // mono
        var bitsPerSample = (short)16;
        var bytesPerSample = bitsPerSample / 8;
        var blockAlign = (short)(numChannels * bytesPerSample);
        var byteRate = sampleRate * blockAlign;
        var dataSize = (int)(sampleRate * durationSeconds * blockAlign);
        if (dataSize < 4) dataSize = 4; // minimum data chunk
        var fileSize = 36 + dataSize;

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        // RIFF header
        bw.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
        bw.Write(fileSize);
        bw.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });

        // fmt chunk
        bw.Write(new[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
        bw.Write(16);           // chunk size
        bw.Write((short)1);     // PCM
        bw.Write(numChannels);
        bw.Write(sampleRate);
        bw.Write(byteRate);
        bw.Write(blockAlign);
        bw.Write(bitsPerSample);

        // data chunk (silence = all zeros)
        bw.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
        bw.Write(dataSize);
        bw.Write(new byte[dataSize]);
    }
}

/// <summary>Info about an available whisper model.</summary>
public sealed class WhisperModelInfo
{
    public string Name { get; }
    public string Description { get; }

    public WhisperModelInfo(string name, string description)
    {
        Name = name;
        Description = description;
    }
}

/// <summary>Type of whisper binary detected on the system.</summary>
public enum WhisperType { Python, Cpp }

/// <summary>Info about a detected whisper binary on the system.</summary>
public sealed class WhisperBinaryInfo
{
    public string Path { get; }
    public WhisperType Type { get; }
    public string DisplayType => Type == WhisperType.Python ? "openai-whisper (Python)" : "whisper.cpp (C++)";

    public WhisperBinaryInfo(string path, WhisperType type)
    {
        Path = path;
        Type = type;
    }
}
