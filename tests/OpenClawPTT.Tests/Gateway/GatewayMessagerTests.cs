using System.Net.WebSockets;
using Moq;
using OpenClawPTT;
using Xunit;

namespace OpenClawPTT.Tests.Gateway;

/// <summary>
/// Note: MessageFraming is not sealed and has virtual members — it can be subclassed
/// for testing, but these tests use a real instance as the factory result to validate
/// that factory injection works correctly (GetFraming returns the provided instance).
/// </summary>
public class GatewayMessagerTests : IDisposable
{
    private readonly AppConfig _cfg;
    private readonly Mock<IClientWebSocket> _mockWs;
    private readonly Mock<IGatewayEventSource> _mockEvents;
    private readonly MessageFraming _realFraming;
    private readonly GatewayMessager _messager;

    public GatewayMessagerTests()
    {
        _cfg = new AppConfig
        {
            CustomDataDir = Path.GetTempPath(),
            GatewayUrl = "wss://127.0.0.1:9999/test",
            AuthToken = "test-token",
            RealTimeReplyOutput = false,
            DebugToolCalls = false
        };
        _mockWs = new Mock<IClientWebSocket>();
        _mockWs.Setup(x => x.State).Returns(WebSocketState.Open);
        _mockEvents = new Mock<IGatewayEventSource>();
        _realFraming = new MessageFraming(_mockWs.Object, _cfg);

        _messager = new GatewayMessager(_mockWs.Object, _mockEvents.Object, _cfg, null, () => _realFraming);
    }

    // ─── Existing tests ───────────────────────────────────────────

    [Fact]
    public void GetFraming_ReturnsProvidedFraming()
    {
        var framing = _messager.GetFraming();
        Assert.Same(_realFraming, framing);
    }

    [Fact]
    public void ClearFraming_DoesNotThrow()
    {
        var exception = Record.Exception(() => _messager.ClearFraming());
        Assert.Null(exception);
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        _messager.Dispose();
        var exception = Record.Exception(() => _messager.Dispose());
        Assert.Null(exception);
    }

    [Fact]
    public void Construct_WithNullFramingFactory_CreatesRealFraming()
    {
        // Use null factory — should fall back to real MessageFraming (backwards compat)
        var messager = new GatewayMessager(_mockWs.Object, _mockEvents.Object, _cfg);
        Assert.NotNull(messager.GetFraming());
        Assert.IsType<MessageFraming>(messager.GetFraming());
        messager.Dispose();
    }

    // ─── ProcessFrame tests ────────────────────────────────────────

    [Fact]
    public void TestProcessFrame_SessionMessageEvent_FiresAgentReplyFull()
    {
        string? capturedText = null;
        _mockEvents.Setup(x => x.RaiseAgentReplyFull(It.IsAny<string>()))
            .Callback<string>(t => capturedText = t);

        var json = @"{""type"":""event"",""event"":""session.message"",""payload"":{""message"":{""role"":""assistant"",""content"":[{""type"":""text"",""text"":""hello world""}]}}}";
        _messager.TestProcessFrame(json);

        Assert.Equal("hello world", capturedText);
    }

    [Fact]
    public void TestProcessFrame_AudioBlock_FiresAgentReplyAudio()
    {
        string? capturedAudio = null;
        _mockEvents.Setup(x => x.RaiseAgentReplyAudio(It.IsAny<string>()))
            .Callback<string>(t => capturedAudio = t);

        var json = @"{""type"":""event"",""event"":""session.message"",""payload"":{""message"":{""role"":""assistant"",""content"":[{""type"":""audio"",""audio"":""test audio content""}]}}}";
        _messager.TestProcessFrame(json);

        Assert.Equal("test audio content", capturedAudio);
    }

    [Fact]
    public void TestProcessFrame_ThinkingBlock_FiresAgentThinking()
    {
        string? capturedThinking = null;
        _mockEvents.Setup(x => x.RaiseAgentThinking(It.IsAny<string>()))
            .Callback<string>(t => capturedThinking = t);

        var json = @"{""type"":""event"",""event"":""session.message"",""payload"":{""message"":{""role"":""assistant"",""content"":[{""type"":""thinking"",""thinking"":"" 分析中""}]}}}";
        _messager.TestProcessFrame(json);

        Assert.Equal(" 分析中", capturedThinking);
    }

    [Fact]
    public void TestProcessFrame_ToolCallBlock_FiresAgentToolCall()
    {
        string? capturedName = null, capturedArgs = null;
        _mockEvents.Setup(x => x.RaiseAgentToolCall(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((n, a) => { capturedName = n; capturedArgs = a; });

        var json = @"{""type"":""event"",""event"":""session.message"",""payload"":{""message"":{""role"":""assistant"",""content"":[{""type"":""toolCall"",""name"":""read"",""arguments"":""{\""path\"":\""a.md\""}""}]}}}";
        _messager.TestProcessFrame(json);

        Assert.Equal("read", capturedName);
        Assert.Contains("path", capturedArgs ?? "");
    }

    [Fact]
    public void TestProcessFrame_AgentStream_DoesNothingInBatchMode()
    {
        var fired = false;
        _mockEvents.Setup(x => x.RaiseAgentReplyDeltaStart()).Callback(() => fired = true);

        var json = @"{""type"":""event"",""event"":""agent"",""payload"":{""data"":{""phase"":""start""}}}";
        _messager.TestProcessFrame(json);

        Assert.False(fired); // batch mode, no delta
    }

    [Fact]
    public void TestProcessFrame_ChatFinal_FiresDeltaEvents()
    {
        var startFired = false;
        _mockEvents.Setup(x => x.RaiseAgentReplyDeltaStart()).Callback(() => startFired = true);

        var json = @"{""type"":""event"",""event"":""chat"",""payload"":{""state"":""final"",""message"":{""content"":[{""type"":""text"",""text"":""final text""}]}}}";
        _messager.TestProcessFrame(json);

        Assert.True(startFired);
    }

    [Fact]
    public void TestProcessFrame_UnknownType_DoesNotThrow()
    {
        var json = @"{""type"":""event"",""event"":""unknown.event"",""payload"":{}}";
        var exception = Record.Exception(() => _messager.TestProcessFrame(json));
        Assert.Null(exception);
    }

    // ─── StripAudioTags tests ──────────────────────────────────────

    [Theory]
    [InlineData("[audio]hello[/audio]", "hello")]
    [InlineData("[audio]test[/audio]", "test")]
    [InlineData("no tags", "no tags")]
    [InlineData("[audio]multi[/audio] word [audio]two[/audio]", "multi word two")]
    public void TestStripAudioTags_VariousInputs_ExpectedOutput(string input, string expected)
    {
        var result = GatewayMessager.TestStripAudioTags(input);
        Assert.Equal(expected, result);
    }

    // ─── ExtractMarkedContent tests ─────────────────────────────────

    [Fact]
    public void TestExtractMarkedContent_TextOnly_ReturnsTextContent()
    {
        var (hasAudio, hasText, audioText, textContent) =
            _messager.TestExtractMarkedContent("hello world");
        Assert.False(hasAudio);
        Assert.True(hasText);
        Assert.Equal("hello world", textContent);
    }

    [Fact]
    public void TestExtractMarkedContent_AudioAndTextTags_SeparatesCorrectly()
    {
        var (hasAudio, hasText, audioText, textContent) =
            _messager.TestExtractMarkedContent("[audio]the audio[/audio][text]the text[/text]");
        Assert.True(hasAudio);
        Assert.True(hasText);
        Assert.Equal("the audio", audioText);
        Assert.Equal("the text", textContent);
    }

    [Fact]
    public void TestExtractMarkedContent_MixedAudioText_ReturnsBoth()
    {
        var text = "[audio]voice[/audio] normal [text]marked text[/text] end";
        var (hasAudio, hasText, audioText, textContent) =
            _messager.TestExtractMarkedContent(text);
        Assert.True(hasAudio);
        Assert.True(hasText);
        Assert.Equal("voice", audioText);
        Assert.Equal("marked text", textContent);
    }

    // ─── TestHandleSessionMessage tests ──────────────────────────────

    [Fact]
    public void TestHandleSessionMessage_TextOnly_FiresAgentReplyFull()
    {
        string? captured = null;
        _mockEvents.Setup(x => x.RaiseAgentReplyFull(It.IsAny<string>()))
            .Callback<string>(t => captured = t);

        var payload = @"{""message"":{""role"":""assistant"",""content"":[{""type"":""text"",""text"":""hello""}]}}";
        _messager.TestHandleSessionMessage(payload);

        Assert.Equal("hello", captured);
    }

    [Fact]
    public void TestHandleSessionMessage_AudioOnly_FiresAgentReplyAudio()
    {
        string? captured = null;
        _mockEvents.Setup(x => x.RaiseAgentReplyAudio(It.IsAny<string>()))
            .Callback<string>(t => captured = t);

        var payload = @"{""message"":{""role"":""assistant"",""content"":[{""type"":""audio"",""audio"":""voice data""}]}}";
        _messager.TestHandleSessionMessage(payload);

        Assert.Equal("voice data", captured);
    }

    [Fact]
    public void TestHandleSessionMessage_NonAssistantRole_Ignores()
    {
        string? captured = null;
        _mockEvents.Setup(x => x.RaiseAgentReplyFull(It.IsAny<string>()))
            .Callback<string>(t => captured = t);

        var payload = @"{""message"":{""role"":""user"",""content"":[{""type"":""text"",""text"":""hello""}]}}";
        _messager.TestHandleSessionMessage(payload);

        Assert.Null(captured);
    }

    [Fact]
    public void TestHandleSessionMessage_NonArrayContent_DoesNotThrow()
    {
        var payload = @"{""message"":{""role"":""assistant"",""content"":""not an array""}}";
        var exception = Record.Exception(() => _messager.TestHandleSessionMessage(payload));
        Assert.Null(exception);
    }

    public void Dispose()
    {
        _messager.Dispose();
    }
}