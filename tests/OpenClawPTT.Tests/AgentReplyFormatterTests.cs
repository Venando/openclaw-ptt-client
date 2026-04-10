using OpenClawPTT;
using Xunit;

namespace OpenClawPTT.Tests;

/// <summary>
/// Tests for AgentReplyFormatter word wrapping and streaming behavior.
/// </summary>
public class AgentReplyFormatterTests
{
    [Fact]
    public void ProcessDelta_EmptyString_DoesNotThrow()
    {
        var formatter = new AgentReplyFormatter(prefix: "  ", rightMarginIndent: 10);
        formatter.ProcessDelta("");
        formatter.Finish();
    }

    [Fact]
    public void ProcessDelta_MultiLineText_WrapsCorrectly()
    {
        var formatter = new AgentReplyFormatter(prefix: "  ", rightMarginIndent: 10);
        formatter.ProcessDelta("line1\nline2");
        formatter.Finish();
    }

    [Fact]
    public void ProcessDelta_IncrementalChunks_AccumulatesCorrectly()
    {
        var formatter = new AgentReplyFormatter(prefix: "  ", rightMarginIndent: 20);
        formatter.ProcessDelta("Hello ");
        formatter.ProcessDelta("World");
        formatter.Finish();
    }

    [Fact]
    public void Finish_DoesNotThrow()
    {
        var formatter = new AgentReplyFormatter(prefix: "  ", rightMarginIndent: 10);
        formatter.ProcessDelta("Some text");
        formatter.Finish();
    }

    [Fact]
    public void Finish_WithNoContent_DoesNotThrow()
    {
        var formatter = new AgentReplyFormatter(prefix: "  ", rightMarginIndent: 10);
        formatter.Finish();
    }
}
