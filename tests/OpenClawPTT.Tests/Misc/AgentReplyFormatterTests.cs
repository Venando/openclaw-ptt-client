using System;
using System.Text;
using System.Threading;
using Xunit;

namespace OpenClawPTT.Tests;

/// <summary>
/// Tests for AgentReplyFormatter word wrapping and streaming behavior.
/// </summary>
public class AgentReplyFormatterTests
{
    private sealed class StringWriterTextOutput : IConsole
    {
        private readonly StringBuilder _sb = new StringBuilder();
        public string Result => _sb.ToString();

        public void Write(string? text) => _sb.Append(text);
        public void WriteLine(string? text = null) => _sb.AppendLine(text);
        public int WindowWidth => 80;

        // IConsole members not used by AgentReplyFormatter — throw if called
        public ConsoleColor ForegroundColor
        {
            get => ConsoleColor.Gray;
            set => throw new NotSupportedException();
        }
        public void ResetColor() => throw new NotSupportedException();
        public bool KeyAvailable => throw new NotSupportedException();
        public ConsoleKeyInfo ReadKey(bool intercept = false) => throw new NotSupportedException();
        public Encoding OutputEncoding
        {
            get => Encoding.UTF8;
            set => throw new NotSupportedException();
        }
        public bool TreatControlCAsInput
        {
            get => false;
            set => throw new NotSupportedException();
        }
        public IAgentReplyFormatter CreateAgentReplyFormatter(string prefix, int rightMarginIndent, bool prefixAlreadyPrinted = false)
            => throw new NotSupportedException();
        public IAgentReplyFormatter CreateAgentReplyFormatter(string prefix, int rightMarginIndent, bool prefixAlreadyPrinted, int consoleWidth)
            => throw new NotSupportedException();
        public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    [Fact]
    public void ProcessDelta_EmptyString_DoesNotThrow()
    {
        var formatter = AgentReplyFormatter.CreateSytemConsoleFormatter(prefix: "  ", rightMarginIndent: 10);
        formatter.ProcessDelta("");
        formatter.Finish();
    }

    [Fact]
    public void ProcessDelta_MultiLineText_WrapsCorrectly()
    {
        var formatter = AgentReplyFormatter.CreateSytemConsoleFormatter(prefix: "  ", rightMarginIndent: 10);
        formatter.ProcessDelta("line1\nline2");
        formatter.Finish();
    }

    [Fact]
    public void ProcessDelta_IncrementalChunks_AccumulatesCorrectly()
    {
        var formatter = AgentReplyFormatter.CreateSytemConsoleFormatter(prefix: "  ", rightMarginIndent: 20);
        formatter.ProcessDelta("Hello ");
        formatter.ProcessDelta("World");
        formatter.Finish();
    }

    [Fact]
    public void Finish_DoesNotThrow()
    {
        var formatter = AgentReplyFormatter.CreateSytemConsoleFormatter(prefix: "  ", rightMarginIndent: 10);
        formatter.ProcessDelta("Some text");
        formatter.Finish();
    }

    [Fact]
    public void Finish_WithNoContent_DoesNotThrow()
    {
        var formatter = AgentReplyFormatter.CreateSytemConsoleFormatter(prefix: "  ", rightMarginIndent: 10);
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

    // ─── New stability tests ──────────────────────────────────────────────────

    [Fact]
    public void Constructor_2Param_DoesNotThrowInHeadlessEnvironment()
    {
        // Uses ConsoleTextOutput internally, which catches Console.WindowWidth exceptions.
        // This test confirms the 2-param constructor initialises without throwing.
        var formatter = AgentReplyFormatter.CreateSytemConsoleFormatter(prefix: "  ", prefixAlreadyPrinted: false);
        formatter.ProcessDelta("hello");
        formatter.Finish(); // must not throw
    }

    [Fact]
    public void Constructor_3ParamWithExplicitWidth_Works()
    {
        var output = new StringWriterTextOutput();
        var formatter = new AgentReplyFormatter(prefix: "  ", rightMarginIndent: 5, prefixAlreadyPrinted: false, consoleWidth: 60, output: output);
        formatter.ProcessDelta("test");
        formatter.Finish();
        Assert.Contains("test", output.Result);
    }

    [Fact]
    public void Constructor_WidthZero_UsesFallbackWidth()
    {
        // Width 0 should be normalised to 80 (safe fallback).
        var output = new StringWriterTextOutput();
        var formatter = new AgentReplyFormatter(prefix: "  ", rightMarginIndent: 5, prefixAlreadyPrinted: false, consoleWidth: 0, output: output);
        formatter.ProcessDelta("hello");
        formatter.Finish();
        Assert.Contains("hello", output.Result); // must not crash and must produce output
    }

    [Fact]
    public void Constructor_VeryLargeWidth_HandlesGracefully()
    {
        // Very large width should not cause overflow or crash.
        var output = new StringWriterTextOutput();
        var formatter = new AgentReplyFormatter(prefix: "  ", rightMarginIndent: 5, prefixAlreadyPrinted: false, consoleWidth: 1_000_000, output: output);
        formatter.ProcessDelta("hello world");
        formatter.Finish();
        Assert.Contains("hello world", output.Result);
    }

    [Fact]
    public void Constructor_NullPrefix_HandlesGracefully()
    {
        // Null prefix should not crash; newlineSuffix will also be null-length.
        var output = new StringWriterTextOutput();
        var formatter = new AgentReplyFormatter(prefix: null!, rightMarginIndent: 5, prefixAlreadyPrinted: false, consoleWidth: 80, output: output);
        formatter.ProcessDelta("hello");
        formatter.Finish();
        Assert.Contains("hello", output.Result);
    }

    [Fact]
    public void Finish_WithoutProcessDelta_DoesNotCrash()
    {
        // Finish() without any prior ProcessDelta must not throw.
        var formatter = AgentReplyFormatter.CreateSytemConsoleFormatter(prefix: "  ", rightMarginIndent: 10);
        formatter.Finish(); // should not throw
    }

    [Fact]
    public void ProcessDelta_MultipleIncrementalChunks_NegativeWidth_DoesNotCrash()
    {
        // Negative width should be normalised to 80; no crash expected.
        var output = new StringWriterTextOutput();
        var formatter = new AgentReplyFormatter(prefix: "  ", rightMarginIndent: 5, prefixAlreadyPrinted: false, consoleWidth: -999, output: output);
        formatter.ProcessDelta("chunk1 ");
        formatter.ProcessDelta("chunk2");
        formatter.Finish();
        Assert.Contains("chunk1 chunk2", output.Result.Replace("\r\n", "\n"));
    }
}
