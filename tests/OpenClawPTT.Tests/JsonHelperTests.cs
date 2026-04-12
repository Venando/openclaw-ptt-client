using System.Text.Json;
using OpenClawPTT.TTS.Providers;
using Xunit;

namespace OpenClawPTT.Tests;

public class JsonHelperTests
{
    // Test 1: Empty string → returns false, no exception
    [Fact]
    public void TryParseJson_EmptyString_ReturnsFalse()
    {
        var result = JsonHelper.TryParseJson("", out var doc, out var type);

        Assert.False(result);
        Assert.Null(doc);
        Assert.Null(type);
    }

    [Fact]
    public void TryParseJson_WhitespaceOnly_ReturnsFalse()
    {
        var result = JsonHelper.TryParseJson("   ", out var doc, out var type);

        Assert.False(result);
        Assert.Null(doc);
        Assert.Null(type);
    }

    [Fact]
    public void TryParseJson_Null_ReturnsFalse()
    {
        var result = JsonHelper.TryParseJson(null, out var doc, out var type);

        Assert.False(result);
        Assert.Null(doc);
        Assert.Null(type);
    }

    // Test 2: Valid JSON object → returns true, parses correctly
    [Fact]
    public void TryParseJson_ValidObjectWithType_ReturnsTrue()
    {
        var json = "{\"type\":\"speak\",\"text\":\"hello\"}";

        var result = JsonHelper.TryParseJson(json, out var doc, out var type);

        Assert.True(result);
        Assert.NotNull(doc);
        Assert.Equal("speak", type);
        doc!.Dispose();
    }

    // Test 3: Trailing garbage after JSON → returns true, parses object (ignores garbage)
    [Fact]
    public void TryParseJson_TrailingGarbage_ReturnsTrue()
    {
        var json = "{\"type\":\"speak\"} extra";

        var result = JsonHelper.TryParseJson(json, out var doc, out var type);

        Assert.True(result);
        Assert.NotNull(doc);
        Assert.Equal("speak", type);
        doc!.Dispose();
    }

    [Fact]
    public void TryParseJson_TrailingGarbageOtherForm_ReturnsTrue()
    {
        // "garbage" can mean various forms of non-JSON trailing content
        var json = "{\"type\":\"speak\"} some_extra_text";

        var result = JsonHelper.TryParseJson(json, out var doc, out var type);

        Assert.True(result);
        Assert.NotNull(doc);
        Assert.Equal("speak", type);
        doc!.Dispose();
    }

    // Test 4: Missing `type` property → returns false
    [Fact]
    public void TryParseJson_MissingType_ReturnsFalse()
    {
        var json = "{}";

        var result = JsonHelper.TryParseJson(json, out var doc, out var type);

        Assert.False(result);
        Assert.Null(doc);
        Assert.Null(type);
    }

    [Fact]
    public void TryParseJson_MissingType_WithOtherProperties_ReturnsFalse()
    {
        var json = "{\"text\":\"hello\"}";

        var result = JsonHelper.TryParseJson(json, out var doc, out var type);

        Assert.False(result);
        Assert.Null(doc);
        Assert.Null(type);
    }

    // Test 5: `type` is non-string value (e.g., {"type": 123}) → returns false
    [Fact]
    public void TryParseJson_TypeIsInteger_ReturnsFalse()
    {
        var json = "{\"type\":123}";

        var result = JsonHelper.TryParseJson(json, out var doc, out var type);

        Assert.False(result);
        Assert.Null(doc);
        Assert.Null(type);
    }

    [Fact]
    public void TryParseJson_TypeIsBoolean_ReturnsFalse()
    {
        var json = "{\"type\":true}";

        var result = JsonHelper.TryParseJson(json, out var doc, out var type);

        Assert.False(result);
        Assert.Null(doc);
        Assert.Null(type);
    }

    [Fact]
    public void TryParseJson_TypeIsArray_ReturnsFalse()
    {
        var json = "{\"type\":[\"speak\"]}";

        var result = JsonHelper.TryParseJson(json, out var doc, out var type);

        Assert.False(result);
        Assert.Null(doc);
        Assert.Null(type);
    }

    [Fact]
    public void TryParseJson_TypeIsObject_ReturnsFalse()
    {
        var json = "{\"type\":{\"nested\":\"object\"}}";

        var result = JsonHelper.TryParseJson(json, out var doc, out var type);

        Assert.False(result);
        Assert.Null(doc);
        Assert.Null(type);
    }

    // Test 6: `type` is null → returns false
    [Fact]
    public void TryParseJson_TypeIsNull_ReturnsFalse()
    {
        var json = "{\"type\":null}";

        var result = JsonHelper.TryParseJson(json, out var doc, out var type);

        Assert.False(result);
        Assert.Null(doc);
        Assert.Null(type);
    }

    // Test 7: Nested JSON with `type` at root → returns true
    [Fact]
    public void TryParseJson_NestedJsonWithTypeAtRoot_ReturnsTrue()
    {
        var json = "{\"type\":\"speak\",\"data\":{\"inner\":\"value\"}}";

        var result = JsonHelper.TryParseJson(json, out var doc, out var type);

        Assert.True(result);
        Assert.NotNull(doc);
        Assert.Equal("speak", type);
        doc!.Dispose();
    }

    [Fact]
    public void TryParseJson_NestedJsonTypeAtRootOnly_ReturnsTrue()
    {
        // type is at root; inner objects don't matter
        var json = "{\"type\":\"event\",\"payload\":{\"type\":\"nested\"}}";

        var result = JsonHelper.TryParseJson(json, out var doc, out var type);

        Assert.True(result);
        Assert.NotNull(doc);
        Assert.Equal("event", type);
        doc!.Dispose();
    }

    // Test 8: `type` is empty string → handled gracefully (returns false)
    [Fact]
    public void TryParseJson_TypeIsEmptyString_ReturnsFalse()
    {
        var json = "{\"type\":\"\"}";

        var result = JsonHelper.TryParseJson(json, out var doc, out var type);

        Assert.False(result);
        Assert.Null(doc);
        Assert.Null(type);
    }

    [Fact]
    public void TryParseJson_TypeIsWhitespaceOnly_ReturnsFalse()
    {
        var json = "{\"type\":\"   \"}";

        var result = JsonHelper.TryParseJson(json, out var doc, out var type);

        Assert.False(result);
        Assert.Null(doc);
        Assert.Null(type);
    }

    // Additional edge cases for coverage
    [Fact]
    public void TryParseJson_InvalidJson_ReturnsFalse()
    {
        var json = "not valid json at all";

        var result = JsonHelper.TryParseJson(json, out var doc, out var type);

        Assert.False(result);
        Assert.Null(doc);
        Assert.Null(type);
    }

    [Fact]
    public void TryParseJson_TruncatedJson_ReturnsFalse()
    {
        var json = "{\"type\":";

        var result = JsonHelper.TryParseJson(json, out var doc, out var type);

        Assert.False(result);
        Assert.Null(doc);
        Assert.Null(type);
    }

    [Fact]
    public void TryParseJson_ArrayInsteadOfObject_ReturnsFalse()
    {
        var json = "[\"speak\"]";

        var result = JsonHelper.TryParseJson(json, out var doc, out var type);

        Assert.False(result);
        Assert.Null(doc);
        Assert.Null(type);
    }

    [Fact]
    public void TryParseJson_TypeWithTrailingWhitespace_ReturnsTrue()
    {
        var json = "{\"type\":\"speak\"}   ";

        var result = JsonHelper.TryParseJson(json, out var doc, out var type);

        Assert.True(result);
        Assert.NotNull(doc);
        Assert.Equal("speak", type);
        doc!.Dispose();
    }
}
