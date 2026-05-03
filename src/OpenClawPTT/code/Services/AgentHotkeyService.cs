using Spectre.Console;
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
    private readonly IGatewayService? _gatewayService;
    private readonly IGlobalHotkeyHook? _hook;

    public AgentHotkeyService(
        IPttController pttController,
        ITextMessageSender textSender,
        IStreamShellHost shellHost,
        AppConfig cfg,
        IGatewayService? gatewayService = null,
        IHotkeyHookFactory? hookFactory = null)
    {
        _pttController = pttController;
        _textSender = textSender;
        _shellHost = shellHost;
        _cfg = cfg;
        _gatewayService = gatewayService;

        // Always create a hook — at minimum for Escape key cancellation.
        if (hookFactory != null)
        {
            _hook = hookFactory.Create(new Hotkey(new Key(' '), new HashSet<Modifier>()));
        }
        else
        {
            _hook = GlobalHotkeyHookFactory.Create();
        }

        if (AgentRegistry.Agents.Count > 0)
        {
            RegisterAllAgentHotkeys();
            _hook.HotkeyIndexPressed += OnHotkeyPressed;
            _hook.HotkeyIndexReleased += OnHotkeyReleased;
        }

        _hook.EscapePressed += OnEscapePressed;
        _hook.Start();

        AgentRegistry.PersistedSettingsChanged += OnPersistedSettingsChanged;
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
            return;

        var agent = agents[agentIndex];
        var activeKey = AgentRegistry.ActiveSessionKey;

        if (agent.SessionKey == activeKey)
        {
            // Block Escape from reaching StreamShell while recording
            if (_hook != null) _hook.BlockEscape = true;
            _pttController.StartRecording();
        }
        else
        {
            AgentRegistry.SetActiveAgent(agent.AgentId);
            ConsoleUi.PrintAgentIntroduction(_cfg);
            // Fetch and print session history (fire-and-forget)
            if (_gatewayService != null)
                _ = PrintHistoryAfterSwitchAsync(agent.SessionKey);
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

    private async Task PrintHistoryAfterSwitchAsync(string sessionKey)
    {
        var history = await _gatewayService!.FetchSessionHistoryAsync(sessionKey, limit: 5);
        if (history == null || history.Count == 0)
        {
            return;
        }

        _shellHost.AddMessage("  [grey]── previous messages ──[/]");
        foreach (var entry in history)
        {
            if (entry.Role.Equals("user", StringComparison.OrdinalIgnoreCase))
                _shellHost.AddMessage($"  🟢 [green]You:[/] {Markup.Escape(entry.Content)}");
            else
                _gatewayService!.DisplayAssistantReply(entry.Content);
        }
    }


    private void OnHotkeyPressed(int index) => HandleHotkeyPressed(index);
    private void OnHotkeyReleased(int index) => HandleHotkeyReleased(index);
    private void OnPersistedSettingsChanged()
    {
        RegisterAllAgentHotkeys();
    }

    private void OnEscapePressed()
    {
        // Unblock Escape so the next press reaches StreamShell for input clearing
        if (_hook != null) _hook.BlockEscape = false;
        _pttController.CancelRecording();
    }

    public void Dispose()
    {
        AgentRegistry.PersistedSettingsChanged -= OnPersistedSettingsChanged;

        if (_hook != null)
        {
            _hook.HotkeyIndexPressed -= OnHotkeyPressed;
            _hook.HotkeyIndexReleased -= OnHotkeyReleased;
            _hook.EscapePressed -= OnEscapePressed;
            _hook.Dispose();
        }
    }
}
