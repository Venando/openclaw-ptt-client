using System;
using System.Text.Json;

namespace OpenClawPTT.Services;

/// <summary>
/// Subscribes to GatewayService domain events and forwards them to the appropriate
/// ConsoleUi output methods. This adapter is responsible for all UI concerns;
/// GatewayService itself only fires domain events.
/// </summary>
public sealed class UiEventAdapter : IDisposable
{
    private readonly AppConfig _config;
    private readonly IConsoleOutput _consoleOutput;
    private readonly ToolDisplayHandler _toolDisplayHandler;
    private readonly AudioResponseHandler? _audioResponseHandler;

    private bool _prefixPrinted;
    private bool _isDeltaStarted;
    private bool _hasAudioInCurrentMessage;
    private AgentReplyFormatter? _formatter;
    private bool _disposed;

    private readonly string _agentReplayPrefix;
    private readonly string _agentReplayPrefixWithAudio;
    private readonly string _agentReplayPrefixTextMode;
    private readonly string _thinkingPrefix;
    private readonly string _thinkingInfo;
    private AgentReplyFormatter? _thinkingFormatter;
    private string _currentPrefix = "";
    private string _newlineSuffix = "";
    private int _prefixLength;

    public UiEventAdapter(AppConfig config) : this(config, new ConsoleOutput())
    {
    }

    public UiEventAdapter(AppConfig config, IConsoleOutput consoleOutput)
    {
        _config = config;
        _consoleOutput = consoleOutput;
        _agentReplayPrefix = $"  🤖 {_config.AgentName}: ";
        _agentReplayPrefixWithAudio = $"  🤖🔊 {_config.AgentName}: ";
        _agentReplayPrefixTextMode = $"  🤖✍️ {_config.AgentName}: ";
        _thinkingPrefix = "  💭 Thinking: ";
        _thinkingInfo = "  💭 Thinking… ";

        _toolDisplayHandler = new ToolDisplayHandler(_config.RightMarginIndent);

        if (config.AudioResponseMode?.ToLowerInvariant() != "text-only")
        {
            _audioResponseHandler = new AudioResponseHandler(config);
        }
    }

    public AudioResponseHandler? AudioResponseHandler => _audioResponseHandler;

    public void AttachToService(GatewayService service)
    {
        service.AgentReplyFull += OnAgentReplyFull;
        service.AgentThinking += OnAgentThinking;
        service.AgentToolCall += OnAgentToolCall;
        service.AgentReplyDeltaStart += OnAgentReplyDeltaStart;
        service.AgentReplyDelta += OnAgentReplyDelta;
        service.AgentReplyDeltaEnd += OnAgentReplyDeltaEnd;
        service.AgentReplyAudio += OnAgentReplyAudio;
    }

    public void DetachFromService(GatewayService service)
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
        EnsurePrefixPrinted();
        if (_formatter != null)
        {
            _formatter.ProcessDelta(body);
            _formatter.Finish();
            _formatter = null;
        }
        else
        {
            _consoleOutput.PrintAgentReply(_currentPrefix, body);
        }
    }

    public void OnAgentThinking(string thinking)
    {
        if (_config.ShowThinking)
        {
            if (!_prefixPrinted)
            {
                _prefixPrinted = true;
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(_thinkingPrefix);
                Console.ResetColor();
                if (_config.EnableWordWrap)
                {
                    _thinkingFormatter = new AgentReplyFormatter(_thinkingPrefix, _config.RightMarginIndent, prefixAlreadyPrinted: true);
                }
            }
            if (_thinkingFormatter != null)
            {
                _thinkingFormatter.ProcessDelta(thinking);
                _thinkingFormatter.Finish();
                _thinkingFormatter = null;
                _prefixPrinted = false;
                Console.WriteLine();
            }
            else
            {
                Console.Write(thinking.TrimEnd());
            }
        }
        else
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(_thinkingInfo);
            Console.ResetColor();
            Console.WriteLine();
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
        EnsurePrefixPrinted();
        if (_formatter != null)
        {
            _formatter.ProcessDelta(delta);
        }
        else
        {
            _consoleOutput.PrintAgentReplyDelta(_currentPrefix, delta, _newlineSuffix);
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

        if (_config.IsAudioEnabled && _hasAudioInCurrentMessage)
        {
            _currentPrefix = _agentReplayPrefixWithAudio;
        }
        else if (_config.IsAudioEnabled)
        {
            _currentPrefix = _agentReplayPrefixTextMode;
        }
        else
        {
            _currentPrefix = _agentReplayPrefix;
        }

        _prefixLength = _currentPrefix.Length;
        _newlineSuffix = new string(' ', _prefixLength);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write(_currentPrefix);
        Console.ResetColor();
        if (_config.EnableWordWrap)
        {
            _formatter = _consoleOutput.CreateAgentReplyFormatter(_currentPrefix, _config.RightMarginIndent, prefixAlreadyPrinted: true);
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
