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
/// </summary>
public sealed class UvBootstrapper
{
    private static readonly HttpClient _httpClient = new HttpClient();

    /// <summary>
    /// Base URL for uv releases (latest redirect — no version pinning needed)
    /// </summary>
    private const string UvReleaseBaseUrl = "https://github.com/astral-sh/uv/releases/latest/download";

    /// <summary>
    /// Progress event for download/extraction progress.
    /// </summary>
    public event Action<string>? ProgressChanged;

    private readonly string _baseDir;

    /// <summary>
    /// Creates a UvBootstrapper.
    /// </summary>
    /// <param name="baseDir">Base directory for tools (e.g., app data folder). uv.exe will be placed in {baseDir}/tools/</param>
    public UvBootstrapper(string baseDir)
    {
        _baseDir = baseDir;
    }

    /// <summary>
    /// Resolves the full download URL for the current OS + architecture.
    /// </summary>
    public static string ResolveDownloadUrl()
    {
        string osPart = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) switch
        {
            true when RuntimeInformation.OSArchitecture == Architecture.X64 => "x86_64-pc-windows-msvc",
            _ => throw new PlatformNotSupportedException($"Unsupported platform: {RuntimeInformation.OSArchitecture}")
        };

        string archiveName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? $"{osPart}.zip"
            : $"{osPart}.tar.gz";

        return $"{UvReleaseBaseUrl}/{archiveName}";
    }

    /// <summary>
    /// Ensures uv.exe is installed locally. Downloads and extracts if missing.
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Absolute path to uv.exe</returns>
    public async Task<string> EnsureUvInstalledAsync(CancellationToken ct = default)
    {
        string toolsDir = Path.Combine(_baseDir, "tools");
        string uvPath = Path.Combine(toolsDir, "uv.exe");

        if (File.Exists(uvPath))
        {
            // Basic sanity: check file size (~15MB for uv)
            var info = new FileInfo(uvPath);
            if (info.Length > 5_000_000)
                return uvPath;

            // Corrupt or too small — delete and re-download
            ProgressChanged?.Invoke("Existing uv.exe appears corrupt, re-downloading...");
            File.Delete(uvPath);
        }

        // Create tools directory
        Directory.CreateDirectory(toolsDir);

        string url = ResolveDownloadUrl();
        ProgressChanged?.Invoke($"Downloading uv from {url}...");

        string tempArchive = Path.Combine(Path.GetTempPath(), $"uv_archive_{Guid.NewGuid()}{Path.GetExtension(url)}");

        try
        {
            // Download with progress
            using (var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                using var contentStream = await response.Content.ReadAsStreamAsync(ct);
                using var fileStream = new FileStream(tempArchive, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[8192];
                long totalRead = 0;
                int bytesRead;
                while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    totalRead += bytesRead;
                    if (totalBytes > 0)
                    {
                        var percent = (int)(totalRead * 100 / totalBytes);
                        ProgressChanged?.Invoke($"Downloading uv... {percent}%");
                    }
                }
            }

            ProgressChanged?.Invoke("Extracting uv...");

            // Extract using SharpCompress (cross-platform, handles zip, tar, tar.gz)
            string extractedUv = ExtractArchiveCrossPlatform(tempArchive);

            if (!File.Exists(extractedUv))
                throw new InvalidOperationException($"Failed to extract uv from archive");

            // Ensure executable bit on Unix
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var fileInfo = new FileInfo(extractedUv);
                fileInfo.UnixFileMode |= UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            }

            // Move to tools/
            File.Copy(extractedUv, uvPath, overwrite: true);
            ProgressChanged?.Invoke($"uv installed at: {uvPath}");

            return uvPath;
        }
        finally
        {
            // Cleanup temp files
            if (File.Exists(tempArchive)) File.Delete(tempArchive);
            CleanupTempExtracted(Path.GetTempPath());
        }
    }

    private static string ExtractArchiveCrossPlatform(string archivePath)
    {
        string tempDir = Path.GetTempPath();

        // SharpCompress handles zip, tar, tar.gz, .tar.bz2, etc. cross-platform
        using var archive = ArchiveFactory.Open(archivePath);
        archive.WriteToDirectory(tempDir, new ExtractionOptions
        {
            ExtractFullPath = false,
            Overwrite = true,
        });

        return FindExtractedUv(tempDir);
    }

    private static string FindExtractedUv(string tempDir)
    {
        // uv archives typically extract to a directory or directly contain uv
        string directUv = Path.Combine(tempDir, "uv");
        if (File.Exists(directUv)) return directUv;

        // Check subdirectories for uv
        foreach (var dir in Directory.GetDirectories(tempDir))
        {
            string name = Path.GetFileName(dir);
            if (name.StartsWith("uv-"))
            {
                string subUv = Path.Combine(dir, "uv");
                if (File.Exists(subUv)) return subUv;
                string subUvExe = Path.Combine(dir, "uv.exe");
                if (File.Exists(subUvExe)) return subUvExe;
            }
        }

        return Path.Combine(tempDir, "uv");
    }

    private static void CleanupTempExtracted(string tempDir)
    {
        try
        {
            foreach (var dir in Directory.GetDirectories(tempDir))
            {
                string name = Path.GetFileName(dir);
                if (name.StartsWith("uv-"))
                {
                    try { Directory.Delete(dir, true); } catch { /* ignore */ }
                }
            }
            string directUv = Path.Combine(tempDir, "uv");
            try { if (File.Exists(directUv)) File.Delete(directUv); } catch { /* ignore */ }
            try { if (File.Exists(directUv + ".exe")) File.Delete(directUv + ".exe"); } catch { /* ignore */ }
        }
        catch { /* ignore cleanup errors */ }
    }

    /// <summary>
    /// Shows a prompt to the user asking for confirmation before downloading.
    /// Returns true if the user accepts, false otherwise.
    /// </summary>
    public static bool PromptUserForDownload(string message)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  ⚠ {message}");
        Console.ResetColor();
        Console.Write("  Download now? [Y/n]: ");
        var key = Console.ReadKey(intercept: true);
        Console.WriteLine();
        return key.Key != ConsoleKey.N;
    }

    /// <summary>
    /// Checks available disk space. Returns true if there's enough space.
    /// </summary>
    /// <param name="requiredBytes">Required bytes</param>
    public static bool HasEnoughDiskSpace(string path, long requiredBytes)
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(path) ?? path);
            return drive.AvailableFreeSpace >= requiredBytes;
        }
        catch
        {
            return true; // Can't check — assume OK
        }
    }
}
