using System.Text.Json;
using Moq;
using OpenClawPTT;

namespace OpenClawPTT.Tests.Gateway;

/// <summary>
/// Tests for GatewayClient event firing and audio tag stripping.
/// Uses internal test methods exposed on GatewayClient for direct testing
/// without requiring a real WebSocket connection.
/// </summary>
public class GatewayClientEventsTests : IDisposable
{
    private readonly AppConfig _config;
    private readonly DeviceIdentity _device;
    private readonly GatewayEventSource _eventSource;
    private readonly GatewayClient _client;
    private readonly List<string> _fullEvents = new();
    private readonly List<string> _audioEvents = new();

    public GatewayClientEventsTests()
    {
        _config = new AppConfig
        {
            CustomDataDir = Path.GetTempPath(),
            GatewayUrl = "wss://127.0.0.1:9999/non-existent",
            AuthToken = "test"
        };
        _device = new DeviceIdentity(_config.DataDir);
        _device.EnsureKeypair();

        _eventSource = new GatewayEventSource();
        _client = new GatewayClient(_config, _device, _eventSource);

        _eventSource.AgentReplyFull += t => _fullEvents.Add(t);
        _eventSource.AgentReplyAudio += t => _audioEvents.Add(t);
    }

    public void Dispose() { _client.Dispose(); }

    // ─── helpers ─────────────────────────────────────────────────────

    /// <summary>Builds a full session.message event frame JSON string.</summary>
    private static string BuildSessionMessage(params (string type, string? text, string? audio)[] blocks)
    {
        var blocksArray = blocks
            .Select(b => b.type switch
            {
                "text" => JsonSerializer.Serialize(new { type = "text", text = b.text }),
                "audio" => JsonSerializer.Serialize(new { type = "audio", audio = b.audio }),
                _ => null
            })
            .Where(x => x != null)
            .Select(x => JsonDocument.Parse(x!).RootElement)
            .ToArray();

        var root = new Dictionary<string, object>
        {
            ["type"] = "event",
            ["event"] = "session.message",
            ["payload"] = new Dictionary<string, object>
            {
                ["message"] = new Dictionary<string, object>
                {
                    ["role"] = "assistant",
                    ["content"] = blocksArray
                }
            }
        };

        using var ms = new MemoryStream();
        using var writer = new Utf8JsonWriter(ms);
        WriteEvent(writer, root);
        writer.Flush();
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    private static void WriteEvent(Utf8JsonWriter w, Dictionary<string, object> obj)
    {
        w.WriteStartObject();
        foreach (var kv in obj)
        {
            w.WritePropertyName(kv.Key);
            WriteValue(w, kv.Value);
        }
        w.WriteEndObject();
    }

    private static void WriteValue(Utf8JsonWriter w, object value)
    {
        switch (value)
        {
            case string s: w.WriteStringValue(s); break;
            case JsonElement e: e.WriteTo(w); break;
            case Dictionary<string, object> d:
                w.WriteStartObject();
                foreach (var kv in d) { w.WritePropertyName(kv.Key); WriteValue(w, kv.Value); }
                w.WriteEndObject();
                break;
            case JsonElement[] arr:
                w.WriteStartArray();
                foreach (var e in arr) e.WriteTo(w);
                w.WriteEndArray();
                break;
            default: w.WriteNullValue(); break;
        }
    }

    // ─── StripAudioTags ──────────────────────────────────────────────

    [Theory]
    [InlineData("[audio]hello[/audio]", "hello")]
    [InlineData("plain text", "plain text")]
    [InlineData("hello [audio]world[/audio] how", "hello world how")]
    public void StripAudioTags_VariousInputs(string input, string expected)
    {
        var result = GatewayClient.TestStripAudioTags(input);
        Assert.Equal(expected, result);
    }

    // ─── ExtractMarkedContent ────────────────────────────────────────

    [Theory]
    [InlineData("[audio]x[/audio]", true, false, "x", "")]
    [InlineData("[text]y[/text]", false, true, "", "y")]
    [InlineData("[text]a[/text] [audio]b[/audio]", true, true, "b", "a")]
    public void ExtractMarkedContent_VariousInputs(string input, bool hasAudio, bool hasText, string audioText, string textContent)
    {
        var (hA, hT, aT, tC) = _client.TestExtractMarkedContent(input);
        Assert.Equal(hasAudio, hA);
        Assert.Equal(hasText, hT);
        Assert.Equal(audioText, aT);
        Assert.Equal(textContent, tC);
    }

    // ─── Event firing ────────────────────────────────────────────────

    [Fact]
    public void AgentReplyFull_FiresOnce_ForOneTextBlock()
    {
        // Use direct method to bypass _framing.ResolveEventWaiter (no socket/ConnectAsync)
        var content = BuildSessionMessage(("text", "hello world", null));
        var payloadJson = ExtractPayload(content);
        _client.TestHandleSessionMessageDirect(payloadJson);
        Assert.Single(_fullEvents);
        Assert.Equal("hello world", _fullEvents[0]);
    }

    [Fact]
    public void AgentReplyAudio_FiresOnce_ForAudioBlock()
    {
        var content = BuildSessionMessage(("audio", null, "ping"));
        var payloadJson = ExtractPayload(content);
        _client.TestHandleSessionMessageDirect(payloadJson);
        Assert.Single(_audioEvents);
        Assert.Equal("ping", _audioEvents[0]);
    }

    [Fact]
    public void AgentReplyAudio_And_AgentReplyFull_BothFire_ForAudioInTextBlock()
    {
        // [audio] tags in a type="text" block → both TTS and display fire
        var content = BuildSessionMessage(("text", "[audio]ping[/audio]", null));
        var payloadJson = ExtractPayload(content);
        _client.TestHandleSessionMessageDirect(payloadJson);
        Assert.Single(_fullEvents);
        Assert.Equal("ping", _fullEvents[0]);
        Assert.Single(_audioEvents);
        Assert.Equal("ping", _audioEvents[0]);
    }

    [Fact]
    public void AgentReplyFull_FiresTwice_ForTwoTextBlocks()
    {
        var content = BuildSessionMessage(
            ("text", "hello", null),
            ("text", "world", null)
        );
        var payloadJson = ExtractPayload(content);
        _client.TestHandleSessionMessageDirect(payloadJson);
        Assert.Equal(2, _fullEvents.Count);
    }

    private static string ExtractPayload(string fullEventJson)
    {
        using var doc = JsonDocument.Parse(fullEventJson);
        return doc.RootElement.GetProperty("payload").GetRawText();
    }
}
