using OpenClawPTT.Services;
using Xunit;

namespace OpenClawPTT.Tests;

/// <summary>
/// Tests for ToolDisplayHandler edge cases.
/// </summary>
public class ToolDisplayHandlerTests
{
    [Fact]
    public void Handle_UnknownTool_ShowsGenericIcon()
    {
        var handler = new ToolDisplayHandler(rightMarginIndent: 10);
        // Should not throw, should use generic 🔧 icon for unknown tools
        handler.Handle("unknown_tool", "{\"key\":\"value\"}");
    }

    [Fact]
    public void Handle_NullArguments_DoesNotThrow()
    {
        var handler = new ToolDisplayHandler(rightMarginIndent: 10);
        handler.Handle("read", null!);
    }

    [Fact]
    public void Handle_EmptyArguments_DoesNotThrow()
    {
        var handler = new ToolDisplayHandler(rightMarginIndent: 10);
        handler.Handle("read", "");
    }

    [Fact]
    public void Handle_MalformedJson_DoesNotThrow()
    {
        var handler = new ToolDisplayHandler(rightMarginIndent: 10);
        handler.Handle("read", "{not valid json");
    }

    [Fact]
    public void Handle_ReadTool_DisplaysFilePath()
    {
        var handler = new ToolDisplayHandler(rightMarginIndent: 10);
        handler.Handle("read", "{\"file\":\"/path/to/file.txt\"}");
    }

    [Fact]
    public void Handle_ReadTool_WithOffsetLimit_DisplaysRange()
    {
        var handler = new ToolDisplayHandler(rightMarginIndent: 10);
        handler.Handle("read", "{\"file\":\"file.txt\",\"offset\":10,\"limit\":50}");
    }

    [Fact]
    public void Handle_WriteTool_DisplaysPath()
    {
        var handler = new ToolDisplayHandler(rightMarginIndent: 10);
        handler.Handle("write", "{\"path\":\"/tmp/out.txt\",\"content\":\"hello world\"}");
    }

    [Fact]
    public void Handle_ExecTool_DisplaysCommand()
    {
        var handler = new ToolDisplayHandler(rightMarginIndent: 10);
        handler.Handle("exec", "{\"command\":\"ls -la\"}");
    }

    [Fact]
    public void Handle_WebFetchTool_StripsProtocolPrefix()
    {
        var handler = new ToolDisplayHandler(rightMarginIndent: 10);
        handler.Handle("web_fetch", "{\"url\":\"https://example.com/path\"}");
    }

    [Fact]
    public void Handle_SessionsSpawnTool_DisplaysTask()
    {
        var handler = new ToolDisplayHandler(rightMarginIndent: 10);
        handler.Handle("sessions_spawn", "{\"label\":\"test\",\"task\":\"do something\"}");
    }

    [Fact]
    public void Handle_SubagentsTool_ListAction()
    {
        var handler = new ToolDisplayHandler(rightMarginIndent: 10);
        handler.Handle("subagents", "{\"action\":\"list\"}");
    }

    [Fact]
    public void Handle_SubagentsTool_KillAction()
    {
        var handler = new ToolDisplayHandler(rightMarginIndent: 10);
        handler.Handle("subagents", "{\"action\":\"kill\",\"target\":\"session-123\"}");
    }

    [Fact]
    public void Handle_GenericKvpTool_DoesNotThrow()
    {
        var handler = new ToolDisplayHandler(rightMarginIndent: 10);
        handler.Handle("process", "{\"action\":\"write\",\"data\":\"test\"}");
    }
}
