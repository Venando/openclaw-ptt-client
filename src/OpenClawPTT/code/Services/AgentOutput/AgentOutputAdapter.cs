using System;
using System.Text.Json;
using Spectre.Console;

namespace OpenClawPTT.Services;

/// <summary>
/// Subscribes to GatewayService domain events and forwards them to the appropriate
/// console output methods. This adapter is responsible for all UI concerns;
/// GatewayService itself only fires domain events.
/// </summary>
public sealed class AgentOutputAdapter : IDisposable
{
    private readonly IColorConsole _console;
    private readonly AppConfig _config;
    private readonly ToolDisplayHandler _toolDisplayHandler;
    private readonly IBackgroundJobRunner _jobRunner;
    private readonly AudioResponseHandler? _audioResponseHandler;

    private bool _prefixPrinted;
    private bool _isDeltaStarted;
    private bool _hasAudioInCurrentMessage;
    private IAgentReplyFormatter? _formatter;
    private bool _disposed;

    private string _currentPrefix = "";
    private string _newlineSuffix = "";
    private int _prefixLength;

    // Capturing console used when StreamShell is active — accumulates formatter output
    // then pushes the complete reply as a single StreamShell message.
    private StreamShellCapturingConsole? _capturingConsole;

    public AgentOutputAdapter(AppConfig config, IColorConsole console, ITtsSummarizer? summarizer = null, IPttStateMachine? pttStateMachine = null)
    {
        _config = config;
        _console = console ?? throw new ArgumentNullException(nameof(console));
        var shellHost = console.GetStreamShellHost();
        _toolDisplayHandler = new ToolDisplayHandler(_config.RightMarginIndent, shellHost);
        _jobRunner = new BackgroundJobRunner(msg => _console.Log("jobrunner", msg));

        if (config.AudioResponseMode?.ToLowerInvariant() != "text-only")
        {
            _audioResponseHandler = new AudioResponseHandler(config, console, _jobRunner, summarizer, pttStateMachine);
        }
    }

    public AudioResponseHandler? AudioResponseHandler => _audioResponseHandler;

    public void AttachToService(IGatewayUIEvents service)
    {
        service.AgentReplyFull += OnAgentReplyFull;
        service.AgentThinking += OnAgentThinking;
        service.AgentToolCall += OnAgentToolCall;
        service.AgentReplyDeltaStart += OnAgentReplyDeltaStart;
        service.AgentReplyDelta += OnAgentReplyDelta;
        service.AgentReplyDeltaEnd += OnAgentReplyDeltaEnd;
        service.AgentReplyAudio += OnAgentReplyAudio;
    }

    public void DetachFromService(IGatewayUIEvents service)
    {
        service.AgentReplyFull -= OnAgentReplyFull;
        service.AgentThinking -= OnAgentThinking;
        service.AgentToolCall -= OnAgentToolCall;
        service.AgentReplyDeltaStart -= OnAgentReplyDeltaStart;
        service.AgentReplyDelta -= OnAgentReplyDelta;
        service.AgentReplyDeltaEnd -= OnAgentReplyDeltaEnd;
        service.AgentReplyAudio -= OnAgentReplyAudio;
    }

    // ─── event handlers ────────────────────────────────────────────

    public void OnAgentReplyFull(string body)
    {
        var markdownBody = MarkdownToSpectreConverter.Convert(body);
        // When StreamShell is active, use a capturing console for word-wrapped replies
        // so the complete formatted reply gets pushed as a single StreamShell message.
        bool useCapturing = _console.GetStreamShellHost() != null;

        EnsurePrefixPrinted();

        if (_formatter != null)
        {
            _formatter.ProcessMarkupDelta(markdownBody);
            _formatter.Finish();
            _formatter = null;

            if (useCapturing && _capturingConsole != null)
            {
                _capturingConsole.FlushToStreamShell($"[cyan]{Markup.Escape(_currentPrefix)}[/]");
            }
        }
        else
        {
            _console.PrintAgentReplyWithMarkdown(_currentPrefix, markdownBody);
        }
        
        _prefixPrinted = false;
    }

    public void OnAgentThinking(string thinking)
    {
        if (_config.ShowThinking)
        {
            // Route through console — goes to StreamShell when active
            _console.Log("agent-think", thinking);
            _prefixPrinted = false;
        }
        else
        {
            _prefixPrinted = false;
        }
    }

    public void OnAgentToolCall(string toolName, string arguments)
    {
        _toolDisplayHandler.Handle(toolName, arguments);
    }

    public void OnAgentReplyDeltaStart()
    {
        _isDeltaStarted = true;
        _formatter = null;
    }

    public void OnAgentReplyDelta(string delta)
    {
        if (!_isDeltaStarted) return;
        EnsurePrefixPrinted();
        if (_formatter != null)
        {
            _formatter.ProcessDelta(delta);
        }
        else
        {
            _console.PrintAgentReplyDelta(_currentPrefix, delta, _newlineSuffix);
        }
    }

    public void OnAgentReplyDeltaEnd()
    {
        if (!_isDeltaStarted) return;

        _isDeltaStarted = false;
        _prefixPrinted = false;
        _hasAudioInCurrentMessage = false;

        if (_formatter != null)
        {
            _formatter.Finish();
            _formatter = null;
        }
    }

    public void OnAgentReplyAudio(string audioText)
    {
        _hasAudioInCurrentMessage = true;
        // Audio handling is async; fire and forget
        if (_audioResponseHandler != null)
        {
            _ = _audioResponseHandler.HandleAudioMarkerAsync(audioText);
        }
    }

    // ─── helpers ───────────────────────────────────────────────────

    private void EnsurePrefixPrinted()
    {
        if (_prefixPrinted) return;
        _prefixPrinted = true;

        var isAudioEnabled = _config.AudioResponseMode?.ToLowerInvariant() != "text-only";
        
        AgentRegistry.GetActiveNameAndEmoji(out var agentName, out var emoji, _config.AgentName);

        if (isAudioEnabled && _hasAudioInCurrentMessage)
        {
            _currentPrefix = $"  {emoji} 🔊 {agentName}: ";
        }
        else if (isAudioEnabled)
        {
            _currentPrefix = $"  {emoji} ✍️ {agentName}: ";
        }
        else
        {
            _currentPrefix = $"  {emoji} {agentName}: ";
        }

        _prefixLength = _currentPrefix.Length;
        _newlineSuffix = new string(' ', _prefixLength);

        if (_config.EnableWordWrap)
        {
            // When StreamShell is active, capture formatter output for final flush to Shell
            var shellHost = _console.GetStreamShellHost();
            if (shellHost != null)
            {
                _capturingConsole = new StreamShellCapturingConsole(shellHost);
                _formatter = new AgentReplyFormatter(_currentPrefix, _config.RightMarginIndent, prefixAlreadyPrinted: true, output: _capturingConsole);
            }
            else
            {
                _capturingConsole = null;
                _formatter = new AgentReplyFormatter(_currentPrefix, _config.RightMarginIndent, prefixAlreadyPrinted: true, output: null!);
            }
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _audioResponseHandler?.Dispose();
            _disposed = true;
        }
    }
}
