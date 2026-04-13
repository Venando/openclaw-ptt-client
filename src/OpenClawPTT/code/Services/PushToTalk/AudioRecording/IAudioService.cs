namespace OpenClawPTT.Services;

public interface IAudioService : IDisposable
{
    bool IsRecording { get; }
    void StartRecording();
    Task<string?> StopAndTranscribeAsync(CancellationToken ct = default);
}