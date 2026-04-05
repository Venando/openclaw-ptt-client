using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace OpenClawPTT.TTS.Providers;

/// <summary>
/// Manages a Python virtual environment created and maintained via uv.
/// Handles venv creation, package installation, and provides the Python executable path.
/// </summary>
public sealed class PythonEnvironment : IDisposable
{
    private readonly string _uvPath;
    private readonly string _pythonVersion;
    private readonly string _baseDir;
    private readonly string _venvName;
    private readonly string _venvPath;
    private bool _disposed;

    /// <summary>
    /// Progress event for venv creation and package installation.
    /// </summary>
    public event Action<string>? ProgressChanged;

    /// <summary>
    /// Full path to the virtual environment directory.
    /// </summary>
    public string VenvPath => _venvPath;

    /// <summary>
    /// Full path to the Python executable inside the venv.
    /// </summary>
    public string PythonPath => GetPythonPath();

    /// <summary>
    /// Creates a PythonEnvironment manager.
    /// </summary>
    /// <param name="uvPath">Path to the uv executable</param>
    /// <param name="pythonVersion">Python version to use (e.g., "3.11")</param>
    /// <param name="baseDir">Base directory where the venv will be created</param>
    /// <param name="venvName">Name of the venv directory</param>
    public PythonEnvironment(string uvPath, string pythonVersion, string baseDir, string venvName)
    {
        _uvPath = uvPath ?? throw new ArgumentNullException(nameof(uvPath));
        _pythonVersion = pythonVersion ?? throw new ArgumentNullException(nameof(pythonVersion));
        _baseDir = baseDir ?? throw new ArgumentNullException(nameof(baseDir));
        _venvName = venvName ?? throw new ArgumentNullException(nameof(venvName));
        _venvPath = Path.Combine(_baseDir, _venvName);
    }

    /// <summary>
    /// Ensures the venv exists with all required packages installed.
    /// Creates the venv if it doesn't exist, then installs packages.
    /// </summary>
    /// <param name="packages">Packages to install via uv pip</param>
    /// <param name="ct">Cancellation token</param>
    public async Task EnsureVenvExistsAsync(string[] packages, CancellationToken ct = default)
    {
        // Ensure base directory exists
        Directory.CreateDirectory(_baseDir);

        // Create venv if it doesn't exist
        if (!Directory.Exists(_venvPath))
        {
            ProgressChanged?.Invoke($"Creating Python {_pythonVersion} venv at {_venvPath}...");
            await CreateVenvAsync(ct);
        }
        else
        {
            ProgressChanged?.Invoke($"Using existing venv at {_venvPath}");
        }

        // Install packages
        if (packages.Length > 0)
        {
            ProgressChanged?.Invoke($"Installing packages: {string.Join(", ", packages)}...");
            await InstallPackagesAsync(packages, ct);
        }

        ProgressChanged?.Invoke("Virtual environment ready.");
    }

    private async Task CreateVenvAsync(CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _uvPath,
            Arguments = $"venv \"{_venvPath}\" --python {_pythonVersion}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start uv venv process");
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync(ct);
            throw new InvalidOperationException($"Failed to create venv: {error}");
        }

        ProgressChanged?.Invoke($"Venv created successfully.");
    }

    private async Task InstallPackagesAsync(string[] packages, CancellationToken ct)
    {
        var packageList = string.Join(" ", packages);
        var psi = new ProcessStartInfo
        {
            FileName = _uvPath,
            Arguments = $"pip install --python \"{_venvPath}\" {packageList}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start uv pip install process");
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync(ct);
            throw new InvalidOperationException($"Failed to install packages: {error}");
        }

        ProgressChanged?.Invoke("Packages installed successfully.");
    }

    private string GetPythonPath()
    {
        // Unix: _venvPath/bin/python
        // Windows: _venvPath\Scripts\python.exe
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Path.Combine(_venvPath, "Scripts", "python.exe");
        }
        else
        {
            return Path.Combine(_venvPath, "bin", "python");
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            // PythonEnvironment doesn't own the venv directory —
            // disposal is handled at a higher level or by the caller
            _disposed = true;
        }
    }
}
