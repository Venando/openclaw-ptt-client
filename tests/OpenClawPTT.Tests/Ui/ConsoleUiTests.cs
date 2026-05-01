using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Moq;
using OpenClawPTT.Services;
using Xunit;

namespace OpenClawPTT.Tests;

/// <summary>
/// Tests for ConsoleUi static methods via StreamShell message routing.
/// Since ConsoleUi no longer wraps an IConsole, all display methods route
/// through ShellMsg to IStreamShellHost.AddMessage when a shell is attached,
/// or are silent when no shell is attached.
/// </summary>
[Collection("ConsoleUi")]
public class ConsoleUiTests : IDisposable
{
    private readonly Mock<IStreamShellHost> _shellHost;

    public ConsoleUiTests()
    {
        _shellHost = new Mock<IStreamShellHost>(MockBehavior.Strict);
        ConsoleUi.SetStreamShellHost(_shellHost.Object);
    }

    public void Dispose()
    {
        ConsoleUi.SetStreamShellHost(null);
    }

    #region PrintBanner

    [Fact]
    public void PrintBanner_CallsShellMsgFiveTimes()
    {
        _shellHost.Setup(h => h.AddMessage(It.IsAny<string>())).Verifiable();
        ConsoleUi.PrintBanner();
        _shellHost.Verify(h => h.AddMessage(It.IsAny<string>()), Times.Exactly(5));
    }

    [Fact]
    public void PrintBanner_SendsCyanMarkup()
    {
        var messages = new List<string>();
        _shellHost.Setup(h => h.AddMessage(It.IsAny<string>()))
            .Callback<string>(m => messages.Add(m));
        ConsoleUi.PrintBanner();
        Assert.All(messages.Skip(1).Take(3), m => Assert.Contains("[cyan]", m));
    }

    #endregion

    #region PrintHelpMenu

    [Fact]
    public void PrintHelpMenu_HoldToTalkMode_IncludesCorrectModeDescription()
    {
        var messages = new List<string>();
        _shellHost.Setup(h => h.AddMessage(It.IsAny<string>()))
            .Callback<string>(m => messages.Add(m));
        ConsoleUi.PrintHelpMenu("Alt+=", true);
        var allOutput = string.Join("", messages.Where(s => s != null));
        Assert.Contains("Hold-to-talk", allOutput);
        Assert.Contains("Alt+=", allOutput);
    }

    [Fact]
    public void PrintHelpMenu_ToggleMode_IncludesCorrectModeDescription()
    {
        var messages = new List<string>();
        _shellHost.Setup(h => h.AddMessage(It.IsAny<string>()))
            .Callback<string>(m => messages.Add(m));
        ConsoleUi.PrintHelpMenu("Space", false);
        var allOutput = string.Join("", messages.Where(s => s != null));
        Assert.Contains("Toggle recording", allOutput);
        Assert.Contains("Space", allOutput);
    }

    [Fact]
    public void PrintHelpMenu_SendsGreenMarkup()
    {
        _shellHost.Setup(h => h.AddMessage(It.IsAny<string>())).Verifiable();
        ConsoleUi.PrintHelpMenu("Alt+=", true);
        _shellHost.Verify(h => h.AddMessage(It.Is<string>(m => m.Contains("[green]"))), Times.AtLeastOnce);
    }

    #endregion

    #region PrintSuccess

    [Fact]
    public void PrintSuccess_SendsGreenMarkup()
    {
        _shellHost.Setup(h => h.AddMessage("[green]  ✓ test message[/]"));
        ConsoleUi.PrintSuccess("test message");
        _shellHost.VerifyAll();
    }

    #endregion

    #region PrintError

    [Fact]
    public void PrintError_SendsRedMarkup()
    {
        _shellHost.Setup(h => h.AddMessage("[red]  ✗ something went wrong[/]"));
        ConsoleUi.PrintError("something went wrong");
        _shellHost.VerifyAll();
    }

    #endregion

    #region PrintWarning

    [Fact]
    public void PrintWarning_SendsYellowMarkup()
    {
        _shellHost.Setup(h => h.AddMessage("[yellow]  ⚠ check settings[/]"));
        ConsoleUi.PrintWarning("check settings");
        _shellHost.VerifyAll();
    }

    #endregion

    #region PrintInfo

    [Fact]
    public void PrintInfo_SendsGreyMarkup()
    {
        _shellHost.Setup(h => h.AddMessage("[grey]  listening...[/]"));
        ConsoleUi.PrintInfo("listening...");
        _shellHost.VerifyAll();
    }

    #endregion

    #region PrintRecordingIndicator

    [Fact]
    public void PrintRecordingIndicator_WhenIsRecordingTrue_SendsRedMarkup()
    {
        _shellHost.Setup(h => h.AddMessage(It.Is<string>(m => m.Contains("[red]"))));
        ConsoleUi.PrintRecordingIndicator(true, "Alt+=", true);
        _shellHost.VerifyAll();
    }

    [Fact]
    public void PrintRecordingIndicator_WhenIsRecordingTrue_SendsRecIndicator()
    {
        _shellHost.Setup(h => h.AddMessage(It.Is<string>(m => m.Contains("REC"))));
        ConsoleUi.PrintRecordingIndicator(true, "Alt+=", true);
        _shellHost.VerifyAll();
    }

    [Fact]
    public void PrintRecordingIndicator_WhenIsRecordingFalse_DoesNotSendMessage()
    {
        ConsoleUi.PrintRecordingIndicator(false, "Alt+=", true);
        _shellHost.Verify(h => h.AddMessage(It.IsAny<string>()), Times.Never);
    }

    #endregion

    #region PrintGatewayError

    [Fact]
    public void PrintGatewayError_SendsRedMarkup()
    {
        _shellHost.Setup(h => h.AddMessage("[red]  Gateway error: timeout[/]"));
        ConsoleUi.PrintGatewayError("timeout");
        _shellHost.VerifyAll();
    }

    [Fact]
    public void PrintGatewayError_WithDetailCode_SendsDetailMessage()
    {
        _shellHost.Setup(h => h.AddMessage("[red]  Gateway error: auth failed[/]"));
        _shellHost.Setup(h => h.AddMessage("  Detail code : AUTH_401"));
        ConsoleUi.PrintGatewayError("auth failed", "AUTH_401", null);
        _shellHost.VerifyAll();
    }

    [Fact]
    public void PrintGatewayError_WithRecommendedStep_SendsRecommendedMessage()
    {
        _shellHost.Setup(h => h.AddMessage("[red]  Gateway error: auth failed[/]"));
        _shellHost.Setup(h => h.AddMessage("  Recommended : Run openclaw auth login"));
        ConsoleUi.PrintGatewayError("auth failed", null, "Run openclaw auth login");
        _shellHost.VerifyAll();
    }

    #endregion

    #region Log / LogOk / LogError

    [Fact]
    public void Log_FormatsTagAndMessage()
    {
        _shellHost.Setup(h => h.AddMessage("[grey]  [[AGENT]] hello[/]"));
        ConsoleUi.Log("AGENT", "hello");
        _shellHost.VerifyAll();
    }

    [Fact]
    public void Log_SendsGreyMarkup()
    {
        _shellHost.Setup(h => h.AddMessage(It.Is<string>(m => m.Contains("[grey]"))));
        ConsoleUi.Log("TAG", "msg");
        _shellHost.VerifyAll();
    }

    [Fact]
    public void LogOk_FormatsTagAndMessage()
    {
        _shellHost.Setup(h => h.AddMessage("[green]  [[OK]] done[/]"));
        ConsoleUi.LogOk("OK", "done");
        _shellHost.VerifyAll();
    }

    [Fact]
    public void LogOk_SendsGreenMarkup()
    {
        _shellHost.Setup(h => h.AddMessage(It.Is<string>(m => m.Contains("[green]"))));
        ConsoleUi.LogOk("OK", "done");
        _shellHost.VerifyAll();
    }

    [Fact]
    public void LogError_FormatsTagAndMessage()
    {
        _shellHost.Setup(h => h.AddMessage("[red]  [[ERR]] failed[/]"));
        ConsoleUi.LogError("ERR", "failed");
        _shellHost.VerifyAll();
    }

    [Fact]
    public void LogError_SendsRedMarkup()
    {
        _shellHost.Setup(h => h.AddMessage(It.Is<string>(m => m.Contains("[red]"))));
        ConsoleUi.LogError("ERR", "failed");
        _shellHost.VerifyAll();
    }

    #endregion

    #region PrintAgentReply

    [Fact]
    public void PrintAgentReply_WithShell_SendsCyanPrefixMarkup()
    {
        _shellHost.Setup(h => h.AddMessage("[cyan]  \U0001f916 Agent: [/]Hello world"));
        ConsoleUi.PrintAgentReply("  \U0001f916 Agent: ", "Hello world");
        _shellHost.VerifyAll();
    }

    #endregion

    #region Non-input methods are silent without shell

    [Fact]
    public void PrintBanner_WithoutShell_DoesNotThrow()
    {
        ConsoleUi.SetStreamShellHost(null);
        ConsoleUi.PrintBanner(); // Should not throw even with no shell
    }

    [Fact]
    public void PrintSuccess_WithoutShell_DoesNotThrow()
    {
        ConsoleUi.SetStreamShellHost(null);
        ConsoleUi.PrintSuccess("test");
    }

    #endregion
}

/// <summary>
/// Marks this collection as non-parallelizable because ConsoleUi uses static
/// shared state that cannot safely handle concurrent test threads.
/// </summary>
[CollectionDefinition("ConsoleUi", DisableParallelization = true)]
public class ConsoleUiCollection : ICollectionFixture<object>
{
}
