using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OpenClawPTT.Services;

public sealed class GatewayService : IDisposable
{
    private readonly AppConfig _config;
    private readonly DeviceIdentity _device;
    private GatewayClient _gatewayClient;
    private bool _disposed;
    private bool _prefixPrinted; 
    private bool _isDeltaStarted;

    public event Action<string>? AgentReplyFull;
    public event Action? AgentReplyDeltaStart;
    public event Action<string>? AgentReplyDelta;
    public event Action? AgentReplyDeltaEnd;
    public event Action<string>? AgentThinking;
    public event Action<string, JsonElement>? EventReceived;
    
    public GatewayService(AppConfig config)
    {
        _config = config;
        _device = new DeviceIdentity(config.DataDir);
        _device.EnsureKeypair();
        _gatewayClient = CreateGatewayClient();
    }
    
    public string DeviceId => _device.DeviceId;
    
    public async Task ConnectAsync(CancellationToken ct)
    {
        Console.WriteLine($"  Device ID: {_device.DeviceId[..16]}…");
        Console.WriteLine();
        
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
        
        const string agentReplayPrefix = "  🤖 Agent: ";
        var prefixLength = agentReplayPrefix.Length;
        var newlineSuffix = new string(' ', prefixLength);
        
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
                ConsoleUi.PrintAgentReply(agentReplayPrefix, body);
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

        client.AgentReplyDeltaStart += () =>
        {
            _isDeltaStarted = true;
            AgentReplyDeltaStart?.Invoke();
        };

        void EnsurePrefixPrinted()
        {
            if (_prefixPrinted) return;
            _prefixPrinted = true;
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write(agentReplayPrefix);
            Console.ResetColor();
            if (_config.EnableWordWrap)
            {
                formatter = ConsoleUi.CreateAgentReplyFormatter(agentReplayPrefix, _config.RightMarginIndent, prefixAlreadyPrinted: true);
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
                ConsoleUi.PrintAgentReplyDelta(agentReplayPrefix, delta, newlineSuffix);
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
        
        return client;
    }
    
    public void Dispose()
    {
        if (!_disposed)
        {
            _gatewayClient.Dispose();
            _disposed = true;
        }
    }
}