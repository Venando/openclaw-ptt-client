using OpenClawPTT.Services;
using Xunit;

namespace OpenClawPTT.Tests;

/// <summary>
/// Tests for ConsoleOutput formatting behavior.
/// Since ConsoleOutput delegates to ConsoleUi (static), these tests
/// verify the IConsoleOutput implementation contract.
/// </summary>
public class ConsoleOutputTests
{
    [Fact]
    public void ConsoleOutput_ImplementsIConsoleOutput()
    {
        var output = new ConsoleOutput();
        Assert.NotNull(output);
    }

    [Fact]
    public void PrintSuccess_DoesNotThrow()
    {
        var output = new ConsoleOutput();
        output.PrintSuccess("Test message");
        Assert.True(true); // reached here = no exception
    }

    [Fact]
    public void PrintError_DoesNotThrow()
    {
        var output = new ConsoleOutput();
        output.PrintError("Error message");
        Assert.True(true);
    }

    [Fact]
    public void PrintSuccessWordWrap_DoesNotThrow()
    {
        var output = new ConsoleOutput();
        output.PrintSuccessWordWrap("  🤖 Agent: ", "Long message content here", 80);
        Assert.True(true);
    }

    [Fact]
    public void PrintGatewayError_DoesNotThrow()
    {
        var output = new ConsoleOutput();
        output.PrintGatewayError("Something went wrong", "ERR_CODE", "Try again");
        Assert.True(true);
    }

    [Fact]
    public void Log_DoesNotThrow()
    {
        var output = new ConsoleOutput();
        output.Log("tag", "message");
        output.LogOk("tag", "message");
        output.LogError("tag", "message");
        Assert.True(true);
    }
}
