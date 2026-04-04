using System.Buffers;
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
    private readonly string? _serviceScriptPathOverride;
    private readonly PythonEnvironment _environment;
    private readonly bool _debugLog;
    private readonly TimeSpan _synthesisTimeout;

    private Process? _process;
    private readonly SemaphoreSlim _sem = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pending = new();
    private CancellationTokenSource? _readCts;
    private readonly object _lock = new();
    private bool _disposed;

    public string ProviderName => "Python TTS";

    public IReadOnlyList<string> AvailableVoices { get; } = new[] { "default" };

    public IReadOnlyList<string> AvailableModels { get; } = Array.Empty<string>();

    public PythonTtsProvider(string? serviceScriptPathOverride, string pythonPath, string modelPath, string modelName, string? espeakNgPath = null, bool debugLog = false, TimeSpan? requestTimeout = null)
    {
        _serviceScriptPathOverride = serviceScriptPathOverride;
        _environment = new PythonEnvironment(pythonPath, modelName, espeakNgPath);
        _debugLog = debugLog;
        _synthesisTimeout = requestTimeout ?? TimeSpan.FromSeconds(30);
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
                var request = new { text, id, voice, model };
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
                audioPath = await tcs.Task.WaitAsync(_synthesisTimeout, ct);
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

        ProcessStartInfo psi = _environment.CreateProcessStartInfo(_serviceScriptPathOverride);

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.Exited += OnProcessExited;

        _process.Start();

        // Wait for READY — loop until we find {"type":"ready"} or plain "READY", logging all lines
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        string? readyLine = null;
        while (!linked.Token.IsCancellationRequested)
        {
            readyLine = await ReadLineAsync(_process.StandardOutput, linked.Token);
            if (IsReadyLine(readyLine ?? ""))
                break;
        }

        if (readyLine == null || !IsReadyLine(readyLine))
        {
            _process.Kill(true);
            throw new InvalidOperationException($"Python TTS failed to start. Expected READY, got: {readyLine}");
        }

        ConsoleUi.Log("python_tts_provider", "Ready");

        // Stderr: structured log messages (perf, warn, info, error)
        // Stdout: structured protocol messages (ready, done, ok)
        _ = ReadLoopAsync(_process.StandardError, DispatchLog, _readCts.Token);
        _ = ReadLoopAsync(_process.StandardOutput, DispatchProtocol, _readCts.Token);
    }

    private async Task<string?> ReadLineAsync(StreamReader reader, CancellationToken ct)
    {
        try
        {
            return await reader.ReadLineAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return null; 
        }
    }

    private async Task ReadLoopAsync(StreamReader reader, Action<string> onLine, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _process is { HasExited: false })
            {
                var line = await ReadLineAsync(reader, ct);
                if (string.IsNullOrWhiteSpace(line)) break;
                onLine(line);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            FailPendingRequests(new InvalidOperationException(
                $"Python TTS read loop crashed: {ex.Message}"));
        }
    }

    private void DispatchProtocol(string line)
    {
        if (!JsonHelper.TryParseJson(line, out JsonDocument? jsonDocument, out string msgType))
        {
            if (_debugLog) Console.Error.WriteLine(line);
            return;
        }

        using (jsonDocument)
        {
            var root = jsonDocument.RootElement;
            switch (msgType)
            {
                case MessageType.Ok:
                    var okId = root.GetProperty("id").GetString();
                    if (!string.IsNullOrEmpty(okId) && _pending.TryRemove(okId, out var okTcs))
                    {
                        var path = root.GetProperty("path").GetString();
                        okTcs.TrySetResult(path ?? string.Empty);
                    }
                    break;
                case MessageType.Error:
                    {
                        var errId = root.TryGetProperty("id", out var eid) ? eid.GetString() : null;
                        var errMsg = root.TryGetProperty("msg", out var em) ? em.GetString() : "unknown";
                        if (!string.IsNullOrEmpty(errId) && _pending.TryRemove(errId, out var errTcs))
                            errTcs.TrySetException(new InvalidOperationException(
                                $"Python TTS request '{errId}' failed: {errMsg ?? "unknown"}"));
                        break;
                    }
                default:
                    if (_debugLog) Console.Error.WriteLine(line);
                    break;
            }
        }
    }

    private static bool IsReadyLine(string line)
    {
        if (JsonHelper.TryParseJson(line, out var jsonDoc, out var msgType))
        {
            jsonDoc.Dispose();
            return msgType == MessageType.Ready;
        }
        return false;
    }

    private void DispatchLog(string line)
    {
        if (!JsonHelper.TryParseJson(line, out JsonDocument? jsonDocument, out string? msgType))
            return;

        using (jsonDocument)
        {
            var root = jsonDocument.RootElement;
            switch (msgType)
            {
                case MessageType.Performance:
                    var secs = root.GetProperty("time").GetDouble();
                    var bytes = root.GetProperty("bytes").GetInt64();
                    ConsoleUi.Log("audio processing:", $"time: {secs:F2}s, {bytes / 1024.0:F1}KB");
                    break;
                case MessageType.Warn:
                    var msg = root.GetProperty("msg").GetString();
                    ConsoleUi.PrintWarning($"TTS Performance Alert: {msg}");
                    break;
                case MessageType.Info:
                case MessageType.Debug:
                    if (_debugLog)
                    {
                        var infoMsg = root.TryGetProperty("msg", out var m) ? m.GetString() : line;
                        Console.Error.WriteLine(infoMsg);
                    }
                    break;
                case MessageType.Error:
                    var errMsg = root.TryGetProperty("msg", out var e) ? e.GetString() : line;
                    ConsoleUi.PrintError(errMsg ?? line);
                    break;
                default:
                    if (_debugLog) Console.Error.WriteLine(line);
                    break;
            }
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        FailPendingRequests(new InvalidOperationException("Python TTS process exited unexpectedly."));
    }

    private void FailPendingRequests(Exception ex)
    {
        foreach (KeyValuePair<string, TaskCompletionSource<string>> kvp in _pending)
            kvp.Value.TrySetException(ex);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // Fail all pending requests before cancelling the read loop.
        // This ensures in-flight SynthesizeAsync calls complete with ObjectDisposedException
        // rather than hanging forever.
        if (!_pending.IsEmpty)
        {
            var ex = new ObjectDisposedException(nameof(PythonTtsProvider));
            foreach (var kvp in _pending)
                kvp.Value.TrySetException(ex);
            _pending.Clear();
        }

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
