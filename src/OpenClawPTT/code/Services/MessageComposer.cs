namespace OpenClawPTT.Services;

public static class MessageComposer
{
    /// <summary>
    /// Prepends audio wrap prompt when TTS is enabled.
    /// </summary>
    public static string ComposeOutgoing(string text, AppConfig config)
    {
        if (config.IsAudioEnabled && !string.IsNullOrEmpty(config.AudioWrapPrompt))
        {
            return config.AudioWrapPrompt + "\n\n" + text;
        }
        return text;
    }
}