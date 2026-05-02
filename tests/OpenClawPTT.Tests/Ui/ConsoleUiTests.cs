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
        AgentRegistry.SetAgents(new List<AgentInfo>());
        _shellHost = new Mock<IStreamShellHost>(MockBehavior.Strict);
        ConsoleUi.SetStreamShellHost(_shellHost.Object);
    }

    public void Dispose()
    {
        AgentRegistry.SetAgents(new List<AgentInfo>());
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
    public void PrintBanner_SendsDeepSkyBlueMarkup()
    {
        var messages = new List<string>();
        _shellHost.Setup(h => h.AddMessage(It.IsAny<string>()))
            .Callback<string>(m => messages.Add(m));
        ConsoleUi.PrintBanner();
        Assert.All(messages.Skip(1).Take(3), m => Assert.Contains("[deepskyblue3]", m));
    }

    #endregion

    #region PrintHelpMenu

    [Fact]
    public void PrintHelpMenu_HelpLine_SendsCrewAndChatCommandInfo()
    {
        AgentRegistry.SetAgents(new List<AgentInfo>
        {
            new() { AgentId = "a1", Name = "Alpha", SessionKey = "agent:a1:main", IsDefault = true }
        });
        var messages = new List<string>();
        _shellHost.Setup(h => h.AddMessage(It.IsAny<string>()))
            .Callback<string>(m => messages.Add(m));
        ConsoleUi.PrintHelpMenu(new AppConfig { HotkeyCombination = "Alt+=", HoldToTalk = true });
        var allOutput = string.Join("", messages.Where(s => s != null));
        Assert.Contains("PTT Active", allOutput);
        Assert.Contains("/crew", allOutput);
        Assert.Contains("/chat", allOutput);
    }

    [Fact]
    public void PrintHelpMenu_ToggleMode_IncludesCorrectModeDescription()
    {
        AgentRegistry.SetAgents(new List<AgentInfo>
        {
            new() { AgentId = "a1", Name = "Alpha", SessionKey = "agent:a1:main", IsDefault = true }
        });
        var messages = new List<string>();
        _shellHost.Setup(h => h.AddMessage(It.IsAny<string>()))
            .Callback<string>(m => messages.Add(m));
        ConsoleUi.PrintHelpMenu(new AppConfig { HotkeyCombination = "Space", HoldToTalk = false });
        var allOutput = string.Join("", messages.Where(s => s != null));
        Assert.Contains("Toggle", allOutput);
    }

    [Fact]
    public void PrintHelpMenu_SendsDeepSkyBlueMarkup()
    {
        AgentRegistry.SetAgents(new List<AgentInfo>
        {
            new() { AgentId = "a1", Name = "Alpha", SessionKey = "agent:a1:main", IsDefault = true }
        });
        _shellHost.Setup(h => h.AddMessage(It.IsAny<string>())).Verifiable();
        ConsoleUi.PrintHelpMenu(new AppConfig { HotkeyCombination = "Alt+=", HoldToTalk = true });
        _shellHost.Verify(h => h.AddMessage(It.Is<string>(m => m.Contains("[deepskyblue3]"))), Times.AtLeastOnce);
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
