using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace OpenClawPTT.TTS.Providers;

/// <summary>
/// Manages Python environment setup: script extraction, Python resolution, and environment variables.
/// </summary>
public sealed class PythonEnvironment
{
    private readonly string _pythonPath;
    private readonly string _modelName;
    private readonly string? _modelPath;
    private readonly string? _ttsConfigPath;
    private readonly string? _espeakNgPath;

    // uv-managed fields (null = legacy mode)
    public string? UvPath { get; private init; }
    public string? PythonVersion { get; private init; }
    public string? BaseDir { get; private init; }
    public string? VenvDirName { get; private init; }
    public string? TtsServiceScript { get; private init; }

    private static bool s_extracted;
    private static readonly string s_scriptFolder = Path.Combine(Path.GetTempPath(), "openclaw-ptt-tts", "scripts");
    private static readonly string s_mainScript = Path.Combine(s_scriptFolder, "tts_service.py");

    /// <summary>
    /// Creates a legacy Coqui/provider-based Python environment.
    /// </summary>
    public PythonEnvironment(string pythonPath = "", string modelName = "", string? modelPath = null, string? ttsConfigPath = null, string? espeakNgPath = null)
    {
        _pythonPath = pythonPath;
        _modelName = modelName;
        _modelPath = modelPath;
        _ttsConfigPath = ttsConfigPath;
        _espeakNgPath = espeakNgPath;
    }

    /// <summary>
    /// Creates a uv-managed Python environment with auto-provisioning.
    /// </summary>
    public static PythonEnvironment CreateUvManaged(string uvPath, string pythonVersion, string baseDir, string venvDirName, string? ttsServiceScript = null)
        => new PythonEnvironment
        {
            UvPath = uvPath,
            PythonVersion = pythonVersion,
            BaseDir = baseDir,
            VenvDirName = venvDirName,
            TtsServiceScript = ttsServiceScript
        };

    /// <summary>
    /// Path to the venv directory for uv-managed environments.
    /// </summary>
    public string VenvPath => BaseDir != null && VenvDirName != null
        ? Path.Combine(BaseDir, VenvDirName)
        : throw new InvalidOperationException("VenvPath not available in legacy mode");

    /// <summary>
    /// Path to the Python executable inside the venv or resolved from legacy path.
    /// </summary>
    public string PythonPath
    {
        get
        {
            if (BaseDir != null && VenvDirName != null)
            {
                var venvPython = Path.Combine(VenvPath, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Scripts" : "bin", "python");
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    venvPython += ".exe";
                return venvPython;
            }
            return ResolvePython();
        }
    }

    /// <summary>
    /// Progress event for uv-bootstrap and venv-creation operations.
    /// </summary>
    public event Action<string>? ProgressChanged;

    /// <summary>
    /// Ensures the venv exists, creating it if necessary and installing packages.
    /// </summary>
    public async Task EnsureVenvExistsAsync(string[] packages, CancellationToken ct = default)
    {
        if (BaseDir == null || UvPath == null || PythonVersion == null)
            throw new InvalidOperationException("uv-managed environment not initialized");

        if (Directory.Exists(VenvPath))
        {
            ProgressChanged?.Invoke($"venv already exists at {VenvPath}");
            return;
        }

        ProgressChanged?.Invoke($"Creating venv with Python {PythonVersion}...");
        var psi = new ProcessStartInfo
        {
            FileName = UvPath,
            Arguments = $"venv \"{VenvPath}\" --python {PythonVersion}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = BaseDir
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start uv venv process");
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            var err = await process.StandardError.ReadToEndAsync(ct);
            throw new InvalidOperationException($"uv venv creation failed: {err}");
        }

        ProgressChanged?.Invoke("venv created. Installing packages...");
        await InstallPackagesAsync(packages, ct);
    }

    private async Task InstallPackagesAsync(string[] packages, CancellationToken ct = default)
    {
        if (BaseDir == null || UvPath == null)
            return;

        var psi = new ProcessStartInfo
        {
            FileName = UvPath,
            Arguments = $"pip install --python \"{PythonPath}\" " + string.Join(" ", packages.Select(p => $"\"{p}\"")),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = BaseDir
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start uv pip install");
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            var err = await process.StandardError.ReadToEndAsync(ct);
            throw new InvalidOperationException($"uv pip install failed: {err}");
        }

        ProgressChanged?.Invoke("Packages installed.");
    }

    /// <summary>
    /// Resolves the Python executable path, checking explicit path first, then PATH.
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
        var pythonExe = PythonPath;
        var psi = new ProcessStartInfo
        {
            FileName = pythonExe,
            Arguments = $"-u \"{GetScriptPath(scriptPathOverride)}\"",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        ApplyEnvironmentVariables(psi);
        return psi;
    }

    /// <summary>
    /// Applies Python environment variables to the process start info.
    /// </summary>
    private void ApplyEnvironmentVariables(ProcessStartInfo psi)
    {
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

        var asm = Assembly.GetExecutingAssembly();
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
}
