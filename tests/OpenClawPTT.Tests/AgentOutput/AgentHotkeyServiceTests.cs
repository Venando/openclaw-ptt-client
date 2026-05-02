using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Moq;
using OpenClawPTT.Services;
using Xunit;

namespace OpenClawPTT.Tests.AgentOutput;

[Collection("AgentHotkeyService")]
public class AgentHotkeyServiceTests : IDisposable
{
    public AgentHotkeyServiceTests()
    {
        // Reset registry between tests
        AgentRegistry.SetAgents(new List<AgentInfo>());
    }

    public void Dispose()
    {
        AgentRegistry.SetAgents(new List<AgentInfo>());
    }

    private static AgentInfo MakeAgent(string id, string name = "", bool isDefault = false)
    {
        return new AgentInfo
        {
            AgentId = id,
            Name = string.IsNullOrEmpty(name) ? id : name,
            SessionKey = $"agent:{id}:main",
            IsDefault = isDefault
        };
    }

    [Fact]
    public void HotkeyPressed_ActiveAgent_StartsRecording()
    {
        var agents = new List<AgentInfo> { MakeAgent("a1", "Alpha", isDefault: true) };
        AgentRegistry.SetAgents(agents);

        var pttCtrl = new Mock<IPttController>();
        var svc = new AgentHotkeyService(pttCtrl.Object, Mock.Of<ITextMessageSender>(), Mock.Of<IStreamShellHost>(), new AppConfig());

        svc.HandleHotkeyPressed(0); // a1 is active by default

        pttCtrl.Verify(p => p.StartRecording(), Times.Once);
    }

    [Fact]
    public void HotkeyPressed_InactiveAgent_SwitchesAndShowsMessage()
    {
        var agents = new List<AgentInfo>
        {
            MakeAgent("a1", "Alpha", isDefault: true),
            MakeAgent("a2", "Beta"),
        };
        AgentRegistry.SetAgents(agents);

        var pttCtrl = new Mock<IPttController>();
        var sender = new Mock<ITextMessageSender>();
        var shellHost = new Mock<IStreamShellHost>();
        var svc = new AgentHotkeyService(pttCtrl.Object, sender.Object, shellHost.Object, new AppConfig());

        // Press hotkey for Beta (index 1), which is not active
        svc.HandleHotkeyPressed(1);

        pttCtrl.Verify(p => p.StartRecording(), Times.Never);
        shellHost.Verify(h => h.AddMessage(It.Is<string>(m => m.Contains("Beta"))), Times.AtLeastOnce);
    }

    [Fact]
    public void HotkeyPressed_InactiveThenActive_FirstSwitchThenRecord()
    {
        var agents = new List<AgentInfo>
        {
            MakeAgent("a1", "Alpha", isDefault: true),
            MakeAgent("a2", "Beta"),
        };
        AgentRegistry.SetAgents(agents);

        var pttCtrl = new Mock<IPttController>();
        var svc = new AgentHotkeyService(pttCtrl.Object, Mock.Of<ITextMessageSender>(), Mock.Of<IStreamShellHost>(), new AppConfig());

        // First press: switch to Beta
        svc.HandleHotkeyPressed(1);
        Assert.Equal("agent:a2:main", AgentRegistry.ActiveSessionKey);

        // Second press: Beta is now active → start recording
        svc.HandleHotkeyPressed(1);
        pttCtrl.Verify(p => p.StartRecording(), Times.Once);
    }

    [Fact]
    public void HotkeyReleased_HoldToTalkActiveAgent_StopsRecording()
    {
        var agents = new List<AgentInfo> { MakeAgent("a1", "Alpha", isDefault: true) };
        AgentRegistry.SetAgents(agents);

        var pttCtrl = new Mock<IPttController>();
        var cfg = new AppConfig { HoldToTalk = true };
        var svc = new AgentHotkeyService(pttCtrl.Object, Mock.Of<ITextMessageSender>(), Mock.Of<IStreamShellHost>(), cfg);

        svc.HandleHotkeyReleased(0);

        pttCtrl.Verify(p => p.StopRecording(), Times.Once);
    }

    [Fact]
    public void HotkeyReleased_NotHoldToTalk_DoesNotStop()
    {
        var agents = new List<AgentInfo> { MakeAgent("a1", "Alpha", isDefault: true) };
        AgentRegistry.SetAgents(agents);

        var pttCtrl = new Mock<IPttController>();
        var cfg = new AppConfig { HoldToTalk = false };
        var svc = new AgentHotkeyService(pttCtrl.Object, Mock.Of<ITextMessageSender>(), Mock.Of<IStreamShellHost>(), cfg);

        svc.HandleHotkeyReleased(0);

        pttCtrl.Verify(p => p.StopRecording(), Times.Never);
    }

    [Fact]
    public void HotkeyPressed_IndexOutOfRange_DoesNothing()
    {
        var agents = new List<AgentInfo> { MakeAgent("a1", "Alpha", isDefault: true) };
        AgentRegistry.SetAgents(agents);

        var pttCtrl = new Mock<IPttController>();
        var svc = new AgentHotkeyService(pttCtrl.Object, Mock.Of<ITextMessageSender>(), Mock.Of<IStreamShellHost>(), new AppConfig());

        svc.HandleHotkeyPressed(5); // out of range
        pttCtrl.Verify(p => p.StartRecording(), Times.Never);
    }

    [Fact]
    public void HandleActiveHotkeyPressed_StartsRecording()
    {
        var pttCtrl = new Mock<IPttController>();
        var svc = new AgentHotkeyService(pttCtrl.Object, Mock.Of<ITextMessageSender>(), Mock.Of<IStreamShellHost>(), new AppConfig { HoldToTalk = true });

        svc.HandleActiveHotkeyPressed();
        pttCtrl.Verify(p => p.StartRecording(), Times.Once);
    }

    [Fact]
    public void HandleActiveHotkeyReleased_StopsWhenHoldToTalk()
    {
        var pttCtrl = new Mock<IPttController>();
        var svc = new AgentHotkeyService(pttCtrl.Object, Mock.Of<ITextMessageSender>(), Mock.Of<IStreamShellHost>(), new AppConfig { HoldToTalk = true });

        svc.HandleActiveHotkeyReleased();
        pttCtrl.Verify(p => p.StopRecording(), Times.Once);
    }
}

/// <summary>
/// Disables parallelization for AgentHotkeyService tests because AgentRegistry
/// is a static singleton and cannot be safely shared across concurrent test threads.
/// </summary>
[CollectionDefinition("AgentHotkeyService", DisableParallelization = true)]
public class AgentHotkeyServiceCollection : ICollectionFixture<object>
{
}
