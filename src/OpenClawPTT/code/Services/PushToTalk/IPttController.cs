namespace OpenClawPTT.Services;

public interface IPttController : IDisposable
{
    void SetHotkey(string hotkeyCombination, bool holdToTalk);

    /// <summary>Atomically returns true once when a hotkey press has been detected.</summary>
    bool PollHotkeyPressed();

    /// <summary>Atomically returns true once when a hotkey release has been detected.</summary>
    bool PollHotkeyRelease();
}