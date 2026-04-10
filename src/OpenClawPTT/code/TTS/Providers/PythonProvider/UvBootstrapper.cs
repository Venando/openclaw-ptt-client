using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace OpenClawPTT.TTS.Providers;

/// <summary>
/// Bootstraps uv: downloads it if not present, verifies it works.
/// </summary>
public sealed class UvBootstrapper
{
    private readonly string _toolsDir;

    public event Action<string>? ProgressChanged;

    public UvBootstrapper(string toolsDir)
    {
        _toolsDir = toolsDir;
    }

    /// <summary>
    /// Returns path to uv.exe, downloading it if necessary.
    /// </summary>
    public async Task<string> EnsureUvInstalledAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(_toolsDir);
        var uvExe = Path.Combine(_toolsDir, "uv.exe");

        if (File.Exists(uvExe))
        {
            ProgressChanged?.Invoke("uv already present");
            return uvExe;
        }

        ProgressChanged?.Invoke("Downloading uv...");

        try
        {
            using var client = new System.Net.Http.HttpClient();
            client.Timeout = TimeSpan.FromMinutes(5);

            var bootstrapUrl = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "https://astral.sh/uv/install.ps1"
                : "https://astral.sh/uv/install.sh";

            ProgressChanged?.Invoke($"Fetching installer from {bootstrapUrl}");

            var script = await client.GetStringAsync(bootstrapUrl, ct);
            var installerPath = Path.Combine(Path.GetTempPath(), $"uv-install-{Guid.NewGuid()}.ps1");
            await File.WriteAllTextAsync(installerPath, script, ct);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = $"-ExecutionPolicy Bypass -File \"{installerPath}\" -D \"{_toolsDir}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start uv installer");
                await process.WaitForExitAsync(ct);
            }
            else
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "sh",
                    Arguments = $"\"{installerPath}\" -d \"{_toolsDir}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start uv installer");
                await process.WaitForExitAsync(ct);
            }

            if (!File.Exists(uvExe))
                throw new InvalidOperationException($"uv installation failed: {uvExe} not found after install");

            ProgressChanged?.Invoke("uv installed successfully");
            return uvExe;
        }
        finally
        {
            // Cleanup temp installer
            var tmpFiles = Directory.GetFiles(Path.GetTempPath(), "uv-install-*.ps1");
            foreach (var f in tmpFiles)
                try { File.Delete(f); } catch { }
        }
    }
}
