namespace OpenClawPTT;

/// <summary>
/// Factory for creating IGlobalHotkeyHook instances. Allows PttController
/// to be tested without registering real global hotkeys.
/// </summary>
internal interface IHotkeyHookFactory
{
    IGlobalHotkeyHook Create(Hotkey mapping);
}
