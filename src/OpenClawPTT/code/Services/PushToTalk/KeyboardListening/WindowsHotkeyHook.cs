using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace OpenClawPTT;

[SupportedOSPlatform("windows")]
internal sealed class WindowsHotkeyHook : IGlobalHotkeyHook
{
    public event Action? HotkeyPressed;
    public event Action? HotkeyReleased;
    public event Action<int>? HotkeyIndexPressed;
    public event Action<int>? HotkeyIndexReleased;

    private readonly Thread _thread;
    private volatile IntPtr _hookHandle = IntPtr.Zero;
    private volatile bool _disposed;
    
    // Hotkey configuration
    private Hotkey? _hotkey;
    private int _hotkeyKeyCode;
    private HashSet<Modifier> _modifiers = new();
    private Dictionary<Modifier, List<int>> _modifierKeyCodes = new();
    private Dictionary<Modifier, bool> _modifierDown = new();
    private bool _hotkeyKeyDown;

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYUP = 0x0105;

    // Virtual key codes
    private const int VK_LMENU = 0xA4;      // Left Alt
    private const int VK_RMENU = 0xA5;      // Right Alt
    private const int VK_LCONTROL = 0xA2;   // Left Ctrl
    private const int VK_RCONTROL = 0xA3;   // Right Ctrl
    private const int VK_LSHIFT = 0xA0;     // Left Shift
    private const int VK_RSHIFT = 0xA1;     // Right Shift
    private const int VK_LWIN = 0x5B;       // Left Windows
    private const int VK_RWIN = 0x5C;       // Right Windows

    public WindowsHotkeyHook()
    {
        _thread = new Thread(MessageLoop)
        {
            IsBackground = true,
            Name = "WinHotkeyLoop"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        
        // Initialize modifier mapping
        _modifierKeyCodes[Modifier.Alt] = new List<int> { VK_LMENU, VK_RMENU };
        _modifierKeyCodes[Modifier.Ctrl] = new List<int> { VK_LCONTROL, VK_RCONTROL };
        _modifierKeyCodes[Modifier.Shift] = new List<int> { VK_LSHIFT, VK_RSHIFT };
        _modifierKeyCodes[Modifier.Win] = new List<int> { VK_LWIN, VK_RWIN };
        foreach (var mod in Enum.GetValues<Modifier>())
            _modifierDown[mod] = false;
    }

    public void SetHotkey(Hotkey hotkey)
    {
        _hotkey = hotkey;
        _hotkeyKeyCode = HotkeyMapping.GetPlatformKeyCode(hotkey.Key);
        _modifiers = hotkey.Modifiers;
        // Reset states
        foreach (var mod in Enum.GetValues<Modifier>())
            _modifierDown[mod] = false;
        _hotkeyKeyDown = false;
    }

    public void SetHotkeys(System.Collections.Generic.IEnumerable<Hotkey> hotkeys)
    {
        // Windows single-hotkey hook — use first for now
        foreach (var hk in hotkeys)
        {
            SetHotkey(hk);
            break;
        }
    }

    public void Start() => _thread.Start();

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _hotkey != null)
        {
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            int msg = wParam.ToInt32();

            bool isDown = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
            bool isUp = msg is WM_KEYUP or WM_SYSKEYUP;
            bool isSysKey = msg is WM_SYSKEYDOWN or WM_SYSKEYUP;

            // Debug: log every key event on the hotkey's vkCode
            if (info.vkCode == _hotkeyKeyCode || info.vkCode is 0xA4 or 0xA5)
            {
                ConsoleUi.Log("hook", $"vkCode=0x{info.vkCode:X4} msg=0x{msg:X4} isDown={isDown} isSysKey={isSysKey} modifiers=[" +
                    string.Join(",", Enum.GetValues<Modifier>().Where(m => _modifierDown[m]).Select(m => m.ToString())) + "]");
            }

            // Update modifier states
            foreach (var (mod, vkList) in _modifierKeyCodes)
            {
                if (vkList.Contains((int)info.vkCode))
                {
                    _modifierDown[mod] = isDown;
                    break;
                }
            }

            // Check if this is the hotkey key
            if (info.vkCode == _hotkeyKeyCode)
            {
                if (isDown && !_hotkeyKeyDown)
                {
                    // Verify modifiers match
                    if (ModifiersMatch())
                    {
                        ConsoleUi.Log("hook", $"Hotkey MATCH — firing HotkeyPressed and HotkeyIndexPressed");
                        _hotkeyKeyDown = true;
                        int capturedIndex = 0; // Windows hook only supports single hotkey
                        ThreadPool.QueueUserWorkItem(_ =>
                        {
                            HotkeyPressed?.Invoke();
                            HotkeyIndexPressed?.Invoke(capturedIndex);
                        });
                        // Block the keystroke so it doesn't reach the console/StreamShell
                        return new IntPtr(1);
                    }
                    else
                    {
                        ConsoleUi.Log("hook", $"Hotkey key down but modifier mismatch");
                    }
                }
                else if (isUp && _hotkeyKeyDown)
                {
                    ConsoleUi.Log("hook", $"Hotkey release — firing HotkeyReleased and HotkeyIndexReleased");
                    _hotkeyKeyDown = false;
                    int capturedIndex = 0;
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        HotkeyReleased?.Invoke();
                        HotkeyIndexReleased?.Invoke(capturedIndex);
                    });
                    // Block the keystroke release too
                    return new IntPtr(1);
                }
            }
        }
        else if (nCode < 0)
        {
            ConsoleUi.Log("hook", $"nCode < 0 ({nCode}), no hotkey configured");
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    /// <summary>
    /// Returns true if the currently pressed modifiers exactly match the configured modifiers.
    /// </summary>
    private bool ModifiersMatch()
    {
        foreach (var mod in Enum.GetValues<Modifier>())
        {
            bool shouldBePressed = _modifiers.Contains(mod);
            bool isPressed = _modifierDown[mod];
            if (shouldBePressed != isPressed)
                return false;
        }
        return true;
    }

    private void MessageLoop()
    {
        using var proc = Process.GetCurrentProcess();
        using var module = proc.MainModule!;

        LowLevelKeyboardProc cb = HookProc;   // keep delegate rooted
        _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, cb,
            GetModuleHandle(module.ModuleName), 0);

        if (_hookHandle == IntPtr.Zero)
            throw new InvalidOperationException(
                $"SetWindowsHookEx failed: {Marshal.GetLastWin32Error()}");

        while (!_disposed && GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        UnhookWindowsHookEx(_hookHandle);
    }

    public void Dispose()
    {
        _disposed = true;
        PostThreadMessage((uint)_thread.ManagedThreadId, 0x0012 /*WM_QUIT*/,
            IntPtr.Zero, IntPtr.Zero);
    }

    // ── P/Invoke ──────────────────────────────────────────────────────

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode, scanCode, flags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam, lParam, time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc fn, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    static extern IntPtr GetModuleHandle(string name);
    [DllImport("user32.dll")] static extern int GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("user32.dll")] static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr w, IntPtr l);
}