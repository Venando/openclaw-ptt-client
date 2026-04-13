namespace OpenClawPTT.Services;

public interface IMessageComposer
{
    string ComposeOutgoing(string text, AppConfig config);
}

public sealed class MessageComposer : IMessageComposer
{
    /// <summary>
    /// Prepends audio wrap prompt when TTS is enabled.
    /// </summary>
    public string ComposeOutgoing(string text, AppConfig config)
    {
        if (config.IsAudioEnabled && !string.IsNullOrEmpty(config.AudioWrapPrompt))
        {
            return config.AudioWrapPrompt + "\n\n" + text;
        }
        return text;
    }
}