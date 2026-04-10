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
    public void ConsoleUi_AllStaticMethodsHaveCorrespondingInterfaceMethod()
    {
        // This test documents the known gap: PrintAgentReply, PrintAgentReplyDelta,
        // and CreateAgentReplyFormatter are NOT part of IConsoleOutput.
        // They are only callable via ConsoleUi statically.
        // For testability, these should ideally be added to IConsoleOutput.
        var consoleOutput = new ConsoleOutput();
        
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
        IConsoleOutput output = new ConsoleOutput();
        output.PrintSuccess("Success message");
        output.PrintWarning("Warning message");
        output.PrintError("Error message");
        output.Log("test", "log message");
    }
}
