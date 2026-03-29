namespace OpenClawPTT;

internal interface IGlobalHotkeyHook : IDisposable
{
    /// <summary>Fired on a background thread when the hotkey is pressed (key down).</summary>
    event Action? HotkeyPressed;
    /// <summary>Fired on a background thread when the hotkey is released (key up).</summary>
    event Action? HotkeyReleased;

    /// <summary>Sets the hotkey configuration.</summary>
    void SetHotkey(Hotkey hotkey);

    void Start();
}