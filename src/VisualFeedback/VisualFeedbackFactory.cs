using System.Runtime.InteropServices;

namespace OpenClawPTT.VisualFeedback;

internal static class VisualFeedbackFactory
{
    public static IVisualFeedback Create()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new WindowsVisualFeedback();

        // Non-Windows platforms get a no-op implementation
        return new NoVisualFeedback();
    }
}