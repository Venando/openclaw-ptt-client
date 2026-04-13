namespace OpenClawPTT;

/// <summary>
/// Abstracts the audio recording backend so AudioService can be tested
/// without requiring a real microphone or sox/NAudio.
/// </summary>
public interface IAudioRecorder : IDisposable
{
    bool IsRecording { get; }
    void StartRecording();
    byte[] StopRecording();
}
