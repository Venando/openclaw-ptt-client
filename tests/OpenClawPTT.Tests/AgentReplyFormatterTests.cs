using System;
using System.Text;
using OpenClawPTT;
using OpenClawPTT.Services;
using Xunit;

namespace OpenClawPTT.Tests;

/// <summary>
/// Tests for AgentReplyFormatter word wrapping and streaming behavior.
/// </summary>
public class AgentReplyFormatterTests
{
    private sealed class StringWriterTextOutput : ITextOutput
    {
        private readonly StringBuilder _sb = new StringBuilder();
        public string Result => _sb.ToString();

        public void Write(string? text) => _sb.Append(text);
        public void WriteLine() => _sb.AppendLine();
        public int WindowWidth => 80;
    }

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

    [Fact]
    public void ProcessDelta_SingleWord_OutputsText()
    {
        var output = new StringWriterTextOutput();
        var formatter = new AgentReplyFormatter(prefix: "  ", rightMarginIndent: 10, prefixAlreadyPrinted: false, consoleWidth: 80, output: output);
        formatter.ProcessDelta("hello");
        formatter.Finish();
        Assert.Equal("hello", output.Result.Trim());
    }

    [Fact]
    public void ProcessDelta_SingleWordWithExplicitWidth_OutputsText()
    {
        var output = new StringWriterTextOutput();
        var formatter = new AgentReplyFormatter(prefix: "  ", rightMarginIndent: 10, prefixAlreadyPrinted: false, consoleWidth: 80, output: output);
        formatter.ProcessDelta("hello");
        formatter.Finish();
        Assert.Contains("hello", output.Result);
    }

    [Fact]
    public void ProcessDelta_MultipleIncrementalChunks_AccumulatesAndFinishes()
    {
        var output = new StringWriterTextOutput();
        var formatter = new AgentReplyFormatter(prefix: "  ", rightMarginIndent: 10, prefixAlreadyPrinted: false, consoleWidth: 80, output: output);
        formatter.ProcessDelta("Hello ");
        formatter.ProcessDelta("World");
        formatter.Finish();
        Assert.Contains("Hello World", output.Result.Replace("\r\n", "\n"));
    }

    [Fact]
    public void ProcessDelta_WithITextOutput_BuildsCorrectOutput()
    {
        var output = new StringWriterTextOutput();
        var formatter = new AgentReplyFormatter(prefix: "  ", rightMarginIndent: 5, prefixAlreadyPrinted: false, consoleWidth: 40, output: output);
        formatter.ProcessDelta("This is a test");
        formatter.Finish();
        Assert.NotEmpty(output.Result);
        // Verify output was written via ITextOutput, not Console
        Assert.DoesNotContain("\x1B[", output.Result); // No ANSI escape codes
    }

    [Fact]
    public void ProcessDelta_ExplicitConsoleWidth_UsesProvidedWidth()
    {
        var output = new StringWriterTextOutput();
        // Wide console
        var formatter = new AgentReplyFormatter(prefix: "  ", rightMarginIndent: 10, prefixAlreadyPrinted: false, consoleWidth: 120, output: output);
        formatter.ProcessDelta("Hello World");
        formatter.Finish();
        Assert.Contains("Hello World", output.Result);
    }

    [Fact]
    public void ProcessDelta_NarrowConsoleWidth_WrapsText()
    {
        var output = new StringWriterTextOutput();
        // Very narrow console to force wrapping
        var formatter = new AgentReplyFormatter(prefix: "  ", rightMarginIndent: 10, prefixAlreadyPrinted: false, consoleWidth: 20, output: output);
        formatter.ProcessDelta("This is a long word that should wrap");
        formatter.Finish();
        // Should contain newlines due to wrapping
        Assert.Contains("\n", output.Result.Replace("\r\n", "\n"));
    }

    [Fact]
    public void ProcessDelta_Finish_EndsWithNewline()
    {
        var output = new StringWriterTextOutput();
        var formatter = new AgentReplyFormatter(prefix: "  ", rightMarginIndent: 10, prefixAlreadyPrinted: false, consoleWidth: 80, output: output);
        formatter.ProcessDelta("test");
        formatter.Finish();
        Assert.EndsWith(Environment.NewLine, output.Result);
    }
}
