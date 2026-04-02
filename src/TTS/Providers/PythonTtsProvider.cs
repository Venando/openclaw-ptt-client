using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace OpenClawPTT.TTS.Providers;

/// <summary>
/// Python TTS provider using a long-running tts_service.py subprocess.
/// </summary>
public sealed class PythonTtsProvider : ITextToSpeech, IAsyncDisposable
{
    private readonly string _pythonPath;
    private readonly string _modelPath;
    private readonly string _modelName;
    private readonly string _serviceScriptPath;

    /*
    
    TODO: LOAD from memory

If you want extra confidence, just embed the script as a resource instead of a loose file:
xml<ItemGroup>
  <EmbeddedResource Include="tts_service.py" />
</ItemGroup>
Then extract and run it from memory:
csharpvar asm = Assembly.GetExecutingAssembly();
using var stream = asm.GetManifestResourceStream("OpenClawPTT.tts_service.py")!;
var tempPath = Path.Combine(Path.GetTempPath(), "tts_service.py");
using var file = File.Create(tempPath);
await stream.CopyToAsync(file);
    */
    private static readonly string s_defaultScriptPath = Path.Combine(AppContext.BaseDirectory, "tts_service.py");
    private readonly string _espeakNgPath;
    private readonly bool _debugLog;

    private Process? _process;
    private readonly SemaphoreSlim _sem = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pending = new();
    private CancellationTokenSource? _readCts;
    private readonly object _lock = new();
    private bool _disposed;

    public string ProviderName => "Python TTS";

    public IReadOnlyList<string> AvailableVoices { get; } = new[] { "default" };

    public IReadOnlyList<string> AvailableModels { get; } = Array.Empty<string>();

    public PythonTtsProvider(string serviceScriptPath, string pythonPath, string modelPath, string modelName, string? espeakNgPath = null, bool debugLog = false)
    {
        _serviceScriptPath = !string.IsNullOrEmpty(serviceScriptPath) ? serviceScriptPath : s_defaultScriptPath;
        _pythonPath = pythonPath;
        _modelPath = modelPath;
        _modelName = modelName;
        _espeakNgPath = espeakNgPath ?? "";
        _debugLog = debugLog;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        ConsoleUi.Log("python_tts_provider", "Loading TTS model...");

        await _sem.WaitAsync(ct);
        try
        {
            await EnsureRunningAsync(ct);
        }
        finally
        {
            _sem.Release();
        }
    }

    public async Task<byte[]> SynthesizeAsync(string text, string? voice = null, string? model = null, CancellationToken ct = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PythonTtsProvider));

        await _sem.WaitAsync(ct);
        try
        {
            var id = Guid.NewGuid().ToString("N");
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = tcs;

            try
            {
                var request = new { text, id };
                var line = JsonSerializer.Serialize(request) + "\n";
                var textPreview = text?.Substring(0, Math.Min(50, text.Length)) ?? "(null)";
                await _process!.StandardInput.WriteAsync(line);
                await _process.StandardInput.FlushAsync();
            }
            catch
            {
                _pending.TryRemove(id, out _);
                throw;
            }

            string audioPath;
            try
            {
                audioPath = await tcs.Task.WaitAsync(ct);
            }
            catch (TaskCanceledException)
            {
                _pending.TryRemove(id, out _);
                throw;
            }
            catch (InvalidOperationException)
            {
                _pending.TryRemove(id, out _);
                throw;
            }

            if (!File.Exists(audioPath))
                throw new InvalidOperationException($"Python TTS did not produce output file: {audioPath}");

            var audio = await File.ReadAllBytesAsync(audioPath, ct);

            try { File.Delete(audioPath); } catch { /* ignore */ }

            return audio;
        }
        finally
        {
            _sem.Release();
        }
    }

    private async Task EnsureRunningAsync(CancellationToken ct)
    {
        if (_process != null && !_process.HasExited)
            return;

        // Kill any zombie process
        if (_process != null)
        {
            _process.Dispose();
            _process = null;
        }

        FailPendingRequests(new InvalidOperationException("Python TTS process died. Restarting..."));

        _readCts?.Cancel();
        _readCts?.Dispose();
        _readCts = new CancellationTokenSource();

        var pythonExe = ResolvePython();

        var psi = new ProcessStartInfo
        {
            FileName = pythonExe,
            Arguments = $"-u \"{_serviceScriptPath}\"",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        if (!string.IsNullOrEmpty(_espeakNgPath))
            psi.Environment["PATH"] = _espeakNgPath + ";" + psi.Environment["PATH"];
        if (!string.IsNullOrEmpty(_modelName))
            psi.Environment["TTS_MODEL"] = _modelName;

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.Exited += OnProcessExited;

        _process.Start();

        // Wait for READY — loop until we find READY, skipping any other stdout lines
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        string? readyLine = null;
        while (!linked.Token.IsCancellationRequested)
        {
            readyLine = await ReadLineAsync(_process.StandardOutput, linked.Token);
            if (readyLine == null) break;
            if (readyLine.Trim().Equals("READY", StringComparison.OrdinalIgnoreCase))
                break;
        }

        if (readyLine == null || !readyLine.Trim().Equals("READY", StringComparison.OrdinalIgnoreCase))
        {
            _process.Kill(true);
            throw new InvalidOperationException($"Python TTS failed to start. Expected READY, got: {readyLine}");
        }

        ConsoleUi.Log("python_tts_provider", "Ready");

        _ = ReadLoopAsync(_process.StandardError, isErrorStream: true, _readCts.Token);
        _ = ReadLoopAsync(_process.StandardOutput, isErrorStream: false, _readCts.Token);
    }

    private async Task<string?> ReadLineAsync(StreamReader reader, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            var buf = new StringBuilder();
            var bufLen = 0;
            while (true)
            {
                var ch = reader.Read();
                if (ch == -1) return null;
                buf.Append((char)ch);
                bufLen++;
                if (ch == '\n' || bufLen >= 4096)
                    break;
            }
            return buf.ToString();
        }, ct);
    }

    private async Task ReadLoopAsync(StreamReader reader, bool isErrorStream, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _process is { HasExited: false })
            {
                var line = await ReadLineAsync(reader, ct);
                if (line == null) break;

                line = line.Trim();
                if (string.IsNullOrEmpty(line)) continue;

                // Stderr lines: only care about warnings/fallback signals
                if (isErrorStream)
                {
                    if (line.Contains("FALLBACK_REASON"))
                        ConsoleUi.PrintWarning($"TTS Performance Alert: {line}");
                    else if (line.Contains("\"perf\":true"))
                    {
                        var jsonStart = line.IndexOf('{');
                        if (jsonStart >= 0)
                        {
                            try
                            {
                                using var doc = JsonDocument.Parse(line[jsonStart..]);
                                var root = doc.RootElement;
                                var secs = root.GetProperty("time").GetDouble();
                                var bytes = root.GetProperty("bytes").GetInt64();
                                ConsoleUi.Log("audio processing:", $"time: {secs:F2}s, {bytes / 1024.0:F1}KB");
                            }
                            catch { Console.Error.WriteLine(line); }
                        }
                    }
                    else if (line.StartsWith(">") || line.StartsWith("['") || line.StartsWith("[\""))
                    {
                        // Coqui internal prints (sentence splits, timing) — suppress
                    }
                    else if (_debugLog)
                        Console.Error.WriteLine(line);
                    continue;
                }

                // Stdout lines below:

                // Skip the handshake sentinel — EnsureRunningAsync already acted on it
                if (line.Equals("READY", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Error completion: DONE:<id>
                if (line.StartsWith("DONE:", StringComparison.OrdinalIgnoreCase))
                {
                    var id = line.Substring(5).Trim();
                    if (_pending.TryRemove(id, out var tcs))
                        tcs.TrySetException(new InvalidOperationException(
                            $"Python TTS request '{id}' completed with error."));
                    continue;
                }

                // Success completion: { "id": "...", "path": "..." }
                if (line.StartsWith("{"))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        var root = doc.RootElement;
                        var id = root.GetProperty("id").GetString();
                        if (!string.IsNullOrEmpty(id) && _pending.TryRemove(id, out var tcs))
                        {
                            var path = root.GetProperty("path").GetString();
                            tcs.TrySetResult(path ?? string.Empty);
                        }
                    }
                    catch { /* ignore malformed JSON */ }
                    continue;
                }

                // Python debug lines
                if (line.StartsWith("[TTS-PY]"))
                {
                    //TODO: Write correct logs
                    continue;
                    Console.Error.WriteLine(line);
                    continue;
                }

                // Catch-all: forward anything unrecognised so nothing is silently swallowed
                Console.Error.WriteLine($"[TTS-PY?] {line}");
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            FailPendingRequests(new InvalidOperationException(
                $"Python TTS {(isErrorStream ? "stderr" : "stdout")} read loop crashed: {ex.Message}"));
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        FailPendingRequests(new InvalidOperationException("Python TTS process exited unexpectedly."));
    }

    private void FailPendingRequests(Exception ex)
    {
        foreach (var kvp in _pending)
            kvp.Value.TrySetException(ex);
    }

    private string ResolvePython()
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

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _readCts?.Cancel();

        try
        {
            if (_process != null && !_process.HasExited)
            {
                try
                {
                    await _process.StandardInput.WriteAsync("EXIT\n");
                    await _process.StandardInput.FlushAsync();
                }
                catch { /* ignore */ }

                using var killCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try
                {
                    await _process.WaitForExitAsync(killCts.Token);
                }
                catch (OperationCanceledException)
                {
                    _process.Kill(true);
                }
            }
        }
        catch { /* ignore */ }
        finally
        {
            _process?.Dispose();
            _process = null;
        }

        _readCts?.Dispose();
        _sem.Dispose();
    }
}
