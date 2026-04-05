using System.Runtime.InteropServices;
using OpenClawPTT;

namespace OpenClawPTT.VisualFeedback;

internal static class VisualFeedbackFactory
{
    public static IVisualFeedback Create(AppConfig config)
    {
        // Mode 0 = no visual feedback regardless of platform
        if (config.VisualMode == 0)
            return new NoVisualFeedback();

        // Check if visual feedback is enabled via config
        if (!config.VisualFeedbackEnabled)
            return new NoVisualFeedback(config);
        
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new WindowsVisualFeedback(config);

        // Non-Windows platforms get a no-op implementation
        return new NoVisualFeedback(config);
    }
}