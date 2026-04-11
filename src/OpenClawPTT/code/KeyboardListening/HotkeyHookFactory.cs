namespace OpenClawPTT;

/// <summary>
/// Default implementation of IHotkeyHookFactory that delegates to GlobalHotkeyHookFactory.
/// </summary>
internal sealed class HotkeyHookFactory : IHotkeyHookFactory
{
    public IGlobalHotkeyHook Create(Hotkey mapping)
    {
        var hook = GlobalHotkeyHookFactory.Create();
        hook.SetHotkey(mapping);
        return hook;
    }
}
