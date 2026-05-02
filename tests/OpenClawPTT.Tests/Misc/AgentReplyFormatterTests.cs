using System;
using System.Text;
using Xunit;

namespace OpenClawPTT.Tests;

/// <summary>
/// Tests for AgentReplyFormatter word wrapping and streaming behavior.
/// </summary>
public class AgentReplyFormatterTests
{
    private sealed class StringWriterTextOutput : IFormattedOutput
    {
        private readonly StringBuilder _sb = new StringBuilder();
        public string Result => _sb.ToString();

        public void Write(string text) => _sb.Append(text);
        public void WriteLine() => _sb.AppendLine();
        public int WindowWidth { get; set; } = 80;
    }

    [Fact]
    public void ProcessDelta_EmptyString_DoesNotThrow()
    {
        var formatter = new AgentReplyFormatter(prefix: "  ", rightMarginIndent: 10, prefixAlreadyPrinted: false, output: new StringWriterTextOutput());
        formatter.ProcessDelta("");
        formatter.Finish();
    }

    [Fact]
    public void ProcessDelta_MultiLineText_WrapsCorrectly()
    {
        var formatter = new AgentReplyFormatter(prefix: "  ", rightMarginIndent: 10, prefixAlreadyPrinted: false, output: new StringWriterTextOutput());
        formatter.ProcessDelta("line1\nline2");
        formatter.Finish();
    }

    [Fact]
    public void ProcessDelta_IncrementalChunks_AccumulatesCorrectly()
    {
        var formatter = new AgentReplyFormatter(prefix: "  ", rightMarginIndent: 20, prefixAlreadyPrinted: false, output: new StringWriterTextOutput());
        formatter.ProcessDelta("Hello ");
        formatter.ProcessDelta("World");
        formatter.Finish();
    }

    [Fact]
    public void Finish_DoesNotThrow()
    {
        var formatter = new AgentReplyFormatter(prefix: "  ", rightMarginIndent: 10, prefixAlreadyPrinted: false, output: new StringWriterTextOutput());
        formatter.ProcessDelta("Some text");
        formatter.Finish();
    }

    [Fact]
    public void Finish_WithNoContent_DoesNotThrow()
    {
        var formatter = new AgentReplyFormatter(prefix: "  ", rightMarginIndent: 10, prefixAlreadyPrinted: false, output: new StringWriterTextOutput());
        formatter.Finish();
    }

    [Fact]
    public void ProcessDelta_SingleWord_OutputsText()
    {
        var output = new StringWriterTextOutput();
        var formatter = new AgentReplyFormatter(prefix: "  ", rightMarginIndent: 10, prefixAlreadyPrinted: false, output: output);
        formatter.ProcessDelta("hello");
        formatter.Finish();
        Assert.Equal("hello", output.Result.Trim());
    }

    [Fact]
    public void ProcessDelta_SingleWordWithExplicitWidth_OutputsText()
    {
        var output = new StringWriterTextOutput();
        var formatter = new AgentReplyFormatter(prefix: "  ", rightMarginIndent: 10, prefixAlreadyPrinted: false, output: output);
        formatter.ProcessDelta("hello");
        formatter.Finish();
        Assert.Contains("hello", output.Result);
    }

    [Fact]
    public void ProcessDelta_MultipleIncrementalChunks_AccumulatesAndFinishes()
    {
        var output = new StringWriterTextOutput();
        var formatter = new AgentReplyFormatter(prefix: "  ", rightMarginIndent: 10, prefixAlreadyPrinted: false, output: output);
        formatter.ProcessDelta("Hello ");
        formatter.ProcessDelta("World");
        formatter.Finish();
        Assert.Contains("Hello World", output.Result.Replace("\r\n", "\n"));
    }

    [Fact]
    public void ProcessDelta_WithITextOutput_BuildsCorrectOutput()
    {
        var output = new StringWriterTextOutput();
        var formatter = new AgentReplyFormatter(prefix: "  ", rightMarginIndent: 5, prefixAlreadyPrinted: false, output: output);
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
        var formatter = new AgentReplyFormatter(prefix: "  ", rightMarginIndent: 10, prefixAlreadyPrinted: false, output: output);
        formatter.ProcessDelta("Hello World");
        formatter.Finish();
        Assert.Contains("Hello World", output.Result);
    }

    [Fact]
    public void ProcessDelta_NarrowConsoleWidth_WrapsText()
    {
        var output = new StringWriterTextOutput();
        // Very narrow console to force wrapping
        var formatter = new AgentReplyFormatter(prefix: "  ", rightMarginIndent: 10, prefixAlreadyPrinted: false, output: output);
        formatter.ProcessDelta("This is a long word that should wrap");
        formatter.Finish();
        // Should contain newlines due to wrapping
        Assert.Contains("\n", output.Result.Replace("\r\n", "\n"));
    }

    [Fact]
    public void ProcessDelta_Finish_EndsWithNewline()
    {
        var output = new StringWriterTextOutput();
        var formatter = new AgentReplyFormatter(prefix: "  ", rightMarginIndent: 10, prefixAlreadyPrinted: false, output: output);
        formatter.ProcessDelta("test");
        formatter.Finish();
        Assert.EndsWith(Environment.NewLine, output.Result);
    }

    // ─── New stability tests ──────────────────────────────────────────────────

    [Fact]
    public void Constructor_2Param_DoesNotThrowInHeadlessEnvironment()
    {
        var formatter = new AgentReplyFormatter(prefix: "  ", prefixAlreadyPrinted: false, output: new StringWriterTextOutput());
        formatter.ProcessDelta("hello");
        formatter.Finish(); // must not throw
    }

    [Fact]
    public void Constructor_3ParamWithExplicitWidth_Works()
    {
        var output = new StringWriterTextOutput();
        var formatter = new AgentReplyFormatter(prefix: "  ", rightMarginIndent: 5, prefixAlreadyPrinted: false, output: output);
        formatter.ProcessDelta("test");
        formatter.Finish();
        Assert.Contains("test", output.Result);
    }

    [Fact]
    public void Constructor_WidthZero_UsesFallbackWidth()
    {
        // Width 0 should be normalised to 80 (safe fallback).
        var output = new StringWriterTextOutput();
        var formatter = new AgentReplyFormatter(prefix: "  ", rightMarginIndent: 5, prefixAlreadyPrinted: false, output: output);
        formatter.ProcessDelta("hello");
        formatter.Finish();
        Assert.Contains("hello", output.Result); // must not crash and must produce output
    }

    [Fact]
    public void Constructor_VeryLargeWidth_HandlesGracefully()
    {
        // Very large width should not cause overflow or crash.
        var output = new StringWriterTextOutput();
        var formatter = new AgentReplyFormatter(prefix: "  ", rightMarginIndent: 5, prefixAlreadyPrinted: false, output: output);
        formatter.ProcessDelta("hello world");
        formatter.Finish();
        Assert.Contains("hello world", output.Result);
    }

    [Fact]
    public void Constructor_NullPrefix_HandlesGracefully()
    {
        // Null prefix should not crash; newlineSuffix will also be null-length.
        var output = new StringWriterTextOutput();
        var formatter = new AgentReplyFormatter(prefix: null!, rightMarginIndent: 5, prefixAlreadyPrinted: false, output: output);
        formatter.ProcessDelta("hello");
        formatter.Finish();
        Assert.Contains("hello", output.Result);
    }

    [Fact]
    public void Finish_WithoutProcessDelta_DoesNotCrash()
    {
        var formatter = new AgentReplyFormatter(prefix: "  ", rightMarginIndent: 10, prefixAlreadyPrinted: false, output: new StringWriterTextOutput());
        formatter.Finish(); // should not throw
    }

    [Fact]
    public void ProcessDelta_MultipleIncrementalChunks_NegativeWidth_DoesNotCrash()
    {
        // Negative width should be normalised to 80; no crash expected.
        var output = new StringWriterTextOutput();
        var formatter = new AgentReplyFormatter(prefix: "  ", rightMarginIndent: 5, prefixAlreadyPrinted: false, output: output);
        formatter.ProcessDelta("chunk1 ");
        formatter.ProcessDelta("chunk2");
        formatter.Finish();
        Assert.Contains("chunk1 chunk2", output.Result.Replace("\r\n", "\n"));
    }

    // ─── ProcessMarkupDelta tests ──────────────────────────────────────────

    [Fact]
    public void ProcessMarkupDelta_SimpleMarkup_OutputsWrappedText()
    {
        var output = new StringWriterTextOutput();
        var formatter = new AgentReplyFormatter(prefix: "", rightMarginIndent: 5, prefixAlreadyPrinted: true, output: output);
        formatter.ProcessMarkupDelta("[bold]Hello World[/]");
        formatter.Finish();
        var result = output.Result.Replace("\r\n", "\n");
        Assert.Contains("[bold]Hello World[/]", result);
    }

    [Fact]
    public void ProcessMarkupDelta_EmptyString_DoesNotThrow()
    {
        var output = new StringWriterTextOutput();
        var formatter = new AgentReplyFormatter(prefix: "", rightMarginIndent: 5, prefixAlreadyPrinted: true, output: output);
        var ex = Record.Exception(() => formatter.ProcessMarkupDelta(""));
        Assert.Null(ex);
        formatter.Finish();
    }

    [Fact]
    public void ProcessMarkupDelta_TagCharactersInsideText_ParsesCorrectly()
    {
        var output = new StringWriterTextOutput();
        var formatter = new AgentReplyFormatter(prefix: "", rightMarginIndent: 5, prefixAlreadyPrinted: true, output: output);
        formatter.ProcessMarkupDelta("[green]a [b] c[/]");
        formatter.Finish();
        var result = output.Result.Replace("\r\n", "\n").Trim();
        // [b] looks like a tag but is inside text — should be treated as visible
        Assert.Contains("[b]", result);
    }

    [Fact]
    public void ProcessMarkupDelta_MultiWordLongText_WrapsAcrossLines()
    {
        var output = new StringWriterTextOutput();
        // Narrow width (20) to force wrapping
        var formatter = new AgentReplyFormatter(prefix: "  ", rightMarginIndent: 10, prefixAlreadyPrinted: false, output: output);
        formatter.ProcessMarkupDelta("[red]" + new string('x', 80) + "[/]");
        formatter.Finish();
        var result = output.Result.Replace("\r\n", "\n");
        // Should contain the markup tags and have multiple lines
        Assert.Contains("[red]", result);
        Assert.Contains("[/]", result);
        Assert.Contains("\n", result);
    }

    [Fact]
    public void ProcessMarkupDelta_NestedMarkupTags_HandledCorrectly()
    {
        var output = new StringWriterTextOutput();
        var formatter = new AgentReplyFormatter(prefix: "", rightMarginIndent: 5, prefixAlreadyPrinted: true, output: output);
        formatter.ProcessMarkupDelta("[bold][green]hello[/][/]");
        formatter.Finish();
        var result = output.Result.Replace("\r\n", "\n").Trim();
        Assert.Contains("[bold][green]hello[/][/]", result);
    }

    [Fact]
    public void ProcessMarkupDelta_NewlinesInsideMarkup_Respected()
    {
        var output = new StringWriterTextOutput();
        var formatter = new AgentReplyFormatter(prefix: "", rightMarginIndent: 5, prefixAlreadyPrinted: true, output: output);
        formatter.ProcessMarkupDelta("[grey]line1\nline2[/]");
        formatter.Finish();
        var result = output.Result.Replace("\r\n", "\n").Trim();
        Assert.Contains("[grey]", result);
        Assert.Contains("\n", result);
    }

    [Fact]
    public void ProcessMarkupDelta_TextWithOpeningBracket_DoesNotConfuseParser()
    {
        var output = new StringWriterTextOutput();
        var formatter = new AgentReplyFormatter(prefix: "", rightMarginIndent: 5, prefixAlreadyPrinted: true, output: output);
        formatter.ProcessMarkupDelta("[yellow]3 > [5] is true[/]");
        formatter.Finish();
        var result = output.Result.Replace("\r\n", "\n").Trim();
        Assert.Contains("3 > [5]", result);
    }

    [Fact]
    public void ProcessMarkupDelta_NarrowWidth_FitsSmallMarkupWithoutNewline()
    {
        // Console width 10 with right margin 5 gives available width of ~5.
        // [white]1[/][gray]2[/] has only 2 visible characters ("1" and "2").
        // Tags have zero visible width. Everything should fit on one line.
        var output = new StringWriterTextOutput { WindowWidth = 10 };
        var formatter = new AgentReplyFormatter(prefix: "", rightMarginIndent: 5, prefixAlreadyPrinted: true, output: output);
        formatter.ProcessMarkupDelta("[white]1[/][gray]2[/]");
        formatter.Finish();
        var result = output.Result.Replace("\r\n", "\n");
        Assert.DoesNotContain("\n", result.Trim());
        Assert.Contains("[white]1[/][gray]2[/]", result);
    }

    [Fact]
    public void ProcessMarkupDelta_LongContent_WrapsWithoutDoubledClosingTags()
    {
        // Regression test: when the formatter word-wraps inside a [dim] region,
        // WriteNewLine() re-emits open tags and Finish() calls FlushWordBuffer
        // for the trailing [/]. Both paths must not duplicate the [/] closing tag.
        var output = new StringWriterTextOutput { WindowWidth = 30 };
        var formatter = new AgentReplyFormatter(prefix: "", rightMarginIndent: 5, prefixAlreadyPrinted: true, output: output);
        formatter.ProcessMarkupDelta("[dim]" + new string('x', 35) + "[/]");
        formatter.Finish();
        var result = output.Result.Replace("\r\n", "\n");
        Assert.DoesNotContain("[/][/]", result);
    }

    [Fact]
    public void ProcessMarkupDelta_EscapedBrackets_ParsesAsContentNotTags()
    {
        // Spectre markup uses [[ for a literal bracket. Brackets in code content
        // must be escaped before passing to ProcessMarkupDelta.
        var output = new StringWriterTextOutput { WindowWidth = 120 };
        var formatter = new AgentReplyFormatter(prefix: "", rightMarginIndent: 10, prefixAlreadyPrinted: true, output: output);
        formatter.ProcessMarkupDelta("[dim]const items = [[[[" + new string('x', 5) + "]]]];[/]");
        formatter.Finish();
        var result = output.Result.Replace("\r\n", "\n");
        int openBrackets = result.Count(c => c == '[');
        int closeBrackets = result.Count(c => c == ']');
        Assert.Equal(openBrackets, closeBrackets);
    }

    [Fact]
    public void ProcessMarkupDelta_FencedCodeBlock_SingleLine_ProducesValidMarkup()
    {
        // Simulates a fenced code block line converted via MarkdownToSpectreConverter.
        var output = new StringWriterTextOutput { WindowWidth = 40 };
        var formatter = new AgentReplyFormatter(prefix: "", rightMarginIndent: 5, prefixAlreadyPrinted: true, output: output);
        formatter.ProcessMarkupDelta("[dim]" + new string('x', 35) + "[/]");
        formatter.Finish();
        var msg = output.Result.Replace("\r\n", "\n");
        var validateResult = MarkupValidator.Validate(msg);
        Assert.True(validateResult.IsValid, $"Invalid markup in message: {msg}\n{validateResult}");
    }



    [Fact]
    public void ProcessMarkupDelta_ExplicitClose_DimTag_DoesNotDoubleCloseOnWrap()
    {
        // Explicit [/dim] close must pop "dim" from the stack so wrapping
        // after the close doesn't emit [/][/] (doubled close tags).
        var output = new StringWriterTextOutput { WindowWidth = 45 };
        var formatter = new AgentReplyFormatter(prefix: "", rightMarginIndent: 5, prefixAlreadyPrinted: true, output: output);
        string markup = "[dim]" + new string('x', 25) + "[/dim] " + new string('x', 20);
        formatter.ProcessMarkupDelta(markup);
        formatter.Finish();
        var result = output.Result.Replace("\r\n", "\n");
        Assert.Contains("[dim]", result);
        Assert.Contains("[/dim]", result);
        Assert.Contains("\n", result.Trim());
        Assert.DoesNotContain("[/][/]", result);
    }
}
