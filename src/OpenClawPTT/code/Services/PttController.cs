using System;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.VisualFeedback;

namespace OpenClawPTT.Services;

public sealed class PttController : IPttController
{
    private readonly IAudioService _audioService;
    private readonly IVisualFeedback _visualFeedback;
    private readonly string _hotkeyCombination;
    private readonly bool _holdToTalk;

    private IGlobalHotkeyHook? _hotkeyHook;
    private bool _disposed;

    private volatile bool _hotkeyPressed;
    private volatile bool _hotkeyReleased;

    public PttController(AppConfig config, IAudioService audioService)
    {
        _audioService = audioService;
        _visualFeedback = VisualFeedbackFactory.Create(config);
        _hotkeyCombination = config.HotkeyCombination;
        _holdToTalk = config.HoldToTalk;
    }

    public bool IsRecording => _audioService.IsRecording;

    public void SetHotkey(string hotkeyCombination, bool holdToTalk)
    {
        _hotkeyHook?.Dispose();
        _hotkeyHook = GlobalHotkeyHookFactory.Create();

        var hotkey = HotkeyMapping.Parse(hotkeyCombination);
        _hotkeyHook.SetHotkey(hotkey);

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

    public void Start()
    {
        _hotkeyHook?.Start();
    }

    public void Stop()
    {
        _hotkeyHook?.Dispose();
    }

    public void StartRecording()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _audioService.StartRecording();
    }

    public async Task<string?> StopAndTranscribeAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return await _audioService.StopAndTranscribeAsync(ct);
    }

    public bool ShouldStopRecording() => _holdToTalk && PollHotkeyRelease() && _audioService.IsRecording;

    public bool ShouldToggleRecording() => !_holdToTalk && PollHotkeyPressed();

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
            _audioService.Dispose();
            _visualFeedback.Dispose();
            _disposed = true;
        }
    }
}
