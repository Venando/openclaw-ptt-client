using System.Runtime.InteropServices;

namespace OpenClawPTT.VisualFeedback;

internal static class VisualFeedbackFactory
{
    public static IVisualFeedback Create(AppConfig config)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new WindowsVisualFeedback(config);

        // Non-Windows platforms get a no-op implementation
        return new NoVisualFeedback();
    }
}