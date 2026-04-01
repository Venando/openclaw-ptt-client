using System.Runtime.InteropServices;
using OpenClawPTT;

namespace OpenClawPTT.VisualFeedback;

internal static class VisualFeedbackFactory
{
    public static IVisualFeedback Create(AppConfig config)
    {
        if (!config.VisualFeedbackEnabled)
            return new NoVisualFeedback(config);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new WindowsVisualFeedback(config);

        return new NoVisualFeedback(config);
    }
}