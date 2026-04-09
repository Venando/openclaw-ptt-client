namespace OpenClawPTT.Services;

public interface IPttController : IDisposable
{
    bool IsRecording { get; }
    void StartRecording();
    Task<string?> StopAndTranscribeAsync(CancellationToken ct = default);
    void SetHotkey(string hotkeyCombination, bool holdToTalk);
    void Start();
    void Stop();

    /// <summary>Atomically returns true once when a hotkey press has been detected.</summary>
    bool PollHotkeyPressed();

    /// <summary>Atomically returns true once when a hotkey release has been detected.</summary>
    bool PollHotkeyRelease();
}