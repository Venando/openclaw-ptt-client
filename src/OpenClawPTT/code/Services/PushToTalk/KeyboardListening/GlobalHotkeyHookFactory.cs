using System.Runtime.InteropServices;

namespace OpenClawPTT;

internal static class GlobalHotkeyHookFactory
{
    public static IGlobalHotkeyHook Create()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new WindowsHotkeyHook();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return new LinuxEvdevHotkeyHook();

        throw new PlatformNotSupportedException(
            $"Global hotkeys are not supported on {RuntimeInformation.OSDescription}");
    }
}