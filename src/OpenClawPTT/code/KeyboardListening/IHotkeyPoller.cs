namespace OpenClawPTT.Services;

/// <summary>Polling interface for hotkey state, separate from PttController lifecycle.</summary>
public interface IHotkeyPoller
{
    bool PollHotkeyPressed();
    bool PollHotkeyRelease();
}
