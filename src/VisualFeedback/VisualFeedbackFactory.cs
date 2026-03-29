using System.Runtime.InteropServices;

namespace OpenClawPTT.VisualFeedback;

internal static class VisualFeedbackFactory
{
    public static IVisualFeedback Create(int visualMode)
    {
        // Mode 0 = no visual feedback regardless of platform
        if (visualMode == 0)
            return new NoVisualFeedback();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new WindowsVisualFeedback(visualMode);

        // Non-Windows platforms get a no-op implementation
        return new NoVisualFeedback();
    }
}