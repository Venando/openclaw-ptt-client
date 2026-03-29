using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

namespace OpenClawPTT.VisualFeedback;

[SupportedOSPlatform("windows")]
internal sealed class WindowsVisualFeedback : IVisualFeedback
{
    private Thread? _uiThread;
    private IntPtr _hwnd = IntPtr.Zero;
    private volatile bool _disposed;
    private readonly ManualResetEvent _windowReady = new ManualResetEvent(false);
    private const int DotSize = 20;
    private readonly string _className = $"OpenClawPTT_VisualFeedback_{Guid.NewGuid():N}";
    private static readonly User32.WndProc _staticWndProc = StaticWndProc;
    private static readonly IntPtr _staticWndProcPtr = Marshal.GetFunctionPointerForDelegate(_staticWndProc);

    public WindowsVisualFeedback()
    {
        _uiThread = new Thread(WindowThread)
        {
            IsBackground = true,
            Name = "VisualFeedbackUI"
        };
        _uiThread.SetApartmentState(ApartmentState.STA);
        _uiThread.Start();
        _windowReady.WaitOne(5000); // Wait up to 5 seconds for window creation
    }

    public void Show()
    {
        if (_disposed || _hwnd == IntPtr.Zero) return;
        User32.ShowWindow(_hwnd, User32.SW_SHOWNOACTIVATE);
        User32.UpdateWindow(_hwnd);
    }

    public void Hide()
    {
        if (_disposed || _hwnd == IntPtr.Zero) return;
        User32.ShowWindow(_hwnd, User32.SW_HIDE);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hwnd != IntPtr.Zero)
        {
            User32.PostMessage(_hwnd, User32.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            _uiThread?.Join(1000);
            _hwnd = IntPtr.Zero;
        }
        _windowReady?.Dispose();
        _uiThread = null;
    }

    private void WindowThread()
    {
        // Register window class
        var wc = new User32.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<User32.WNDCLASSEX>(),
            style = 0,
            lpfnWndProc = _staticWndProcPtr,
            cbClsExtra = 0,
            cbWndExtra = 0,
            hInstance = Kernel32.GetModuleHandle(null),
            hIcon = IntPtr.Zero,
            hCursor = IntPtr.Zero,
            hbrBackground = IntPtr.Zero,
            lpszMenuName = null,
            lpszClassName = _className,
            hIconSm = IntPtr.Zero
        };

        ushort atom = User32.RegisterClassEx(ref wc);
        if (atom == 0)
            throw new InvalidOperationException($"Failed to register window class (Win32 error: {Marshal.GetLastWin32Error()})");

        // Get primary monitor dimensions
        int screenWidth = User32.GetSystemMetrics(User32.SM_CXSCREEN);
        int screenHeight = User32.GetSystemMetrics(User32.SM_CYSCREEN);
        int x = screenWidth - DotSize - 10;
        int y = 10;

        // Create window
        _hwnd = User32.CreateWindowEx(
            User32.WS_EX_TOPMOST | User32.WS_EX_TOOLWINDOW | User32.WS_EX_LAYERED | User32.WS_EX_NOACTIVATE,
            _className,
            "",
            User32.WS_POPUP,
            x, y, DotSize, DotSize,
            IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
            throw new InvalidOperationException($"Failed to create window (Win32 error: {Marshal.GetLastWin32Error()})");

        // Store this instance in window's user data
        GCHandle handle = GCHandle.Alloc(this);
        User32.SetWindowLongPtr(_hwnd, User32.GWLP_USERDATA, GCHandle.ToIntPtr(handle));

        // Set layered window attributes: transparency color key to magenta (RGB(255,0,255))
        User32.SetLayeredWindowAttributes(_hwnd, 0x00FF00FF, 255, User32.LWA_COLORKEY);

        // Set window region to a circle
        IntPtr hRgn = Gdi32.CreateEllipticRgn(0, 0, DotSize, DotSize);
        User32.SetWindowRgn(_hwnd, hRgn, true);

        _windowReady.Set();

        // Message loop
        while (User32.GetMessage(out var msg, IntPtr.Zero, 0, 0) != 0)
        {
            User32.TranslateMessage(ref msg);
            User32.DispatchMessage(ref msg);
        }

        // Cleanup
        User32.UnregisterClass(_className, wc.hInstance);
    }

    private static IntPtr StaticWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        // Retrieve instance from user data
        IntPtr ptr = User32.GetWindowLongPtr(hWnd, User32.GWLP_USERDATA);
        if (ptr != IntPtr.Zero)
        {
            GCHandle handle = GCHandle.FromIntPtr(ptr);
            if (handle.Target is WindowsVisualFeedback instance)
                return instance.InstanceWndProc(hWnd, msg, wParam, lParam);
        }
        return User32.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private IntPtr InstanceWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case User32.WM_PAINT:
                Paint(hWnd);
                break;
            case User32.WM_ERASEBKGND:
                return (IntPtr)1; // We handle background ourselves
            case User32.WM_DESTROY:
                // Clean up GCHandle
                IntPtr ptr = User32.GetWindowLongPtr(hWnd, User32.GWLP_USERDATA);
                if (ptr != IntPtr.Zero)
                {
                    GCHandle.FromIntPtr(ptr).Free();
                    User32.SetWindowLongPtr(hWnd, User32.GWLP_USERDATA, IntPtr.Zero);
                }
                User32.PostQuitMessage(0);
                break;
        }
        return User32.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void Paint(IntPtr hWnd)
    {
        User32.PAINTSTRUCT ps;
        IntPtr hdc = User32.BeginPaint(hWnd, out ps);
        if (hdc == IntPtr.Zero) return;

        // Create a red brush and fill the entire client area as an ellipse
        IntPtr hBrush = Gdi32.CreateSolidBrush(0x0000FF); // BGR format: RGB(255,0,0)
        IntPtr oldBrush = Gdi32.SelectObject(hdc, hBrush);
        // Draw filled ellipse covering the entire window (the window region clips it to a circle)
        Gdi32.Ellipse(hdc, 0, 0, DotSize, DotSize);
        Gdi32.SelectObject(hdc, oldBrush);
        Gdi32.DeleteObject(hBrush);

        User32.EndPaint(hWnd, ref ps);
    }

    private static class User32
    {
        // Constants
        public const int WS_EX_TOPMOST = 0x00000008;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_LAYERED = 0x00080000;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const int WS_POPUP = unchecked((int)0x80000000);
        public const int SW_SHOWNOACTIVATE = 4;
        public const int SW_HIDE = 0;
        public const int WM_CLOSE = 0x0010;
        public const int WM_PAINT = 0x000F;
        public const int WM_ERASEBKGND = 0x0014;
        public const int WM_DESTROY = 0x0002;
        public const int SM_CXSCREEN = 0;
        public const int SM_CYSCREEN = 1;
        public const int GWLP_USERDATA = -21;
        public const int LWA_COLORKEY = 0x00000001;

        // Structs
        [StructLayout(LayoutKind.Sequential)]
        public struct WNDCLASSEX
        {
            public uint cbSize;
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            [MarshalAs(UnmanagedType.LPTStr)]
            public string? lpszMenuName;
            [MarshalAs(UnmanagedType.LPTStr)]
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PAINTSTRUCT
        {
            public IntPtr hdc;
            public bool fErase;
            public RECT rcPaint;
            public bool fRestore;
            public bool fIncUpdate;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] rgbReserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int left, top, right, bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public POINT pt;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int x, y;
        }

        // Delegates
        public delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        // Functions
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern IntPtr CreateWindowEx(int dwExStyle, string lpClassName, string lpWindowName,
            int dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu,
            IntPtr hInstance, IntPtr lpParam);
        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")]
        public static extern bool UpdateWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int nIndex);
        [DllImport("user32.dll")]
        public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);
        [DllImport("user32.dll")]
        public static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        public static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);
        [DllImport("user32.dll")]
        public static extern bool TranslateMessage(ref MSG lpMsg);
        [DllImport("user32.dll")]
        public static extern IntPtr DispatchMessage(ref MSG lpMsg);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool UnregisterClass(string lpClassName, IntPtr hInstance);
        [DllImport("user32.dll")]
        public static extern IntPtr BeginPaint(IntPtr hWnd, out PAINTSTRUCT lpPaint);
        [DllImport("user32.dll")]
        public static extern bool EndPaint(IntPtr hWnd, ref PAINTSTRUCT lpPaint);
        [DllImport("user32.dll")]
        public static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        public static extern void PostQuitMessage(int nExitCode);
    }

    private static class Gdi32
    {
        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateSolidBrush(int crColor);
        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);
        [DllImport("gdi32.dll")]
        public static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);
        [DllImport("gdi32.dll")]
        public static extern bool Ellipse(IntPtr hdc, int left, int top, int right, int bottom);
        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateEllipticRgn(int x1, int y1, int x2, int y2);
    }

    private static class Kernel32
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr GetModuleHandle(string? lpModuleName);
    }
}