using System;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace OpenClawPTT.TTS.Providers;

/// <summary>
/// Handles uv.exe lifecycle: download, extraction, and version management.
/// uv is treated as a private tool bundled with the app (not a system dependency).
/// Called via local absolute path, never delegated to PATH.
/// </summary>
public sealed class UvBootstrapper
{
    private static readonly HttpClient _httpClient = new HttpClient();

    private const string UvReleaseBaseUrl = "https://github.com/astral-sh/uv/releases/latest/download";

    /// <summary>
    /// Fires when download/extraction progress changes. Message describes current step.
    /// </summary>
    public event Action<string>? ProgressChanged;

    private readonly string _baseDir;

    public UvBootstrapper(string baseDir)
    {
        _baseDir = baseDir;
    }

    /// <summary>
    /// Returns the full download URL for the current OS + architecture.
    /// Throws PlatformNotSupportedException if the platform is not supported.
    /// </summary>
    public static string ResolveDownloadUrl()
    {
        string? detectedOS = null;
        string? detectedArch = null;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            detectedOS = "Windows";
            detectedArch = RuntimeInformation.OSArchitecture switch
            {
                Architecture.X64 => "x86_64",
                Architecture.Arm64 => "aarch64",
                _ => null
            };
            if (detectedArch != null)
                return $"{UvReleaseBaseUrl}/{detectedArch}-pc-windows-msvc.zip";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            detectedOS = "Linux";
            detectedArch = RuntimeInformation.OSArchitecture switch
            {
                Architecture.X64 => "x86_64-unknown-linux-gnu.tar.gz",
                Architecture.Arm64 => "aarch64-unknown-linux-gnu.tar.gz",
                _ => null
            };
            if (detectedArch != null) return $"{UvReleaseBaseUrl}/{detectedArch}";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            detectedOS = "macOS";
            detectedArch = RuntimeInformation.OSArchitecture switch
            {
                Architecture.X64 => "x86_64-apple-darwin.tar.gz",
                Architecture.Arm64 => "aarch64-apple-darwin.tar.gz",
                _ => null
            };
            if (detectedArch != null) return $"{UvReleaseBaseUrl}/{detectedArch}";
        }

        throw new PlatformNotSupportedException(
            $"Unsupported OS/arch: {detectedOS ?? "Unknown"}, {RuntimeInformation.OSArchitecture}");
    }

    /// <summary>
    /// Ensures uv.exe is present locally. Downloads and extracts if missing or corrupt.
    /// Returns the absolute path to uv.exe.
    /// </summary>
    public async Task<string> EnsureUvInstalledAsync(CancellationToken ct = default)
    {
        string toolsDir = Path.Combine(_baseDir, "tools");
        string uvPath = Path.Combine(toolsDir, "uv.exe");
        Directory.CreateDirectory(toolsDir);

        // Sanity check existing binary (~15MB for uv)
        if (File.Exists(uvPath) && new FileInfo(uvPath).Length > 5_000_000)
            return uvPath;

        // User prompt before downloading
        if (!PromptUser("Python 3.11 and dependencies (~5GB) will be downloaded. Continue?"))
            throw new InvalidOperationException("User declined Python download.");

        // Check available disk space
        const long estimatedBytes = 5L * 1024 * 1024 * 1024; // ~5GB
        if (!HasEnoughSpace(_baseDir, estimatedBytes))
            throw new InvalidOperationException("Insufficient disk space for Python download. Free up space and try again.");

        // Need to download
        string url = ResolveDownloadUrl();
        ProgressChanged?.Invoke($"Downloading uv from {url}...");

        bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        string extension = isWindows ? ".zip" : ".tar.gz";
        string tempArchive = Path.Combine(Path.GetTempPath(), $"uv_archive_{Guid.NewGuid()}{extension}");

        try
        {
            await DownloadFileAsync(url, tempArchive, ct);

            ProgressChanged?.Invoke("Extracting uv...");

            // Extract archive
            string extractedUv = Path.Combine(Path.GetTempPath(), isWindows ? "uv.exe" : "uv");

            using var archive = ArchiveFactory.Open(tempArchive);
            foreach (var entry in archive.Entries)
            {
                string name = Path.GetFileName(entry.Key);
                if (entry.IsDirectory || (name != "uv" && name != "uv.exe"))
                    continue;
                entry.WriteToFile(extractedUv, new ExtractionOptions { ExtractFullPath = false, Overwrite = true });
            }

            if (!File.Exists(extractedUv))
                throw new InvalidOperationException($"uv binary not found after extraction");

            // Unix: set executable bit
            if (!isWindows)
            {
                var fi = new FileInfo(extractedUv);
                fi.UnixFileMode |= UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            }

            File.Copy(extractedUv, uvPath, overwrite: true);
            ProgressChanged?.Invoke($"uv installed: {uvPath}");

            // Cleanup extracted binary (keep archive for now)
            try { File.Delete(extractedUv); } catch { /* ignore */ }

            return uvPath;
        }
        finally
        {
            try { File.Delete(tempArchive); } catch { /* ignore */ }
        }
    }

    private async Task DownloadFileAsync(string url, string destPath, CancellationToken ct)
    {
        const int maxRetries = 3;
        int attempt = 0;
        while (true)
        {
            try
            {
                using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;

                await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
                await using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[8192];
                long totalRead = 0;
                int bytesRead;
                while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    totalRead += bytesRead;
                    if (totalBytes > 0)
                    {
                        int percent = (int)(totalRead * 100 / totalBytes);
                        ProgressChanged?.Invoke($"Downloading uv... {percent}%");
                    }
                }
                return;
            }
            catch (Exception ex) when (attempt < maxRetries - 1 &&
                (ex is HttpRequestException ||
                 ex is OperationCanceledException))
            {
                attempt++;
                int delayMs = (int)(500 * Math.Pow(2, attempt));
                ProgressChanged?.Invoke($"Download failed (attempt {attempt}), retrying in {delayMs}ms...");
                await Task.Delay(delayMs, ct);
            }
        }
    }

    /// <summary>
    /// Shows a console prompt and returns the user's answer.
    /// </summary>
    public static bool PromptUser(string message)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  ⚠ {message}");
        Console.ResetColor();
        Console.Write("  Proceed? [Y/n]: ");
        var key = Console.ReadKey(intercept: true);
        Console.WriteLine();
        return key.Key != ConsoleKey.N;
    }

    /// <summary>
    /// Checks if there's enough free space at the given path.
    /// </summary>
    public static bool HasEnoughSpace(string path, long requiredBytes)
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(path) ?? path);
            return drive.AvailableFreeSpace >= requiredBytes;
        }
        catch { return true; } // If we can't check, assume OK
    }
}
