using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StreamShell;

namespace OpenClawPTT.TTS.Providers;

/// <summary>
/// Simple bottom panel showing uv environment setup progress during
/// Coqui TTS model list fetch. Displays status text and the latest
/// uv stderr/stdout line.
/// </summary>
public sealed class CoquiEnvSetupPanel : IBottomPanel
{
    private readonly object _sync = new();
    private readonly string[] _lines = new string[2];

    private string _status = "Initializing...";
    private string _latestLine = "";
    private bool _completed;
    private bool _isDirty = true;

    public int LineCount => 2;
    public bool IsDirty { get { lock (_sync) return _isDirty; } }
    public string? CurrentSuggestion => null;
    public bool ShowBottomSeparator => false;

    public void SetStatus(string status, string? latestLine = null)
    {
        lock (_sync)
        {
            _status = status;
            if (latestLine != null)
                _latestLine = TruncateLine(latestLine);
            _isDirty = true;
        }
    }

    public void SetCompleted(bool success, string message)
    {
        lock (_sync)
        {
            _completed = true;
            _status = success
                ? $"[green]✓[/] {message}"
                : $"[yellow]⚠[/] {message}";
            _isDirty = true;
        }
    }

    public IReadOnlyList<string> GetLines(string currentInput)
    {
        lock (_sync)
        {
            _lines[0] = $"[bold]Coqui TTS Setup[/] — {_status}";
            _lines[1] = string.IsNullOrEmpty(_latestLine)
                ? "[grey]Waiting for uv...[/]"
                : $"[grey]{_latestLine}[/]";
            _isDirty = false;
            return _lines;
        }
    }

    public void ClearDirty()
    {
        lock (_sync) { _isDirty = false; }
    }

    public bool TryHandleKey(ConsoleKeyInfo key) => false;

    public Task RunAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose() { }

    private static string TruncateLine(string line, int maxLen = 100)
    {
        if (string.IsNullOrEmpty(line)) return "";
        var trimmed = line.Trim();
        return trimmed.Length <= maxLen ? trimmed : trimmed[..(maxLen - 3)] + "...";
    }
}
