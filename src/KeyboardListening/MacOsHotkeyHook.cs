using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace OpenClawPTT;

[SupportedOSPlatform("macos")]
internal sealed class MacOsHotkeyHook : IGlobalHotkeyHook
{
    public event Action? HotkeyPressed;

    private readonly CancellationTokenSource _cts = new();
    private Thread? _thread;
    private volatile bool _altDown;

    // CGEventType
    private const int kCGEventKeyDown = 10;
    private const int kCGEventFlagsChanged = 12;

    // Virtual key codes (macOS)
    private const int kVK_Equal = 0x18;   // '=' key
    private const long kCGEventFlagMaskAlternate = 0x00080000; // Alt/Option

    private GCHandle _selfHandle;   // keeps 'this' rooted while callback is alive
    private IntPtr _eventTap;
    private IntPtr _runLoopSource;

    public void Start()
    {
        _thread = new Thread(RunLoopThread) { IsBackground = true, Name = "MacHotkeyLoop" };
        _thread.Start();
    }

    private void RunLoopThread()
    {
        // Check permission first — tap will be created but dead without it
        if (!AXIsProcessTrusted())
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  [hotkey] Accessibility permission required.");
            Console.WriteLine("  [hotkey] System Settings → Privacy & Security → Accessibility");
            Console.WriteLine("  [hotkey] → enable your terminal app, then restart.");
            Console.ResetColor();
            return;
        }

        _selfHandle = GCHandle.Alloc(this);
        CGEventTapCallBack callback = EventTapCallback;

        // Tap at session level — catches events regardless of focused app
        _eventTap = CGEventTapCreate(
            kCGSessionEventTap,
            kCGHeadInsertEventTap,
            kCGEventTapOptionListenOnly,  // listen-only: we don't modify events
            (1L << kCGEventKeyDown) | (1L << kCGEventFlagsChanged),
            callback,
            GCHandle.ToIntPtr(_selfHandle));

        if (_eventTap == IntPtr.Zero)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  [hotkey] CGEventTapCreate failed — check permissions.");
            Console.ResetColor();
            _selfHandle.Free();
            return;
        }

        _runLoopSource = CFMachPortCreateRunLoopSource(IntPtr.Zero, _eventTap, 0);
        CFRunLoopAddSource(CFRunLoopGetCurrent(), _runLoopSource, kCFRunLoopCommonModes);
        CGEventTapEnable(_eventTap, true);

        // Blocks until CFRunLoopStop is called in Dispose()
        CFRunLoopRun();

        // Cleanup
        CGEventTapEnable(_eventTap, false);
        CFRunLoopRemoveSource(CFRunLoopGetCurrent(), _runLoopSource, kCFRunLoopCommonModes);
        CFRelease(_runLoopSource);
        CFRelease(_eventTap);
        _selfHandle.Free();
    }

    private static IntPtr EventTapCallback(IntPtr proxy, int type, IntPtr eventRef, IntPtr userInfo)
    {
        var self = (MacOsHotkeyHook)GCHandle.FromIntPtr(userInfo).Target!;

        if (type == kCGEventFlagsChanged)
        {
            long flags = CGEventGetFlags(eventRef);
            self._altDown = (flags & kCGEventFlagMaskAlternate) != 0;
            return eventRef;
        }

        if (type == kCGEventKeyDown)
        {
            long keyCode = CGEventGetIntegerValueField(eventRef, kCGKeyboardEventKeycode);
            if (keyCode == kVK_Equal && self._altDown)
                ThreadPool.QueueUserWorkItem(_ => self.HotkeyPressed?.Invoke());
        }

        return eventRef;
    }

    public void Dispose()
    {
        _cts.Cancel();
        // Signal the run loop on the correct thread
        if (_thread?.IsAlive == true)
            CFRunLoopStop(CFRunLoopGetMain()); // approximate — ideally store ref from thread
    }

    // ── P/Invoke ──────────────────────────────────────────────────────

    private delegate IntPtr CGEventTapCallBack(IntPtr proxy, int type, IntPtr eventRef, IntPtr userInfo);

    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string AppServices = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";

    private const int kCGSessionEventTap = 1;
    private const int kCGHeadInsertEventTap = 0;
    private const int kCGEventTapOptionListenOnly = 1;
    private const int kCGKeyboardEventKeycode = 9;
    private static readonly IntPtr kCFRunLoopCommonModes = CFRunLoopCopyCurrentMode(IntPtr.Zero);

    [DllImport(CoreGraphics)] static extern IntPtr CGEventTapCreate(int tap, int place, int options, long eventsOfInterest, CGEventTapCallBack cb, IntPtr userInfo);
    [DllImport(CoreGraphics)] static extern void CGEventTapEnable(IntPtr tap, bool enable);
    [DllImport(CoreGraphics)] static extern long CGEventGetFlags(IntPtr evt);
    [DllImport(CoreGraphics)] static extern long CGEventGetIntegerValueField(IntPtr evt, int field);
    [DllImport(CoreFoundation)] static extern IntPtr CFMachPortCreateRunLoopSource(IntPtr alloc, IntPtr port, int order);
    [DllImport(CoreFoundation)] static extern IntPtr CFRunLoopGetCurrent();
    [DllImport(CoreFoundation)] static extern IntPtr CFRunLoopGetMain();
    [DllImport(CoreFoundation)] static extern void CFRunLoopRun();
    [DllImport(CoreFoundation)] static extern void CFRunLoopStop(IntPtr rl);
    [DllImport(CoreFoundation)] static extern void CFRunLoopAddSource(IntPtr rl, IntPtr source, IntPtr mode);
    [DllImport(CoreFoundation)] static extern void CFRunLoopRemoveSource(IntPtr rl, IntPtr source, IntPtr mode);
    [DllImport(CoreFoundation)] static extern IntPtr CFRunLoopCopyCurrentMode(IntPtr rl);
    [DllImport(CoreFoundation)] static extern void CFRelease(IntPtr cf);
    [DllImport(AppServices)] static extern bool AXIsProcessTrusted();
}