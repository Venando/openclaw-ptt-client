using OpenClawPTT.VisualFeedback;

namespace OpenClawPTT.Services;

internal sealed class PttController : IPttController
{
    private readonly IHotkeyHookFactory? _hotkeyHookFactory;
    private IGlobalHotkeyHook? _hotkeyHook;
    private bool _disposed;

    private volatile bool _hotkeyPressed;
    private volatile bool _hotkeyReleased;

    public PttController(IHotkeyHookFactory? hotkeyHookFactory = null)
    {
        _hotkeyHookFactory = hotkeyHookFactory;
    }

    public void SetHotkey(string hotkeyCombination, bool holdToTalk)
    {
        _hotkeyHook?.Dispose();

        var mapping = HotkeyMapping.Parse(hotkeyCombination);

        if (_hotkeyHookFactory != null)
        {
            _hotkeyHook = _hotkeyHookFactory.Create(mapping);
        }
        else
        {
            _hotkeyHook = GlobalHotkeyHookFactory.Create();
            _hotkeyHook.SetHotkey(mapping);
        }
        
        _hotkeyHook.Start();

        if (holdToTalk)
        {
            _hotkeyHook.HotkeyPressed += () => _hotkeyPressed = true;
            _hotkeyHook.HotkeyReleased += () => _hotkeyReleased = true;
        }
        else
        {
            _hotkeyHook.HotkeyPressed += () => _hotkeyPressed = true;
        }
    }

    public bool PollHotkeyPressed()
    {
        if (_hotkeyPressed)
        {
            _hotkeyPressed = false;
            return true;
        }
        return false;
    }

    public bool PollHotkeyRelease()
    {
        if (_hotkeyReleased)
        {
            _hotkeyReleased = false;
            return true;
        }
        return false;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _hotkeyHook?.Dispose();
            _disposed = true;
        }
    }
}
