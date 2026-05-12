using System;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT;
using OpenClawPTT.Transcriber;
using OpenClawPTT.VisualFeedback;
using Spectre.Console;

namespace OpenClawPTT.Services;

public sealed class AudioService : IAudioService
{
    private readonly IColorConsole _console;
    private readonly IAudioRecorder _recorder;
    private ITranscriber _transcriber;
    private readonly IVisualFeedback _visualFeedback;
    private readonly IAgentSettingsPersistence _agentSettingsPersistence;
    
    private readonly string _hotkeyCombination;
    private readonly bool _holdToTalk;
    private readonly int _rightMarginIndent;
    private bool _disposed;
    
    /// <summary>
    /// Creates an AudioService with a real AudioRecorder.
    /// </summary>
    public AudioService(AppConfig config, IColorConsole console, IAgentSettingsPersistence agentSettingsPersistence)
        : this(config, console, agentSettingsPersistence, recorder: null)
    {
    }
    
    /// <summary>
    /// Creates an AudioService with an injected recorder (for testing).
    /// </summary>
    internal AudioService(AppConfig config, IColorConsole console, IAgentSettingsPersistence agentSettingsPersistence, IAudioRecorder? recorder)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _agentSettingsPersistence = agentSettingsPersistence ?? throw new ArgumentNullException(nameof(agentSettingsPersistence));
        _recorder = recorder ?? new AudioRecorder(config.SampleRate, config.Channels, config.BitsPerSample, config.MaxRecordSeconds);
        _transcriber = TranscriberFactory.Create(config, console);
        _visualFeedback = VisualFeedbackFactory.Create(config);
        LogSttProvider(config);
        _hotkeyCombination = config.HotkeyCombination;
        _holdToTalk = config.HoldToTalk;
        _rightMarginIndent = config.RightMarginIndent;
    }
    
    public bool IsRecording => _recorder.IsRecording;
    
    public void StartRecording()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AudioService));
        
        _recorder.StartRecording();
        // Use per-agent hotkey if set, else fall back to global config default
        var activeAgentId = AgentRegistry.ActiveAgentId;
        var effectiveHotkey = activeAgentId != null
            ? (_agentSettingsPersistence.GetPersistedHotkey(activeAgentId) ?? _hotkeyCombination)
            : _hotkeyCombination;
        _console.PrintRecordingIndicator(true, effectiveHotkey, _holdToTalk);
        _visualFeedback.Show();
    }
    
    public void StopDiscard()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AudioService));
        if (!_recorder.IsRecording) return;

        _recorder.StopRecording();
        _visualFeedback.Hide();
        _console.PrintMarkup("[grey]  ─ Recording discarded ─[/]");
    }

    public async Task<string?> StopAndTranscribeAsync(CancellationToken ct)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AudioService));
        if (!_recorder.IsRecording) return null;
        
        var wav = _recorder.StopRecording();
        _visualFeedback.Hide();
        _console.PrintInfo("■ Recording stopped");
        
        if (wav.Length < 1024)
        {
            _console.PrintWarning("Too short (<1KB), skipped.");
            return null;
        }

        try
        {
            var transcribed = await _transcriber.TranscribeAsync(wav, ct: ct);
            var shellHost = _console.GetStreamShellHost();
            var prefix = $"Transcribed ({wav.Length / 1024.0:F1} KB): ";
            _console.PrintMarkup($"[green][dim]  ✓ {Markup.Escape(prefix)}[/][/] [green]{Markup.Escape(transcribed)}[/]");
            return transcribed;
        }
        catch (Exception ex)
        {
            _console.PrintError($"Transcription failed ({wav.Length / 1024.0:F1} KB): {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Re-creates the transcriber after a config change (e.g. STT provider/model switched).
    /// Disposes the old transcriber and creates a new one from the updated config.
    /// </summary>
    public void RecreateTranscriber(AppConfig config, IColorConsole console)
    {
        var old = Interlocked.Exchange(ref _transcriber, TranscriberFactory.Create(config, console));
        old?.Dispose();
        LogSttProvider(config, recreated: true);
    }

    private void LogSttProvider(AppConfig config, bool recreated = false)
    {
        var provider = config.SttProvider ?? "groq";
        string model = provider switch
        {
            "groq" => config.GroqModel ?? "whisper-large-v3-turbo",
            "openai" => config.OpenAiModel ?? "whisper-1",
            "whisper-cpp" => config.WhisperCppModel ?? "base",
            _ => "?"
        };
        var action = recreated ? "Switched to" : "STT";
        _console.PrintMarkup($"[grey][dim]  {action}: {provider} ({model})[/][/]");
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _recorder.Dispose();
            _transcriber.Dispose();
            _visualFeedback.Dispose();
            _disposed = true;
        }
    }
}