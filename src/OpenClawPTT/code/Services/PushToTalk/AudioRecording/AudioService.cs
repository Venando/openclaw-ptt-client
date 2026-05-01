using System;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT;
using OpenClawPTT.Transcriber;
using OpenClawPTT.VisualFeedback;

namespace OpenClawPTT.Services;

public sealed class AudioService : IAudioService
{
    private readonly IAudioRecorder _recorder;
    private readonly ITranscriber _transcriber;
    private readonly IVisualFeedback _visualFeedback;
    
    private readonly string _hotkeyCombination;
    private readonly bool _holdToTalk;
    private readonly int _rightMarginIndent;
    private bool _disposed;
    
    /// <summary>
    /// Creates an AudioService with a real AudioRecorder.
    /// </summary>
    public AudioService(AppConfig config)
        : this(config, recorder: null)
    {
    }
    
    /// <summary>
    /// Creates an AudioService with an injected recorder (for testing).
    /// </summary>
    internal AudioService(AppConfig config, IAudioRecorder? recorder)
    {
        _recorder = recorder ?? new AudioRecorder(config.SampleRate, config.Channels, config.BitsPerSample, config.MaxRecordSeconds);
        _transcriber = TranscriberFactory.Create(config);
        _visualFeedback = VisualFeedbackFactory.Create(config);
        _hotkeyCombination = config.HotkeyCombination;
        _holdToTalk = config.HoldToTalk;
        _rightMarginIndent = config.RightMarginIndent;
    }
    
    public bool IsRecording => _recorder.IsRecording;
    
    public void StartRecording()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AudioService));
        
        _recorder.StartRecording();
        ConsoleUi.PrintRecordingIndicator(true, _hotkeyCombination, _holdToTalk);
        _visualFeedback.Show();
    }
    
    public async Task<string?> StopAndTranscribeAsync(CancellationToken ct)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AudioService));
        if (!_recorder.IsRecording) return null;
        
        var wav = _recorder.StopRecording();
        _visualFeedback.Hide();
        ConsoleUi.PrintInlineInfo("■");
        
        if (wav.Length < 1024)
        {
            ConsoleUi.PrintWarning("Too short (<1KB), skipped.");
            return null;
        }

        Console.WriteLine();
        ConsoleUi.PrintInfo($"Sending to Groq {wav.Length / 1024.0:F1} KB…");
        
        try
        {
            var transcribed = await _transcriber.TranscribeAsync(wav, ct: ct);
            ConsoleUi.PrintSuccessWordWrap("  ✓ Transcribed: ", transcribed, _rightMarginIndent);
            return transcribed;
        }
        catch (Exception ex)
        {
            ConsoleUi.PrintError($"Transcription failed: {ex.Message}");
            return null;
        }
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