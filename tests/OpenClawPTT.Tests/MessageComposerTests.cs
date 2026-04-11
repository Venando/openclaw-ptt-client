using OpenClawPTT.Services;
using Xunit;

namespace OpenClawPTT.Tests;

public class MessageComposerTests
{
    private readonly MessageComposer _composer = new();

    [Fact]
    public void ComposeOutgoing_TextOnlyConfig_ReturnsOriginalText()
    {
        var cfg = new AppConfig { AudioResponseMode = "text-only" };
        var result = _composer.ComposeOutgoing("hello", cfg);
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
        var result = _composer.ComposeOutgoing("hello", cfg);
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
        var result = _composer.ComposeOutgoing("hello", cfg);
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
        var result = _composer.ComposeOutgoing("hello", cfg);
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
        var result = _composer.ComposeOutgoing("hello", cfg);
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
        var result = _composer.ComposeOutgoing("test message", cfg);
        Assert.StartsWith("[Speak this]", result);
    }

    [Fact]
    public void Interface_IMessageComposer_CanBeUsedForMocking()
    {
        // Verify the interface can be used for DI/mocking
        IMessageComposer composer = new MessageComposer();
        var cfg = new AppConfig { AudioResponseMode = "text-only" };
        var result = composer.ComposeOutgoing("test", cfg);
        Assert.Equal("test", result);
    }
}
