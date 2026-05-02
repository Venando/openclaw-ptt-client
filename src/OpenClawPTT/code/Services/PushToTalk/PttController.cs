using OpenClawPTT.VisualFeedback;

namespace OpenClawPTT.Services;

internal sealed class PttController : IPttController
{
    private readonly IHotkeyHookFactory? _hotkeyHookFactory;
    private IGlobalHotkeyHook? _hotkeyHook;
    private bool _disposed;

    private volatile bool _hotkeyPressed;
    private volatile bool _hotkeyReleased;

    private volatile bool _externalHotkeyPressed;
    private volatile bool _externalHotkeyRelease;
    private volatile bool _cancelRecording;

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
        if (_hotkeyPressed || _externalHotkeyPressed)
        {
            _hotkeyPressed = false;
            _externalHotkeyPressed = false;
            return true;
        }
        return false;
    }

    public bool PollHotkeyRelease()
    {
        if (_hotkeyReleased || _externalHotkeyRelease)
        {
            _hotkeyReleased = false;
            _externalHotkeyRelease = false;
            return true;
        }
        return false;
    }

    public void StartRecording()
    {
        _externalHotkeyPressed = true;
        _externalHotkeyRelease = false;
    }

    public void StopRecording()
    {
        _externalHotkeyPressed = false;
        _externalHotkeyRelease = true;
    }

    public void CancelRecording()
    {
        _cancelRecording = true;
        _externalHotkeyPressed = false;
        _externalHotkeyRelease = false;
    }

    /// <summary>Returns true if the current recording should be cancelled (Escape pressed).</summary>
    public bool PollCancelRecording()
    {
        if (_cancelRecording)
        {
            _cancelRecording = false;
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
