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
    
    public event Action<string>? AgentReplyFull;
    public event Action? AgentReplyDeltaStart;
    public event Action<string>? AgentReplyDelta;
    public event Action? AgentReplyDeltaEnd;
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
        
        client.AgentReplyFull += body =>
        {
            ConsoleUi.PrintAgentReply(agentReplayPrefix, body);
            AgentReplyFull?.Invoke(body);
        };
        
        client.AgentReplyDeltaStart += () =>
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write(agentReplayPrefix);
            Console.ResetColor();
            AgentReplyDeltaStart?.Invoke();
        };
        
        client.AgentReplyDelta += delta =>
        {
            ConsoleUi.PrintAgentReplyDelta(agentReplayPrefix, delta, newlineSuffix);
            AgentReplyDelta?.Invoke(delta);
        };
        
        client.AgentReplyDeltaEnd += () =>
        {
            Console.WriteLine();
            AgentReplyDeltaEnd?.Invoke();
        };
        
        client.EventReceived += (name, json) =>
        {
            // health and tick events not sure what to do with them
            return;
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"[event]: " + name);
            Console.ResetColor();
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