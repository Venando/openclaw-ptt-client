using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OpenClawPTT.TTS.Providers;

/// <summary>
/// Python-based TTS provider with optional uv-managed Python environment.
/// When useUvManagement is enabled, uv is auto-downloaded and Python 3.11
/// is provisioned in an isolated venv with required packages.
/// </summary>
public sealed class PythonTtsProvider : ITextToSpeech, IDisposable
{
    /// <summary>
    /// Packages to install in the uv-managed venv.
    /// </summary>
    public static readonly string[] DefaultPackages = new[]
    {
        "typeguard==2.13.3",
        "coqui-tts",
        "torch"
    };

    private readonly PythonEnvironment? _pythonEnv;
    private readonly string? _pythonPath; // Fallback: direct Python path
    private readonly string? _ttsServiceScript;
    private readonly bool _useUvManagement;
    private Process? _ttsProcess;
    private readonly object _processLock = new();
    private bool _disposed;
    private bool _venvReady;

    public string ProviderName => "Python TTS (uv-managed)";

    public IReadOnlyList<string> AvailableVoices { get; } = new[] { "default" };

    public IReadOnlyList<string> AvailableModels { get; } = new[]
    {
        "coqui-tts", // Default — Coqui TTS multilingual
    };

    /// <summary>
    /// Creates a Python TTS provider with uv-managed Python environment.
    /// </summary>
    /// <param name="baseDir">Base directory for tools and venv</param>
    /// <param name="useUvManagement">If true, use uv to bootstrap Python</param>
    /// <param name="uvToolsPath">Optional explicit path to uv.exe</param>
    /// <param name="pythonVersion">Python version to use (default 3.11)</param>
    /// <param name="ttsServiceScript">Path to the Python TTS service script</param>
    public PythonTtsProvider(
        string baseDir,
        bool useUvManagement,
        string? uvToolsPath = null,
        string pythonVersion = "3.11",
        string? ttsServiceScript = null)
    {
        _useUvManagement = useUvManagement;
        _ttsServiceScript = ttsServiceScript;

        if (_useUvManagement)
        {
            string uvPath = uvToolsPath ?? Path.Combine(baseDir, "tools", "uv.exe");
            _pythonEnv = new PythonEnvironment(uvPath, pythonVersion, baseDir, ".venv");
            _pythonEnv.ProgressChanged += msg => ConsoleUi.PrintInfo($"[python-env] {msg}");
        }
        else
        {
            // Fallback to PATH-resolved Python
            _pythonPath = ResolvePythonFromPath();
        }
    }

    /// <summary>
    /// Creates a Python TTS provider with explicit Python path (legacy).
    /// </summary>
    public PythonTtsProvider(string pythonPath, string? ttsServiceScript = null)
    {
        _pythonPath = pythonPath ?? throw new ArgumentNullException(nameof(pythonPath));
        _ttsServiceScript = ttsServiceScript;
        _useUvManagement = false;
    }

    /// <summary>
    /// Initializes the provider: bootstraps uv if needed, creates venv, starts TTS subprocess.
    /// Call this before using SynthesizeAsync.
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_useUvManagement && _pythonEnv != null && !_venvReady)
        {
            // Step 1: Bootstrap uv
            var bootstrapper = new UvBootstrapper(Path.GetDirectoryName(_pythonEnv.VenvPath) ?? throw new InvalidOperationException("VenvPath has no directory"));
            bootstrapper.ProgressChanged += msg => ConsoleUi.PrintInfo($"[uv-bootstrapper] {msg}");

            string uvPath = await bootstrapper.EnsureUvInstalledAsync(ct);
            ConsoleUi.PrintSuccess($"uv ready at: {uvPath}");

            // Re-create PythonEnvironment with the resolved uv path
            var venvDir = Path.GetDirectoryName(_pythonEnv.VenvPath) ?? throw new InvalidOperationException("VenvPath has no directory");
            var venvName = Path.GetFileName(_pythonEnv.VenvPath);
            var pyEnv2 = new PythonEnvironment(uvPath, "3.11", venvDir, venvName);
            pyEnv2.ProgressChanged += msg => ConsoleUi.PrintInfo($"[python-env] {msg}");

            // Step 2: Ensure venv exists with packages
            await pyEnv2.EnsureVenvExistsAsync(DefaultPackages, ct);
            _venvReady = true;

            // Step 3: Start TTS subprocess
            StartTtsProcess(pyEnv2.PythonPath);
        }
        else if (!_useUvManagement && _pythonPath != null)
        {
            StartTtsProcess(_pythonPath);
        }
    }

    private void StartTtsProcess(string pythonPath)
    {
        lock (_processLock)
        {
            if (_ttsProcess != null) return;

            if (string.IsNullOrEmpty(_ttsServiceScript))
            {
                // No script provided — provider can still be used for subprocess-less synthesis
                return;
            }

            var psi = new ProcessStartInfo
            {
                FileName = pythonPath,
                Arguments = $"\"{_ttsServiceScript}\"",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            _ttsProcess = Process.Start(psi);
            if (_ttsProcess == null)
                throw new InvalidOperationException($"Failed to start TTS process: {pythonPath} {_ttsServiceScript}");
        }
    }

    public async Task<byte[]> SynthesizeAsync(string text, string? voice = null, string? model = null, CancellationToken ct = default)
    {
        if (_ttsProcess == null || _ttsProcess.HasExited)
            throw new InvalidOperationException("TTS process not running. Call InitializeAsync first.");

        // Write text to stdin, read audio from stdout
        // Protocol: write JSON with text/voice/model, read raw WAV bytes
        var request = new
        {
            text = text,
            voice = voice ?? "default",
            model = model ?? "coqui-tts"
        };

        var json = System.Text.Json.JsonSerializer.Serialize(request);

        lock (_processLock)
        {
            if (_ttsProcess == null || _ttsProcess.HasExited)
                throw new InvalidOperationException("TTS process has exited");
            _ttsProcess.StandardInput.WriteLine(json);
            _ttsProcess.StandardInput.Flush();
        }

        // Read response — in a real implementation, read the audio bytes
        // For now, read stderr for logging and return empty
        var error = await _ttsProcess.StandardError.ReadLineAsync(ct);
        if (!string.IsNullOrEmpty(error))
            ConsoleUi.PrintWarning($"[tts-service] {error}");

        // Read audio bytes (implementation depends on the tts_service.py protocol)
        using var ms = new MemoryStream();
        var buffer = new byte[8192];
        int bytesRead;
        // Read until process signals end of audio (e.g., empty line or sentinel)
        while ((bytesRead = await _ttsProcess.StandardOutput.BaseStream.ReadAsync(buffer, ct)) > 0)
        {
            if (bytesRead == 1 && buffer[0] == 0xFF) break; // End sentinel
            await ms.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
        }

        return ms.ToArray();
    }

    private static string ResolvePythonFromPath()
    {
        // Try to find Python in PATH
        var pythonNames = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new[] { "python.exe", "python3.exe" }
            : new[] { "python3", "python" };

        foreach (var name in pythonNames)
        {
            try
            {
                var path = new ProcessStartInfo
                {
                    FileName = name,
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using var p = Process.Start(path);
                if (p != null)
                {
                    p.WaitForExit(2000);
                    if (p.ExitCode == 0)
                        return name;
                }
            }
            catch { /* try next */ }
        }

        throw new InvalidOperationException("Python not found in PATH");
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            lock (_processLock)
            {
                if (_ttsProcess != null && !_ttsProcess.HasExited)
                {
                    try { _ttsProcess.Kill(true); } catch { /* ignore */ }
                    _ttsProcess.Dispose();
                    _ttsProcess = null;
                }
            }
            _pythonEnv?.Dispose();
            _disposed = true;
        }
    }
}
