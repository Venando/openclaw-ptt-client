namespace OpenClawPTT;

/// <summary>
/// Abstraction for platform detection, enabling testability.
/// </summary>
public interface IPlatformInfo
{
    string GetPlatform(); // returns "windows", "macos", or "linux"
}

/// <summary>
/// Default implementation that delegates to OperatingSystem checks.
/// </summary>
public sealed class SystemPlatformInfo : IPlatformInfo
{
    public string GetPlatform()
    {
        if (OperatingSystem.IsWindows()) return "windows";
        if (OperatingSystem.IsMacOS()) return "macos";
        return "linux";
    }
}
