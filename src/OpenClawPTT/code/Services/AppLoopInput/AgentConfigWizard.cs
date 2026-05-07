using System;
using System.Threading.Tasks;
using OpenClawPTT.Services;
using Spectre.Console;
using StreamShell;

namespace OpenClawPTT;

/// <summary>
/// Event-driven wizard for configuring per-agent settings (hotkey, emoji, color).
/// Follows the same pattern as <see cref="ConfigurationWizard"/>:
/// subscribes to <see cref="IStreamShellHost.UserInputSubmitted"/>,
/// sends prompts, validates input, and advances through steps.
/// </summary>
public sealed class AgentConfigWizard
{
    private enum Step
    {
        Hotkey,
        Emoji,
        Color,
        Done
    }

    private readonly IStreamShellHost _host;
    private readonly IAgentSettingsPersistence _persistence;
    private AgentInfo? _agent;
    private Step _currentStep;
    private bool _isFirstPrompt;

    public AgentConfigWizard(IStreamShellHost host, IAgentSettingsPersistence persistence)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
    }

    public Task RunAsync(AgentInfo agent)
    {
        _agent = agent;
        _currentStep = Step.Hotkey;
        _isFirstPrompt = true;

        _host.UserInputSubmitted += OnUserInputSubmitted;
        SendPrompt(_currentStep);

        return Task.CompletedTask;
    }

    private void OnUserInputSubmitted(string input, InputType type, IReadOnlyList<Attachment> attachments)
    {
        if (_agent == null)
            return;

        var rawInput = input.Trim();
        var step = _currentStep;

        // "--" means clear the field (set to null)
        if (rawInput == "--")
        {
            ApplyValue(step, null);
            _host.AddMessage("[green]  ✓ (cleared)[/]");
            Advance();
            return;
        }

        // Empty input keeps the current value — just advance
        if (string.IsNullOrEmpty(rawInput))
        {
            _host.AddMessage("[green]  ✓ (kept current)[/]");
            Advance();
            return;
        }

        // Validate
        if (!Validate(step, rawInput, out var errorHint))
        {
            _host.AddMessage($"[red]  ✗ {errorHint}[/]");
            SendPrompt(step);
            return;
        }

        ApplyValue(step, rawInput);
        _host.AddMessage($"[green]  ✓ {Markup.Escape(rawInput)}[/]");
        Advance();
    }

    private void Advance()
    {
        _currentStep++;

        if (_currentStep == Step.Done)
        {
            Unsubscribe();
            _host.AddMessage($"[green]  ✓ Configuration complete for {Markup.Escape(_agent?.Name ?? "agent")}![/]");
            return;
        }

        SendPrompt(_currentStep);
    }

    private void Unsubscribe()
    {
        _host.UserInputSubmitted -= OnUserInputSubmitted;
    }

    private void SendPrompt(Step step)
    {
        if (!_isFirstPrompt)
        {
            _host.AddMessage(""); // blank line between prompts
        }
        _isFirstPrompt = false;

        var (description, currentValue) = GetStepInfo(step);
        _host.AddMessage($"[cyan2]▸ {description}[/]");
        if (currentValue != null)
        {
            _host.AddMessage($"  [grey](current: {Markup.Escape(currentValue)}, press Enter to keep)[/]");
        }
        else
        {
            _host.AddMessage($"  [grey](current: none)[/]");
        }
    }

    private (string Description, string? CurrentValue) GetStepInfo(Step step)
    {
        if (_agent == null)
            return (step.ToString(), null);

        switch (step)
        {
            case Step.Hotkey:
                return ("Set hotkey combination (e.g. Alt+1, Ctrl+F5, -- to clear)",
                    _persistence.GetPersistedHotkey(_agent.AgentId));
            case Step.Emoji:
                return ("Set emoji (e.g. 🎮, 🐧, 🤖 — -- to clear)",
                    _persistence.GetPersistedEmoji(_agent.AgentId));
            case Step.Color:
                return ("Set color (Spectre.Console color name or #hex, e.g. cyan2, springgreen4, #ff6600 — -- to clear).\n       See https://spectreconsole.net/appendix/colors for color names",
                    _persistence.GetPersistedColor(_agent.AgentId));
            default:
                return (step.ToString(), null);
        }
    }

    private static bool Validate(Step step, string rawInput, out string errorHint)
    {
        errorHint = "";

        switch (step)
        {
            case Step.Hotkey:
                try
                {
                    HotkeyMapping.Parse(rawInput);
                    return true;
                }
                catch (Exception ex)
                {
                    errorHint = $"Invalid hotkey: {Markup.Escape(ex.Message)}";
                    return false;
                }
            case Step.Emoji:
                // Accept any string as emoji
                return true;
            case Step.Color:
                // Accept any string — Spectre will validate at render time
                return true;
            default:
                return true;
        }
    }

    private void ApplyValue(Step step, string? value)
    {
        if (_agent == null)
            return;

        switch (step)
        {
            case Step.Hotkey:
                _persistence.SetPersistedHotkey(_agent.AgentId, value);
                break;
            case Step.Emoji:
                _persistence.SetPersistedEmoji(_agent.AgentId, value);
                break;
            case Step.Color:
                _persistence.SetPersistedColor(_agent.AgentId, value);
                break;
        }
    }
}
