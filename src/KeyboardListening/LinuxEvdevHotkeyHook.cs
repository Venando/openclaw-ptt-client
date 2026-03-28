using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace OpenClawPTT;

/// <summary>
/// Reads raw evdev events from /dev/input/event* to detect Alt+= globally.
/// Works in TTY without X11/Wayland. Requires membership in the 'input' group:
///   sudo usermod -aG input $USER
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class LinuxEvdevHotkeyHook : IGlobalHotkeyHook
{
    public event Action? HotkeyPressed;

    private readonly CancellationTokenSource _cts = new();
    private Thread? _thread;

    // evdev constants
    private const ushort EV_KEY = 0x01;
    private const ushort KEY_EQUAL = 13;   // physical '=' / '+' key
    private const ushort KEY_LALT = 56;
    private const ushort KEY_RALT = 100;
    private const int VALUE_DOWN = 1;
    private const int VALUE_REPEAT = 2;

    // sizeof(struct input_event) on 64-bit Linux:
    //   struct timeval = 8+8, type = 2, code = 2, value = 4  →  24 bytes
    private const int EVENT_SIZE = 24;
    private int _altDownCount;

    public void Start()
    {
        _thread = new Thread(ReadLoop) { IsBackground = true, Name = "EvdevHotkeyLoop" };
        _thread.Start();
    }

    // ── main device-reading loop ──────────────────────────────────────

    private void ReadLoop()
    {
        var devicePaths = DiscoverKeyboardDevices();

        if (devicePaths.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  [hotkey] No accessible keyboard devices found in /dev/input/.");
            Console.WriteLine("  [hotkey] Fix: sudo usermod -aG input $USER  (then re-login)");
            Console.ResetColor();
            return;
        }

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  [hotkey] Watching {devicePaths.Count} keyboard device(s) for Alt+=");
        Console.ResetColor();

        var ct = _cts.Token;

        // Spawn one reader task per device — they all share altDown via Interlocked
        _altDownCount = 0; // > 0 means at least one Alt key is held
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

        if (code is KEY_LALT or KEY_RALT)
        {
            if (value == VALUE_DOWN)
                Interlocked.Increment(ref _altDownCount);
            else if (value == 0)
                Interlocked.Decrement(ref _altDownCount);
            return;
        }

        if (code == KEY_EQUAL && value == VALUE_DOWN && Volatile.Read(ref _altDownCount) > 0)
            ThreadPool.QueueUserWorkItem(_ => HotkeyPressed?.Invoke());
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

                // EVIOCGBIT(EV_KEY) over key codes — verify it has KEY_EQUAL
                var keyBits = new byte[128];
                if (ioctl(fd, EVIOCGBIT(EV_KEY, keyBits.Length), keyBits) < 0) continue;
                if ((keyBits[KEY_EQUAL / 8] & (1 << (KEY_EQUAL % 8))) == 0) continue;

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