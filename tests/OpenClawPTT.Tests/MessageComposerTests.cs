using OpenClawPTT.Services;
using Xunit;

namespace OpenClawPTT.Tests;

public class MessageComposerTests
{
    [Fact]
    public void ComposeOutgoing_TextOnlyConfig_ReturnsOriginalText()
    {
        var cfg = new AppConfig { AudioResponseMode = "text-only" };
        var result = MessageComposer.ComposeOutgoing("hello", cfg);
        Assert.Equal("hello", result);
    }

    [Fact]
    public void ComposeOutgoing_AudioEnabledWithWrapPrompt_PrependsPrompt()
    {
        var cfg = new AppConfig
        {
            AudioResponseMode = "both",
            AudioWrapPrompt = "[Wrap spoken]"
        };
        var result = MessageComposer.ComposeOutgoing("hello", cfg);
        Assert.Equal("[Wrap spoken]\n\nhello", result);
    }

    [Fact]
    public void ComposeOutgoing_AudioEnabledEmptyWrapPrompt_ReturnsOriginalText()
    {
        var cfg = new AppConfig
        {
            AudioResponseMode = "both",
            AudioWrapPrompt = ""
        };
        var result = MessageComposer.ComposeOutgoing("hello", cfg);
        Assert.Equal("hello", result);
    }

    [Fact]
    public void ComposeOutgoing_AudioEnabledNullWrapPrompt_ReturnsOriginalText()
    {
        var cfg = new AppConfig
        {
            AudioResponseMode = "both",
            AudioWrapPrompt = default
        };
        var result = MessageComposer.ComposeOutgoing("hello", cfg);
        Assert.Equal("hello", result);
    }

    [Fact]
    public void ComposeOutgoing_TextOnlyWithWrapPrompt_ReturnsOriginalText()
    {
        // text-only means IsAudioEnabled is false
        var cfg = new AppConfig
        {
            AudioResponseMode = "text-only",
            AudioWrapPrompt = "[Should not be used]"
        };
        var result = MessageComposer.ComposeOutgoing("hello", cfg);
        Assert.Equal("hello", result);
    }

    [Fact]
    public void ComposeOutgoing_AudioOnly_UsesAudioWrapPrompt()
    {
        var cfg = new AppConfig
        {
            AudioResponseMode = "audio-only",
            AudioWrapPrompt = "[Speak this]"
        };
        var result = MessageComposer.ComposeOutgoing("test message", cfg);
        Assert.StartsWith("[Speak this]", result);
    }
}
