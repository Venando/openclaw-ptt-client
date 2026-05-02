using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace OpenClawPTT;

/// <summary>
/// Reads raw evdev events from /dev/input/event* to detect global hotkeys.
/// Works in TTY without X11/Wayland. Requires membership in the 'input' group:
///   sudo usermod -aG input $USER
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class LinuxEvdevHotkeyHook : IGlobalHotkeyHook
{
    public event Action? HotkeyPressed;
    public event Action? HotkeyReleased;

    private readonly CancellationTokenSource _cts = new();
    private Thread? _thread;

    // evdev constants
    private const ushort EV_KEY = 0x01;
    private const int VALUE_DOWN = 1;
    private const int VALUE_REPEAT = 2;
    private const int VALUE_UP = 0;

    // Linux key codes for modifiers (left/right)
    private const ushort KEY_LEFTALT = 56;
    private const ushort KEY_RIGHTALT = 100;
    private const ushort KEY_LEFTCTRL = 29;
    private const ushort KEY_RIGHTCTRL = 97;
    private const ushort KEY_LEFTSHIFT = 42;
    private const ushort KEY_RIGHTSHIFT = 54;
    private const ushort KEY_LEFTMETA = 125;  // Left Super/Windows
    private const ushort KEY_RIGHTMETA = 126;

    // sizeof(struct input_event) on 64-bit Linux:
    //   struct timeval = 8+8, type = 2, code = 2, value = 4  →  24 bytes
    private const int EVENT_SIZE = 24;

    // Hotkey configuration
    private Hotkey? _hotkey;
    private int _hotkeyKeyCode;
    private HashSet<Modifier> _modifiers = new();
    // Modifier down counts (for left/right variants)
    private int _altDownCount;
    private int _ctrlDownCount;
    private int _shiftDownCount;
    private int _metaDownCount;
    private bool _hotkeyKeyDown;

    public void SetHotkey(Hotkey hotkey)
    {
        _hotkey = hotkey;
        _hotkeyKeyCode = HotkeyMapping.GetPlatformKeyCode(hotkey.Key);
        _modifiers = hotkey.Modifiers;
        // Reset states
        _altDownCount = 0;
        _ctrlDownCount = 0;
        _shiftDownCount = 0;
        _metaDownCount = 0;
        _hotkeyKeyDown = false;
    }

    public void Start()
    {
        _thread = new Thread(ReadLoop) { IsBackground = true, Name = "EvdevHotkeyLoop" };
        _thread.Start();
    }

    // ── main device-reading loop ──────────────────────────────────────

    private void ReadLoop()
    {
        List<string> devicePaths;
        try
        {
            devicePaths = DiscoverKeyboardDevices();
        }
        catch (DirectoryNotFoundException ex)
        {
            ConsoleUi.Log("hotkey", $"Keyboard device directory not found: {ex.Message}");
            return;
        }
        catch (UnauthorizedAccessException ex)
        {
            ConsoleUi.Log("hotkey", $"No permission to access keyboard devices: {ex.Message}");
            ConsoleUi.Log("hotkey", "Fix: sudo usermod -aG input $USER  (then re-login)");
            return;
        }

        if (devicePaths.Count == 0)
        {
            ConsoleUi.Log("hotkey", "No accessible keyboard devices found in /dev/input/.");
            ConsoleUi.Log("hotkey", "Fix: sudo usermod -aG input $USER  (then re-login)");
            return;
        }

        ConsoleUi.Log("hotkey", $"Watching {devicePaths.Count} keyboard device(s) for hotkey");

        var ct = _cts.Token;

        // Spawn one reader task per device — they all share modifier counts via Interlocked
        var tasks = devicePaths
            .Select(path => ReadDeviceAsync(path, ct))
            .ToArray();

        try
        {
            Task.WaitAll(tasks, ct);
        }
        catch (OperationCanceledException) { }
        catch (AggregateException) { }
    }

    private async Task ReadDeviceAsync(string path, CancellationToken ct)
    {
        FileStream? fs = TryOpenDevice(path);
        if (fs is null) return;

        await using (fs)
        {
            var buf = new byte[EVENT_SIZE];
            while (!ct.IsCancellationRequested)
            {
                int totalRead = 0;
                try
                {
                    while (totalRead < EVENT_SIZE)
                    {
                        int n = await fs.ReadAsync(buf.AsMemory(totalRead, EVENT_SIZE - totalRead), ct);
                        if (n == 0) return;
                        totalRead += n;
                    }
                }
                catch (OperationCanceledException) { return; }
                catch { return; }

                ProcessEvent(buf);
            }
        }
    }

    private void ProcessEvent(byte[] data)
    {
        ushort type = BitConverter.ToUInt16(data, 16);
        ushort code = BitConverter.ToUInt16(data, 18);
        int value = BitConverter.ToInt32(data, 20);

        if (type != EV_KEY) return;

        // Update modifier counts
        switch (code)
        {
            case KEY_LEFTALT:
            case KEY_RIGHTALT:
                if (value == VALUE_DOWN) Interlocked.Increment(ref _altDownCount);
                else if (value == VALUE_UP) Interlocked.Decrement(ref _altDownCount);
                break;
            case KEY_LEFTCTRL:
            case KEY_RIGHTCTRL:
                if (value == VALUE_DOWN) Interlocked.Increment(ref _ctrlDownCount);
                else if (value == VALUE_UP) Interlocked.Decrement(ref _ctrlDownCount);
                break;
            case KEY_LEFTSHIFT:
            case KEY_RIGHTSHIFT:
                if (value == VALUE_DOWN) Interlocked.Increment(ref _shiftDownCount);
                else if (value == VALUE_UP) Interlocked.Decrement(ref _shiftDownCount);
                break;
            case KEY_LEFTMETA:
            case KEY_RIGHTMETA:
                if (value == VALUE_DOWN) Interlocked.Increment(ref _metaDownCount);
                else if (value == VALUE_UP) Interlocked.Decrement(ref _metaDownCount);
                break;
        }

        // Check hotkey key
        if (code == _hotkeyKeyCode)
        {
            if (value == VALUE_DOWN && !_hotkeyKeyDown)
            {
                if (ModifiersMatch())
                {
                    _hotkeyKeyDown = true;
                    ThreadPool.QueueUserWorkItem(_ => HotkeyPressed?.Invoke());
                }
            }
            else if (value == VALUE_UP && _hotkeyKeyDown)
            {
                _hotkeyKeyDown = false;
                ThreadPool.QueueUserWorkItem(_ => HotkeyReleased?.Invoke());
            }
        }
    }

    private bool ModifiersMatch()
    {
        bool altRequired = _modifiers.Contains(Modifier.Alt);
        bool ctrlRequired = _modifiers.Contains(Modifier.Ctrl);
        bool shiftRequired = _modifiers.Contains(Modifier.Shift);
        bool winRequired = _modifiers.Contains(Modifier.Win);

        bool altPressed = Volatile.Read(ref _altDownCount) > 0;
        bool ctrlPressed = Volatile.Read(ref _ctrlDownCount) > 0;
        bool shiftPressed = Volatile.Read(ref _shiftDownCount) > 0;
        bool winPressed = Volatile.Read(ref _metaDownCount) > 0;

        return altRequired == altPressed &&
               ctrlRequired == ctrlPressed &&
               shiftRequired == shiftPressed &&
               winRequired == winPressed;
    }

    // ── device discovery ──────────────────────────────────────────────

    private static List<string> DiscoverKeyboardDevices()
    {
        var results = new List<string>();

        foreach (var path in Directory.GetFiles("/dev/input", "event*").OrderBy(x => x))
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open,
                    FileAccess.Read, FileShare.ReadWrite);

                int fd = (int)fs.SafeFileHandle.DangerousGetHandle();

                // EVIOCGBIT(EV_KEY) — check that this device reports key events
                var evBits = new byte[4];
                if (ioctl(fd, EVIOCGBIT(0, evBits.Length), evBits) < 0) continue;
                if ((evBits[EV_KEY / 8] & (1 << (EV_KEY % 8))) == 0) continue;

                // We could verify the hotkey key exists, but skip for simplicity
                // If the key doesn't exist on this device, it'll never trigger.

                results.Add(path);
            }
            catch { /* no permission or not a suitable device */ }
        }

        return results;
    }

    // EVIOCGBIT(type, len) ioctl number for Linux/x86-64
    private static int EVIOCGBIT(int type, int len) =>
        (int)(0x80000000u | ((uint)len << 16) | ((uint)'E' << 8) | (uint)(0x20 + type));

    private static FileStream? TryOpenDevice(string path)
    {
        try
        {
            return new FileStream(path, FileMode.Open,
                FileAccess.Read, FileShare.ReadWrite,
                bufferSize: 0, useAsync: true);
        }
        catch { return null; }
    }

    public void Dispose() => _cts.Cancel();

    [DllImport("libc", SetLastError = true, EntryPoint = "ioctl")]
    private static extern int ioctl(int fd, int request, byte[] arg);
}