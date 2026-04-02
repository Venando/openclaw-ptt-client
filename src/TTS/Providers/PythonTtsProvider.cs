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

    private Process? _process;
    private readonly SemaphoreSlim _sem = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pending = new();
    private CancellationTokenSource? _readCts;
    private readonly object _lock = new();
    private bool _disposed;

    public string ProviderName => "Python TTS";

    public IReadOnlyList<string> AvailableVoices { get; } = new[] { "default" };

    public IReadOnlyList<string> AvailableModels { get; } = Array.Empty<string>();

    public PythonTtsProvider(string serviceScriptPath, string pythonPath, string modelPath, string modelName)
    {
        _serviceScriptPath = serviceScriptPath;
        _pythonPath = pythonPath;
        _modelPath = modelPath;
        _modelName = modelName;
    }

    public async Task<byte[]> SynthesizeAsync(string text, string? voice = null, string? model = null, CancellationToken ct = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PythonTtsProvider));

        await _sem.WaitAsync(ct);
        try
        {
            await EnsureRunningAsync(ct);

            var id = Guid.NewGuid().ToString("N");
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = tcs;

            try
            {
                var request = new { text, id };
                var line = JsonSerializer.Serialize(request) + "\n";
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
        var args = $"\"{_serviceScriptPath}\" --model_path \"{_modelPath}\" --model_name \"{_modelName}\"";

        var psi = new ProcessStartInfo
        {
            FileName = pythonExe,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.Exited += OnProcessExited;

        _process.Start();

        // Wait for READY
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        var readyLine = await ReadLineAsync(_process.StandardOutput, linked.Token);
        if (readyLine == null || !readyLine.Trim().Equals("READY", StringComparison.OrdinalIgnoreCase))
        {
            var err = await _process.StandardError.ReadToEndAsync(ct);
            _process.Kill(true);
            throw new InvalidOperationException($"Python TTS failed to start. Expected READY, got: {readyLine}\n{err}");
        }

        _ = ReadLoopAsync(_process.StandardOutput, _readCts.Token);
    }

    private async Task<string?> ReadLineAsync(StreamReader reader, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            var buf = new StringBuilder();
            var bufLen = 0;
            while (true)
            {
                var ch = (char)reader.Read();
                if (ch == -1) return null;
                buf.Append(ch);
                bufLen++;
                if (ch == '\n' || bufLen >= 4096)
                    break;
            }
            return buf.ToString();
        }, ct);
    }

    private async Task ReadLoopAsync(StreamReader reader, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _process != null && !_process.HasExited)
            {
                var line = await ReadLineAsync(reader, ct);
                if (line == null) break;
                line = line.Trim();
                if (string.IsNullOrEmpty(line)) continue;

                if (line.Equals("READY", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (line.StartsWith("DONE:", StringComparison.OrdinalIgnoreCase))
                {
                    var id = line.Substring(5).Trim();
                    if (_pending.TryRemove(id, out var tcs))
                        tcs.TrySetException(new InvalidOperationException($"Python TTS request {id} completed with error."));
                    continue;
                }

                // JSON response: { "id": "...", "path": "..." }
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
                            tcs.TrySetResult(path ?? "");
                        }
                    }
                    catch { /* ignore malformed JSON */ }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch { FailPendingRequests(new InvalidOperationException("Python TTS read loop crashed.")); }
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
