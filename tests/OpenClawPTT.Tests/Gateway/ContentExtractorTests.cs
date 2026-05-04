using OpenClawPTT;
using Xunit;

namespace OpenClawPTT.Tests.Gateway;

public class ContentExtractorTests
{
    private readonly IContentExtractor _extractor;

    public ContentExtractorTests()
    {
        _extractor = new ContentExtractor();
    }

    // ─── StripAudioTags tests ──────────────────────────────────────

    [Theory]
    [InlineData("[audio]hello[/audio]", "hello")]
    [InlineData("[audio]test[/audio]", "test")]
    [InlineData("no tags", "no tags")]
    [InlineData("[audio]multi[/audio] word [audio]two[/audio]", "multi word two")]
    public void StripAudioTags_VariousInputs_ExpectedOutput(string input, string expected)
    {
        var result = _extractor.StripAudioTags(input);
        Assert.Equal(expected, result);
    }

    // ─── ExtractMarkedContent tests ─────────────────────────────────

    [Fact]
    public void ExtractMarkedContent_TextOnly_ReturnsTextContent()
    {
        var (hasAudio, hasText, audioText, textContent) =
            _extractor.ExtractMarkedContent("hello world");
        Assert.False(hasAudio);
        Assert.True(hasText);
        Assert.Equal("hello world", textContent);
    }

    [Fact]
    public void ExtractMarkedContent_AudioAndTextTags_SeparatesCorrectly()
    {
        var (hasAudio, hasText, audioText, textContent) =
            _extractor.ExtractMarkedContent("[audio]the audio[/audio][text]the text[/text]");
        Assert.True(hasAudio);
        Assert.True(hasText);
        Assert.Equal("the audio", audioText);
        Assert.Equal("the text", textContent);
    }

    [Fact]
    public void ExtractMarkedContent_MixedAudioText_ReturnsBoth()
    {
        var text = "[audio]voice[/audio] normal [text]marked text[/text] end";
        var (hasAudio, hasText, audioText, textContent) =
            _extractor.ExtractMarkedContent(text);
        Assert.True(hasAudio);
        Assert.True(hasText);
        Assert.Equal("voice", audioText);
        Assert.Equal("marked text", textContent);
    }

    [Fact]
    public void ExtractMarkedContent_AudioOnly_ReturnsAudioContent()
    {
        var (hasAudio, hasText, audioText, textContent) =
            _extractor.ExtractMarkedContent("[audio]voice only[/audio]");
        Assert.True(hasAudio);
        Assert.False(hasText);
        Assert.Equal("voice only", audioText);
        Assert.Empty(textContent);
    }

    [Fact]
    public void ExtractMarkedContent_TextTagOnly_ReturnsTextContent()
    {
        var (hasAudio, hasText, audioText, textContent) =
            _extractor.ExtractMarkedContent("[text]text only[/text]");
        Assert.False(hasAudio);
        Assert.True(hasText);
        Assert.Empty(audioText);
        Assert.Equal("text only", textContent);
    }

    [Fact]
    public void ExtractMarkedContent_EmptyString_ReturnsNoContent()
    {
        var (hasAudio, hasText, audioText, textContent) =
            _extractor.ExtractMarkedContent("");
        Assert.False(hasAudio);
        Assert.False(hasText);
        Assert.Empty(audioText);
        Assert.Empty(textContent);
    }

    [Fact]
    public void ExtractMarkedContent_PartialAudioTag_OpensWithoutClose()
    {
        var (hasAudio, hasText, audioText, textContent) =
            _extractor.ExtractMarkedContent("[audio]partial audio content");
        Assert.True(hasAudio);
        Assert.False(hasText);
        Assert.Equal("partial audio content", audioText);
    }

    [Fact]
    public void ExtractMarkedContent_PartialTextTag_OpensWithoutClose()
    {
        var (hasAudio, hasText, audioText, textContent) =
            _extractor.ExtractMarkedContent("[text]partial text content");
        Assert.False(hasAudio);
        Assert.True(hasText);
        Assert.Equal("partial text content", textContent);
    }

    [Fact]
    public void ExtractMarkedContent_MultilineAudioContent_HandlesCorrectly()
    {
        var multilineAudio = "[audio]line1\nline2\nline3[/audio]";
        var (hasAudio, hasText, audioText, textContent) =
            _extractor.ExtractMarkedContent(multilineAudio);
        Assert.True(hasAudio);
        Assert.False(hasText);
        Assert.Equal("line1\nline2\nline3", audioText);
    }

    [Fact]
    public void ExtractMarkedContent_WhitespaceTrimmed_Correctly()
    {
        var (hasAudio, hasText, audioText, textContent) =
            _extractor.ExtractMarkedContent("[audio]  spaces  [/audio]");
        Assert.True(hasAudio);
        Assert.Equal("spaces", audioText);
    }
}
