namespace OpenClawPTT.Services;

public interface IAudioService : IDisposable
{
    bool IsRecording { get; }
    void StartRecording();
    Task<string?> StopAndTranscribeAsync(CancellationToken ct = default);

    /// <summary>Stops recording and discards the audio without transcribing.</summary>
    void StopDiscard();

    /// <summary>
    /// Re-creates the transcriber after a config change (e.g. STT provider/model switched).
    /// </summary>
    void RecreateTranscriber(AppConfig config, IColorConsole console);
}