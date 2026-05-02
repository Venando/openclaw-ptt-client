using System;
using System.Linq;
using System.Threading;
using OpenClawPTT.Services;

namespace OpenClawPTT;

/// <summary>
/// Listens to ALL agent hotkeys and routes them:
/// - Active agent's hotkey → recording via PttController
/// - Inactive agent's hotkey → switch agent and show message
/// Falls back to direct PttController trigger when no agents are configured.
/// </summary>
public sealed class AgentHotkeyService : IDisposable
{
    private readonly IPttController _pttController;
    private readonly ITextMessageSender _textSender;
    private readonly IStreamShellHost _shellHost;
    private readonly AppConfig _cfg;
    private readonly IGlobalHotkeyHook? _hook;

    public AgentHotkeyService(
        IPttController pttController,
        ITextMessageSender textSender,
        IStreamShellHost shellHost,
        AppConfig cfg,
        IHotkeyHookFactory? hookFactory = null)
    {
        _pttController = pttController;
        _textSender = textSender;
        _shellHost = shellHost;
        _cfg = cfg;

        // Always create a hook — at minimum for Escape key cancellation.
        // Create hook directly via GlobalHotkeyHookFactory to avoid passing
        // a dummy hotkey through HotkeyHookFactory.Create() which calls
        // SetHotkey(validating mapping) on Windows and crashes.
        _hook = GlobalHotkeyHookFactory.Create();

        if (AgentRegistry.Agents.Count > 0)
        {
            RegisterAllAgentHotkeys();
            _hook.HotkeyIndexPressed += OnHotkeyPressed;
            _hook.HotkeyIndexReleased += OnHotkeyReleased;
        }

        _hook.EscapePressed += OnEscapePressed;
        _hook.Start();
    }

    private void RegisterAllAgentHotkeys()
    {
        if (_hook == null) return;
        var hotkeys = AgentRegistry.AllAgentsWithHotkeys
            .Select(a => HotkeyMapping.Parse(a.Hotkey ?? _cfg.HotkeyCombination))
            .ToList();
        _hook.SetHotkeys(hotkeys);
    }

    /// <summary>Called when a hotkey fires for the agent at the given index.</summary>
    public void HandleHotkeyPressed(int agentIndex)
    {
        var agents = AgentRegistry.Agents;
        if (agentIndex < 0 || agentIndex >= agents.Count)
        {
            ConsoleUi.Log("hotkey", $"HandleHotkeyPressed index {agentIndex} out of range (count {agents.Count})");
            return;
        }

        var agent = agents[agentIndex];
        var activeKey = AgentRegistry.ActiveSessionKey;
        ConsoleUi.Log("hotkey", $"HandleHotkeyPressed: agent={agent.Name} activeKey={activeKey} agentKey={agent.SessionKey}");

        if (agent.SessionKey == activeKey)
        {
            ConsoleUi.Log("hotkey", "Active agent — starting recording via PttController.StartRecording()");
            _pttController.StartRecording();
        }
        else
        {
            ConsoleUi.Log("hotkey", $"Inactive agent — switching to {agent.Name}");
            AgentRegistry.SetActiveAgent(agent.AgentId);
            _shellHost.AddMessage($"[green]  Switched to {agent.Name}. Press hotkey again to record.[/]");
        }
    }

    /// <summary>Called when a hotkey is released for the agent at the given index.</summary>
    public void HandleHotkeyReleased(int agentIndex)
    {
        if (!_cfg.HoldToTalk) return;

        var agents = AgentRegistry.Agents;
        if (agentIndex < 0 || agentIndex >= agents.Count)
            return;

        var agent = agents[agentIndex];
        var activeKey = AgentRegistry.ActiveSessionKey;

        if (agent.SessionKey == activeKey)
        {
            _pttController.StopRecording();
        }
    }

    /// <summary>Fallback when no agents configured — directly trigger recording.</summary>
    public void HandleActiveHotkeyPressed()
    {
        _pttController.StartRecording();
    }

    /// <summary>Fallback when no agents configured — stop recording on release.</summary>
    public void HandleActiveHotkeyReleased()
    {
        if (_cfg.HoldToTalk)
            _pttController.StopRecording();
    }

    private void OnHotkeyPressed(int index) => HandleHotkeyPressed(index);
    private void OnHotkeyReleased(int index) => HandleHotkeyReleased(index);
    private void OnEscapePressed()
    {
        ConsoleUi.Log("hotkey", "EscapePressed — cancelling recording via PttController.CancelRecording()");
        _pttController.CancelRecording();
    }

    public void Dispose()
    {
        if (_hook != null)
        {
            _hook.HotkeyIndexPressed -= OnHotkeyPressed;
            _hook.HotkeyIndexReleased -= OnHotkeyReleased;
            _hook.EscapePressed -= OnEscapePressed;
            _hook.Dispose();
        }
    }
}
