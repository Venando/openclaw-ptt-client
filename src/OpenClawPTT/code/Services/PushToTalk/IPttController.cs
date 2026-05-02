namespace OpenClawPTT.Services;

public interface IPttController : IDisposable
{
    void SetHotkey(string hotkeyCombination, bool holdToTalk);

    /// <summary>Atomically returns true once when a hotkey press has been detected.</summary>
    bool PollHotkeyPressed();

    /// <summary>Atomically returns true once when a hotkey release has been detected.</summary>
    bool PollHotkeyRelease();

    /// <summary>External trigger: simulate hotkey press (used by AgentHotkeyService).</summary>
    void StartRecording();

    /// <summary>External trigger: simulate hotkey release (used by AgentHotkeyService).</summary>
    void StopRecording();
}