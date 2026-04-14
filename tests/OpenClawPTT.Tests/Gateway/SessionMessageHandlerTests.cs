using System.Text.Json;
using OpenClawPTT;

namespace OpenClawPTT.Tests.Gateway;

public class SessionMessageHandlerTests
{
    private static JsonElement ParsePayload(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static JsonElement BuildSessionMessagePayload(params (string type, string? text, string? audio, string? thinking, string? toolName, string? toolArgs)[] blocks)
    {
        var blockParts = new List<string>();
        foreach (var b in blocks)
        {
            string? blockJson = b.type switch
            {
                "text" when b.text != null => "{\"type\":\"text\",\"text\":\"" + b.text.Replace("\"", "\\\"") + "\"}",
                "audio" when b.audio != null => "{\"type\":\"audio\",\"audio\":\"" + b.audio.Replace("\"", "\\\"") + "\"}",
                "thinking" when b.thinking != null => "{\"type\":\"thinking\",\"thinking\":\"" + b.thinking.Replace("\"", "\\\"") + "\"}",
                "toolCall" => "{\"type\":\"toolCall\",\"name\":\"" + (b.toolName?.Replace("\"", "\\\"") ?? "") + "\",\"arguments\":\"" + (b.toolArgs?.Replace("\"", "\\\"") ?? "") + "\"}",
                _ => null
            };
            if (blockJson != null)
                blockParts.Add(blockJson);
        }

        var blocksJson = string.Join(",", blockParts);
        var payloadJson = "{\"message\":{\"role\":\"assistant\",\"content\":[" + blocksJson + "]}}";
        return ParsePayload(payloadJson);
    }

    [Fact]
    public void HandleSessionMessage_TextBlock_ExtractsText()
    {
        var cfg = new AppConfig { RealTimeReplyOutput = false };
        var handler = new SessionMessageHandler(cfg);
        var payload = BuildSessionMessagePayload(("text", "hello world", null, null, null, null));
        var result = handler.HandleSessionMessage(payload);
        Assert.True(result.HasText);
        Assert.Equal("hello world", result.TextContent);
    }

    [Fact]
    public void HandleSessionMessage_AudioTag_ExtractsAudioText()
    {
        var cfg = new AppConfig { RealTimeReplyOutput = false };
        var handler = new SessionMessageHandler(cfg);
        var payload = BuildSessionMessagePayload(("text", "[audio]test audio[/audio]", null, null, null, null));
        var result = handler.HandleSessionMessage(payload);
        Assert.True(result.HasAudio);
        Assert.Equal("test audio", result.AudioText);
    }

    [Fact]
    public void HandleSessionMessage_ThinkingBlock_ExtractsThinking()
    {
        var cfg = new AppConfig { RealTimeReplyOutput = false };
        var handler = new SessionMessageHandler(cfg);
        var payload = BuildSessionMessagePayload(("thinking", null, null, " 分析中", null, null));
        var result = handler.HandleSessionMessage(payload);
        Assert.Equal(" 分析中", result.Thinking);
    }

    [Fact]
    public void HandleSessionMessage_ToolCallBlock_ExtractsNameAndArgs()
    {
        var cfg = new AppConfig { RealTimeReplyOutput = false, DebugToolCalls = false };
        var handler = new SessionMessageHandler(cfg);
        var payload = BuildSessionMessagePayload(("toolCall", null, null, null, "read", "{\"path\":\"a.md\"}"));
        var result = handler.HandleSessionMessage(payload);
        Assert.Equal("read", result.ToolCallName);
        Assert.Contains("path", result.ToolCallArgs ?? "");
    }

    [Theory]
    [InlineData("[audio]hello[/audio]", "hello")]
    [InlineData("no tags", "no tags")]
    public void StripAudioTags_VariousInputs_ExpectedOutput(string input, string expected)
    {
        var result = SessionMessageHandler.StripAudioTags(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ExtractMarkedContent_TextOnly_ReturnsTextContent()
    {
        var (hasAudio, hasText, audioText, textContent) =
            SessionMessageHandler.ExtractMarkedContent("hello world");
        Assert.False(hasAudio);
        Assert.True(hasText);
        Assert.Equal("hello world", textContent);
    }

    [Fact]
    public void ExtractMarkedContent_AudioAndTextTags_SeparatesCorrectly()
    {
        var (hasAudio, hasText, audioText, textContent) =
            SessionMessageHandler.ExtractMarkedContent("[audio]the audio[/audio][text]the text[/text]");
        Assert.True(hasAudio);
        Assert.True(hasText);
        Assert.Equal("the audio", audioText);
        Assert.Equal("the text", textContent);
    }
}