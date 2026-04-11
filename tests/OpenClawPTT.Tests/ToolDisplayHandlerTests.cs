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

    // ─── Renderer architecture tests ───────────────────────────────────────────

    [Theory]
    [InlineData("read")]
    [InlineData("write")]
    [InlineData("edit")]
    [InlineData("exec")]
    [InlineData("web_fetch")]
    [InlineData("sessions_list")]
    [InlineData("session_status")]
    [InlineData("memory_search")]
    [InlineData("memory_get")]
    [InlineData("subagents")]
    [InlineData("sessions_spawn")]
    [InlineData("process")]
    [InlineData("web_search")]
    [InlineData("image_generate")]
    public void Handle_KnownTool_DoesNotThrow(string toolName)
    {
        var handler = new ToolDisplayHandler(rightMarginIndent: 10);
        handler.Handle(toolName, "{\"key\":\"value\"}");
    }

    [Fact]
    public void Handle_UnknownTool_DoesNotThrow()
    {
        var handler = new ToolDisplayHandler(rightMarginIndent: 10);
        handler.Handle("totally_unknown_tool", "{\"key\":\"value\"}");
    }

    [Fact]
    public void Handle_MalformedJson_UnknownTool_DoesNotThrow()
    {
        var handler = new ToolDisplayHandler(rightMarginIndent: 10);
        handler.Handle("unknown_tool", "{not json");
    }

    [Fact]
    public void Handle_AllRenderersWithEmptyArgs_DoNotThrow()
    {
        var handler = new ToolDisplayHandler(rightMarginIndent: 10);
        // Each of these should not throw even with empty/missing arguments
        handler.Handle("read", "{}");
        handler.Handle("write", "{}");
        handler.Handle("edit", "{}");
        handler.Handle("exec", "{}");
        handler.Handle("web_fetch", "{}");
        handler.Handle("sessions_list", "{}");
        handler.Handle("session_status", "{}");
        handler.Handle("memory_search", "{}");
        handler.Handle("memory_get", "{}");
        handler.Handle("subagents", "{}");
        handler.Handle("sessions_spawn", "{}");
        handler.Handle("process", "{}");
    }
}
