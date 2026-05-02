namespace OpenClawPTT.Services;

public interface IAudioService : IDisposable
{
    bool IsRecording { get; }
    void StartRecording();
    Task<string?> StopAndTranscribeAsync(CancellationToken ct = default);

    /// <summary>Stops recording and discards the audio without transcribing.</summary>
    void StopDiscard();
}