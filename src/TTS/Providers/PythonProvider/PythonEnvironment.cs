using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace OpenClawPTT.TTS.Providers;

/// <summary>
/// Manages a Python virtual environment created and managed by uv.
/// Encapsulates venv creation, dependency installation, and Python path resolution.
/// </summary>
public sealed class PythonEnvironment : IDisposable
{
    /// <summary>
    /// Progress callback for venv creation and pip install steps.
    /// </summary>
    public event Action<string>? ProgressChanged;

    private readonly string _uvBinPath;
    private readonly string _pythonVersion;
    private readonly string _venvPath;
    private readonly string _pythonPath;
    private bool _disposed;

    // Legacy fields for backward compatibility with existing PythonTtsProvider
    private readonly string? _modelName;
    private readonly string? _modelPath;
    private readonly string? _ttsConfigPath;
    private readonly string? _espeakNgPath;

    private static bool s_extracted;
    private static readonly string s_scriptFolder = Path.Combine(Path.GetTempPath(), "openclaw-ptt-tts", "scripts");
    private static readonly string s_mainScript = Path.Combine(s_scriptFolder, "tts_service.py");

    /// <summary>
    /// Creates a PythonEnvironment that uses uv to manage the venv.
    /// </summary>
    /// <param name="uvBinPath">Absolute path to the uv.exe binary</param>
    /// <param name="pythonVersion">Python version string (e.g. "3.11")</param>
    /// <param name="baseDir">Base directory where the .venv will be created</param>
    /// <param name="venvName">Name of the venv directory (default: ".venv")</param>
    public PythonEnvironment(string uvBinPath, string pythonVersion, string baseDir, string venvName = ".venv")
    {
        if (string.IsNullOrEmpty(uvBinPath)) throw new ArgumentNullException(nameof(uvBinPath));
        if (string.IsNullOrEmpty(pythonVersion)) throw new ArgumentNullException(nameof(pythonVersion));

        _uvBinPath = uvBinPath;
        _pythonVersion = pythonVersion;
        _venvPath = Path.Combine(baseDir, venvName);

        // Resolve Python executable path based on OS
        _pythonPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Path.Combine(_venvPath, "Scripts", "python.exe")
            : Path.Combine(_venvPath, "bin", "python");

        _modelName = null;
        _modelPath = null;
        _ttsConfigPath = null;
        _espeakNgPath = null;
    }

    /// <summary>
    /// Creates a PythonEnvironment using the old PATH-based Python resolution (legacy constructor).
    /// </summary>
    /// <param name="pythonPath">Path to Python executable or directory</param>
    /// <param name="modelName">Model name</param>
    /// <param name="modelPath">Model path</param>
    /// <param name="ttsConfigPath">TTS config path</param>
    /// <param name="espeakNgPath">eSpeak NG path</param>
    public PythonEnvironment(string pythonPath, string modelName, string? modelPath, string? ttsConfigPath, string? espeakNgPath)
    {
        _pythonPath = pythonPath ?? throw new ArgumentNullException(nameof(pythonPath));
        _venvPath = Path.Combine(Path.GetTempPath(), "openclaw-ptt-tts");
        _uvBinPath = string.Empty;
        _pythonVersion = string.Empty;
        _modelName = modelName;
        _modelPath = modelPath;
        _ttsConfigPath = ttsConfigPath;
        _espeakNgPath = espeakNgPath;
    }

    /// <summary>
    /// Full path to the venv directory.
    /// </summary>
    public string VenvPath => _venvPath;

    /// <summary>
    /// Full path to the Python executable within the venv.
    /// </summary>
    public string PythonPath => _pythonPath;

    /// <summary>
    /// True if the venv already exists on disk.
    /// </summary>
    public bool VenvExists => Directory.Exists(_venvPath) && File.Exists(_pythonPath);

    /// <summary>
    /// Resolves the Python executable path, checking explicit path first, then PATH environment variable.
    /// Used by legacy code path.
    /// </summary>
    public string ResolvePython()
    {
        if (!string.IsNullOrEmpty(_pythonPath))
        {
            var pythonExe = Path.Combine(_pythonPath, "python.exe");
            if (File.Exists(pythonExe))
                return pythonExe;
            pythonExe = Path.Combine(_pythonPath, "Scripts", "python.exe");
            if (File.Exists(pythonExe))
                return pythonExe;
        }

        var envPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in envPath.Split(Path.PathSeparator))
        {
            var exe = Path.Combine(dir, "python.exe");
            if (File.Exists(exe))
                return exe;
        }

        return "python";
    }

    /// <summary>
    /// Gets the script path, extracting embedded scripts if no override is provided.
    /// </summary>
    public string GetScriptPath(string? overridePath)
    {
        if (!string.IsNullOrEmpty(overridePath))
            return overridePath;
        ExtractAllScripts();
        return s_mainScript;
    }

    /// <summary>
    /// Creates a configured ProcessStartInfo for launching the TTS service script.
    /// </summary>
    public ProcessStartInfo CreateProcessStartInfo(string? scriptPathOverride)
    {
        var pythonExe = ResolvePython();
        var psi = new ProcessStartInfo
        {
            FileName = pythonExe,
            Arguments = $"-u \"{GetScriptPath(scriptPathOverride)}\"",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
        };
        ApplyEnvironmentVariables(psi);
        return psi;
    }

    /// <summary>
    /// Applies Python environment variables to the process start info.
    /// </summary>
    private void ApplyEnvironmentVariables(ProcessStartInfo psi)
    {
        // Force CPU-only mode — avoids CUDA initialization overhead and potential driver issues
        psi.Environment["CUDA_VISIBLE_DEVICES"] = "";
        if (!string.IsNullOrEmpty(_espeakNgPath))
            psi.Environment["PATH"] = _espeakNgPath + ";" + psi.Environment["PATH"];
        if (!string.IsNullOrEmpty(_modelName))
            psi.Environment["TTS_MODEL"] = _modelName;
        if (!string.IsNullOrEmpty(_modelPath))
            psi.Environment["TTS_MODEL_PATH"] = _modelPath;
        if (!string.IsNullOrEmpty(_ttsConfigPath))
            psi.Environment["TTS_CONFIG_PATH"] = _ttsConfigPath;
    }

    private static void ExtractAllScripts()
    {
        if (s_extracted)
            return;

        var asm = System.Reflection.Assembly.GetExecutingAssembly();
        // RootNamespace from csproj is OpenClawPTT — resource names use it, not the assembly name
        var resourcePrefix = "OpenClawPTT.scripts.";
        var allResources = asm.GetManifestResourceNames().ToList();
        var resourceNames = allResources.Where(r => r.StartsWith(resourcePrefix)).ToList();

        foreach (var resourceName in resourceNames)
        {
            string relativePath = resourceName.Substring(resourcePrefix.Length);
            string outputPath = Path.Combine(s_scriptFolder, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            using var stream = asm.GetManifestResourceStream(resourceName)!;
            using var file = File.Create(outputPath);
            stream.CopyTo(file);
        }

        s_extracted = true;
    }

    /// <summary>
    /// Ensures the venv exists. Creates it if missing, installing required packages.
    /// </summary>
    /// <param name="packages">Packages to install via uv pip install</param>
    /// <param name="ct">Cancellation token</param>
    public async Task EnsureVenvExistsAsync(string[] packages, CancellationToken ct = default)
    {
        if (VenvExists)
        {
            ProgressChanged?.Invoke("venv already exists, skipping creation.");
            return;
        }

        if (string.IsNullOrEmpty(_uvBinPath))
            throw new InvalidOperationException("uvBinPath is required to create a venv");

        // Step 1: Create venv
        ProgressChanged?.Invoke($"Creating venv with Python {_pythonVersion}...");
        await RunUvCommandAsync($"venv --python {_pythonVersion}", ct);

        // Step 2: Install packages
        foreach (var pkg in packages)
        {
            ProgressChanged?.Invoke($"Installing {pkg}...");
            await RunUvCommandAsync($"pip install \"{pkg}\"", ct);
        }

        ProgressChanged?.Invoke("venv ready.");
    }

    /// <summary>
    /// Runs a uv command and waits for it to complete.
    /// </summary>
    private async Task RunUvCommandAsync(string arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _uvBinPath,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(_venvPath) ?? _venvPath
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start uv process: {psi.FileName} {arguments}");

        string output = await process.StandardOutput.ReadToEndAsync(ct);
        string error = await process.StandardError.ReadToEndAsync(ct);

        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"uv {arguments} failed (exit {process.ExitCode}): {error}");
        }

        if (!string.IsNullOrWhiteSpace(output))
            ProgressChanged?.Invoke(output.Trim());
    }

    /// <summary>
    /// Runs a Python script using this environment's Python.
    /// Returns the process exit code.
    /// </summary>
    public async Task<int> RunPythonAsync(string scriptPath, string arguments, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _pythonPath,
            Arguments = $"\"{scriptPath}\" {arguments}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start Python: {_pythonPath}");
        await process.WaitForExitAsync(ct);
        return process.ExitCode;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }
}