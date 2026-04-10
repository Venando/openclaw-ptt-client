using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OpenClawPTT.Services;

public sealed class GatewayService : IGatewayService
{
    private readonly AppConfig _config;
    private readonly DeviceIdentity _device;
    private readonly IConsoleOutput _consoleOutput;
    private GatewayClient _gatewayClient;
    private UiEventAdapter? _uiAdapter;
    private bool _disposed;

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
        _consoleOutput = new ConsoleOutput();
        _gatewayClient = CreateGatewayClient();
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

        _uiAdapter?.Dispose();
        _gatewayClient.Dispose();
        _gatewayClient = CreateGatewayClient();
    }

    private GatewayClient CreateGatewayClient()
    {
        _uiAdapter = new UiEventAdapter(_config, _consoleOutput);
        var client = new GatewayClient(_config, _device);

        // Wire domain events → UI adapter
        client.AgentReplyFull += body =>
        {
            _uiAdapter.OnAgentReplyFull(body);
            AgentReplyFull?.Invoke(body);
        };

        client.AgentThinking += thinking =>
        {
            _uiAdapter.OnAgentThinking(thinking);
            AgentThinking?.Invoke(thinking);
        };

        client.AgentToolCall += (toolName, arguments) =>
        {
            _uiAdapter.OnAgentToolCall(toolName, arguments);
            AgentToolCall?.Invoke(toolName, arguments);
        };

        client.AgentReplyDeltaStart += () =>
        {
            _uiAdapter.OnAgentReplyDeltaStart();
            AgentReplyDeltaStart?.Invoke();
        };

        client.AgentReplyDelta += delta =>
        {
            _uiAdapter.OnAgentReplyDelta(delta);
            AgentReplyDelta?.Invoke(delta);
        };

        client.AgentReplyDeltaEnd += () =>
        {
            _uiAdapter.OnAgentReplyDeltaEnd();
            AgentReplyDeltaEnd?.Invoke();
        };

        client.EventReceived += (name, json) =>
        {
            EventReceived?.Invoke(name, json);
        };

        client.AgentReplyAudio += audioText =>
        {
            _uiAdapter.OnAgentReplyAudio(audioText);
            AgentReplyAudio?.Invoke(audioText);
        };

        return client;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _uiAdapter?.Dispose();
            _gatewayClient.Dispose();
            _disposed = true;
        }
    }
}
