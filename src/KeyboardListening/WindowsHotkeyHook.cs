using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace OpenClawPTT;

[SupportedOSPlatform("windows")]
internal sealed class WindowsHotkeyHook : IGlobalHotkeyHook
{
    public event Action? HotkeyPressed;

    private readonly Thread _thread;
    private volatile IntPtr _hookHandle = IntPtr.Zero;
    private volatile bool _disposed;
    private bool _altDown;

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYUP = 0x0105;
    private const int VK_MENU = 0x12;   // Alt
    private const int VK_OEM_PLUS = 0xBB;   // '=' / '+'

    public WindowsHotkeyHook()
    {
        _thread = new Thread(MessageLoop)
        {
            IsBackground = true,
            Name = "WinHotkeyLoop"
        };
        _thread.SetApartmentState(ApartmentState.STA);
    }

    public void Start() => _thread.Start();

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            int msg = wParam.ToInt32();

            bool isDown = msg is WM_KEYDOWN or WM_SYSKEYDOWN;

            // Use the flags field as the authoritative Alt indicator —
            // bit 5 (0x20) is set by Windows when Alt is held for any key event
            bool altHeld = (info.flags & 0x20) != 0;

            if (info.vkCode == VK_MENU)
                _altDown = isDown;

            // Check BOTH our tracked state and the hardware flags field
            if (isDown && info.vkCode == VK_OEM_PLUS && (_altDown || altHeld))
            {
                ThreadPool.QueueUserWorkItem(_ => HotkeyPressed?.Invoke());
            }
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
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