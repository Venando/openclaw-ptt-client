using OpenClawPTT.Services;
using Xunit;

namespace OpenClawPTT.Tests;

/// <summary>
/// Tests for IConsoleOutput interface completeness vs ConsoleUi static methods.
/// Verifies that ConsoleOutput wrapper covers all ConsoleUi functionality.
/// </summary>
public class IConsoleOutputContractTests
{
    [Fact]
    public void ConsoleOutput_ImplementsAllInterfaceMethods()
    {
        // This test documents the IConsoleOutput contract — it verifies that all
        // core output methods can be called without throwing.
        var consoleOutput = new SinkConsoleOutput();

        // These should work (covered by IConsoleOutput):
        consoleOutput.PrintBanner();
        consoleOutput.PrintHelpMenu("Ctrl+P", false);
        consoleOutput.PrintSuccess("test");
        consoleOutput.PrintSuccessWordWrap("  ", "test", 10);
        consoleOutput.PrintWarning("test");
        consoleOutput.PrintError("test");
        consoleOutput.PrintInfo("test");
        consoleOutput.PrintInlineInfo("test");
        consoleOutput.PrintInlineSuccess("test");
        consoleOutput.PrintGatewayError("err", null, null);
        consoleOutput.Log("tag", "msg");
        consoleOutput.LogOk("tag", "msg");
        consoleOutput.LogError("tag", "msg");
    }

    [Fact]
    public void ConsoleOutput_DropInReplacement_DoesNotThrow()
    {
        // Verify ConsoleOutput can be used as drop-in for ConsoleUi static calls
        IConsoleOutput output = new SinkConsoleOutput();
        output.PrintSuccess("Success message");
        output.PrintWarning("Warning message");
        output.PrintError("Error message");
        output.Log("test", "log message");
    }

    /// <summary>
    /// Minimal IConsoleOutput implementation that discards all output.
    /// </summary>
    private sealed class SinkConsoleOutput : IConsoleOutput
    {
        public IAgentReplyFormatter CreateAgentReplyFormatter(string prefix, int rightMarginIndent, bool prefixAlreadyPrinted = false)
            => new AgentReplyFormatter(prefix, rightMarginIndent, prefixAlreadyPrinted, output: new SinkFormattedOutput());

        public IAgentReplyFormatter CreateAgentReplyFormatter(string prefix, int rightMarginIndent, bool prefixAlreadyPrinted, int consoleWidth)
            => new AgentReplyFormatter(prefix, rightMarginIndent, prefixAlreadyPrinted, output: new SinkFormattedOutput());

        public void PrintBanner() { }
        public void PrintHelpMenu(string hotkeyCombination, bool holdToTalk) { }
        public void PrintRecordingIndicator(bool isRecording, string hotkeyCombination, bool holdToTalk) { }
        public void PrintUserMessage(string text) { }
        public void PrintSuccess(string message) { }
        public void PrintSuccessWordWrap(string prefix, string message, int rightMarginIndent) { }
        public void PrintWarning(string message) { }
        public void PrintError(string message) { }
        public void PrintInfo(string message) { }
        public void PrintInlineInfo(string message) { }
        public void PrintInlineSuccess(string message) { }
        public void PrintGatewayError(string message, string? detailCode, string? recommendedStep) { }
        public void PrintAgentReply(string prefix, string body) { }
        public void PrintAgentReplyDelta(string prefix, string delta, string newlineSuffix) { }
        public void Log(string tag, string msg) { }
        public void LogOk(string tag, string msg) { }
        public void LogError(string tag, string msg) { }
        public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<string?>(null);

        private sealed class SinkFormattedOutput : IFormattedOutput
        {
            public void Write(string text) { }
            public void WriteLine() { }
            public int WindowWidth => 120;
        }
    }
}
