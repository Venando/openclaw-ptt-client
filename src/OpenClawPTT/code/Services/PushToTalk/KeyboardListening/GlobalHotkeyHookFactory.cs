using System.Runtime.InteropServices;
using OpenClawPTT.Services;

namespace OpenClawPTT;

internal static class GlobalHotkeyHookFactory
{
    public static IGlobalHotkeyHook Create(IColorConsole console)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new WindowsHotkeyHook();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return new LinuxEvdevHotkeyHook(console);

        throw new PlatformNotSupportedException(
            $"Global hotkeys are not supported on {RuntimeInformation.OSDescription}");
    }
}