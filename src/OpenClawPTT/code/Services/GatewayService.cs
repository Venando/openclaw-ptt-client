using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OpenClawPTT.Services;

public sealed class GatewayService : IGatewayService
{
    private readonly AppConfig _config;
    private readonly DeviceIdentity _device;
    private GatewayClient _gatewayClient;
    private AudioResponseHandler? _audioResponseHandler;
    private bool _disposed;
    private bool _prefixPrinted;
    private bool _isDeltaStarted;
    private bool _hasAudioInCurrentMessage;

    public event Action<string>? AgentReplyFull;
    public event Action? AgentReplyDeltaStart;
    public event Action<string>? AgentReplyDelta;
    public event Action? AgentReplyDeltaEnd;
    public event Action<string>? AgentThinking;
    public event Action<string, string>? AgentToolCall; // (toolName, arguments)
    public event Action<string, JsonElement>? EventReceived;
    public event Action<string>? AgentReplyAudio;

    public GatewayService(AppConfig config)
    {
        _config = config;
        _device = new DeviceIdentity(config.DataDir);
        _device.EnsureKeypair();
        _gatewayClient = CreateGatewayClient();
        
        // Initialize audio response handler if audio mode is not text-only
        if (config.AudioResponseMode?.ToLowerInvariant() != "text-only")
        {
            _audioResponseHandler = new AudioResponseHandler(config);
        }
    }
    
    public async Task ConnectAsync(CancellationToken ct)
    {
        await _gatewayClient.ConnectAsync(ct);
    }
    
    public async Task SendTextAsync(string text, CancellationToken ct)
    {
        await _gatewayClient.SendTextAsync(text, ct);
    }
    
    public void RecreateWithConfig(AppConfig newConfig)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(GatewayService));
        
        _gatewayClient.Dispose();
        _gatewayClient = CreateGatewayClient();
    }
    
    private GatewayClient CreateGatewayClient()
    {
        var client = new GatewayClient(_config, _device);
        
        string agentReplayPrefix = $"  🤖 {_config.AgentName}: ";
        string agentReplayPrefixWithAudio = $"  🤖🔊 {_config.AgentName}: ";
        string agentReplayPrefixTextMode = $"  🤖✍️ {_config.AgentName}: ";
        int prefixLength = agentReplayPrefix.Length;
        string newlineSuffix = new string(' ', prefixLength);
        string currentPrefix = agentReplayPrefix;
        
        AgentReplyFormatter? formatter = null;
        AgentReplyFormatter? thinkingFormatter = null;
        const string thinkingPrefix = "  💭 Thinking: ";
        const string thinkingInfo = "  💭 Thinking… ";

        client.AgentReplyFull += body =>
        {
            EnsurePrefixPrinted();
            if (formatter != null)
            {
                formatter.ProcessDelta(body);
                formatter.Finish();
                formatter = null;
            }
            else
            {
                ConsoleUi.PrintAgentReply(currentPrefix, body);
            }
            AgentReplyFull?.Invoke(body);
        };

        client.AgentThinking += thinking =>
        {
            if (_config.ShowThinking)
            {
                if (!_prefixPrinted)
                {
                    _prefixPrinted = true;
                    Console.WriteLine();
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write(thinkingPrefix);
                    Console.ResetColor();
                    if (_config.EnableWordWrap)
                    {
                        thinkingFormatter = new AgentReplyFormatter(thinkingPrefix, _config.RightMarginIndent, prefixAlreadyPrinted: true);
                    }
                }
                if (thinkingFormatter != null)
                {
                    thinkingFormatter.ProcessDelta(thinking);
                    thinkingFormatter.Finish();
                    thinkingFormatter = null;
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
                Console.WriteLine(thinkingInfo);
                Console.ResetColor();
                Console.WriteLine();
                _prefixPrinted = false;
            }
            AgentThinking?.Invoke(thinking);
        };

        var toolDisplayHandler = new ToolDisplayHandler(_config.RightMarginIndent);
        client.AgentToolCall += (toolName, arguments) =>
        {
            toolDisplayHandler.Handle(toolName, arguments);
            AgentToolCall?.Invoke(toolName, arguments);
        };

        client.AgentReplyDeltaStart += () =>
        {
            _isDeltaStarted = true;
            formatter = null;
            AgentReplyDeltaStart?.Invoke();
        };

        void EnsurePrefixPrinted()
        {
            if (_prefixPrinted) return;
            _prefixPrinted = true;

            if (_config.IsAudioEnabled && _hasAudioInCurrentMessage)
            {
                currentPrefix = agentReplayPrefixWithAudio;
            }
            else if (_config.IsAudioEnabled)
            {
                currentPrefix = agentReplayPrefixTextMode;
            }
            else
            {
                currentPrefix = agentReplayPrefix;
            }

            prefixLength = currentPrefix.Length;
            newlineSuffix = new string(' ', prefixLength);

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write(currentPrefix);
            Console.ResetColor();
            if (_config.EnableWordWrap)
            {
                formatter = ConsoleUi.CreateAgentReplyFormatter(currentPrefix, _config.RightMarginIndent, prefixAlreadyPrinted: true);
            }
        }

        client.AgentReplyDelta += delta =>
        {
            EnsurePrefixPrinted();
            if (formatter != null)
            {
                formatter.ProcessDelta(delta);
            }
            else
            {
                ConsoleUi.PrintAgentReplyDelta(currentPrefix, delta, newlineSuffix);
            }
            AgentReplyDelta?.Invoke(delta);
        };
        
        client.AgentReplyDeltaEnd += () =>
        {
            if (!_isDeltaStarted)
            {
                return;
            }

            _isDeltaStarted = false;
            _prefixPrinted = false;
            _hasAudioInCurrentMessage = false;

            if (formatter != null)
            {
                formatter.Finish();
                formatter = null;
            }
            // else
            // {
            //     Console.WriteLine();
            // }
            AgentReplyDeltaEnd?.Invoke();
        };
        
        client.EventReceived += (name, json) =>
        {
            // health and tick events not sure what to do with them
            // For now, just forward the event
            EventReceived?.Invoke(name, json);
        };
        
        // Handle [audio] marker - synthesize and play TTS
        client.AgentReplyAudio += async audioText =>
        {
            _hasAudioInCurrentMessage = true;
            if (_audioResponseHandler != null)
            {
                await _audioResponseHandler.HandleAudioMarkerAsync(audioText);
            }
            AgentReplyAudio?.Invoke(audioText);
        };

        return client;
    }
    
    public void Dispose()
    {
        if (!_disposed)
        {
            _gatewayClient.Dispose();
            _audioResponseHandler?.Dispose();
            _disposed = true;
        }
    }
}