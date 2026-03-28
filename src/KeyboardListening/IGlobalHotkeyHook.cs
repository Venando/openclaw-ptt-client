namespace OpenClawPTT;

internal interface IGlobalHotkeyHook : IDisposable
{
    /// <summary>Fired on a background thread when Alt+= is pressed.</summary>
    event Action? HotkeyPressed;

    void Start();
}