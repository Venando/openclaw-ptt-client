using System;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.VisualFeedback;

namespace OpenClawPTT.Services;

public sealed class AudioService : IDisposable
{
    private readonly AudioRecorder _recorder;
    private readonly GroqTranscriber _transcriber;
    private readonly IVisualFeedback _visualFeedback;
    private bool _disposed;
    
    public AudioService(int sampleRate, int channels, int bitsPerSample, int maxRecordSeconds, string groqApiKey)
    {
        _recorder = new AudioRecorder(sampleRate, channels, bitsPerSample, maxRecordSeconds);
        _transcriber = new GroqTranscriber(groqApiKey);
        _visualFeedback = VisualFeedbackFactory.Create();
    }
    
    public bool IsRecording => _recorder.IsRecording;
    
    public void StartRecording()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AudioService));
        
        _recorder.StartRecording();
        ConsoleUi.PrintRecordingIndicator(true);
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