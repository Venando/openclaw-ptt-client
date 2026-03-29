using System;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT;
using OpenClawPTT.VisualFeedback;

namespace OpenClawPTT.Services;

public sealed class AudioService : IDisposable
{
    private readonly AudioRecorder _recorder;
    private readonly GroqTranscriber _transcriber;
    private readonly IVisualFeedback _visualFeedback;
    private readonly string _hotkeyCombination;
    private readonly bool _holdToTalk;
    private bool _disposed;
    
    public AudioService(AppConfig config)
    {
        _recorder = new AudioRecorder(config.SampleRate, config.Channels, config.BitsPerSample, config.MaxRecordSeconds);
        _transcriber = new GroqTranscriber(config.GroqApiKey, 
            retryCount: config.GroqRetryCount, 
            retryDelayMs: config.GroqRetryDelayMs, 
            retryBackoffFactor: config.GroqRetryBackoffFactor);
        _visualFeedback = VisualFeedbackFactory.Create();
        _hotkeyCombination = config.HotkeyCombination;
        _holdToTalk = config.HoldToTalk;
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
        Console.WriteLine("■");
        
        if (wav.Length < 1024)
        {
            ConsoleUi.PrintWarning("Too short (<1KB), skipped.");
            return null;
        }
        
        ConsoleUi.PrintInfo($"Sending to Groq {wav.Length / 1024.0:F1} KB…");
        
        try
        {
            var transcribed = await _transcriber.TranscribeAsync(wav);
            ConsoleUi.PrintSuccess($"Transcribed: {transcribed}");
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