using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT;

namespace OpenClawPTT.Services;

public sealed class GatewayService : IGatewayService
{
    private readonly AppConfig _config;
    private readonly DeviceIdentity _device;
    private readonly IConsoleOutput _consoleOutput;
    private IGatewayClient _gatewayClient;
    private AgentOutputAdapter? _uiAdapter;
    private bool _disposed;

    public event Action<string>? AgentReplyFull;
    public event Action? AgentReplyDeltaStart;
    public event Action<string>? AgentReplyDelta;
    public event Action? AgentReplyDeltaEnd;
    public event Action<string>? AgentThinking;
    public event Action<string, string>? AgentToolCall; // (toolName, arguments)
    public event Action<string, JsonElement>? EventReceived;
    public event Action<string>? AgentReplyAudio;

    /// <summary>
    /// Initializes the gateway service with the given config and console output.
    /// </summary>
    /// <param name="config">Application configuration.</param>
    /// <param name="consoleOutput">Console output for agent reply display; uses <see cref="StreamShellConsoleOutput"/> as default.</param>
    public GatewayService(AppConfig config, IConsoleOutput? consoleOutput = null)
    {
        _config = config;
        _device = new DeviceIdentity(config.DataDir);
        _device.EnsureKeypair();
        _consoleOutput = consoleOutput ?? new StreamShellConsoleOutput(new StreamShellHost());
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

    private IGatewayClient CreateGatewayClient()
    {
        _uiAdapter = new AgentOutputAdapter(_config, _consoleOutput);
        var client = new GatewayClient(_config, _device, new GatewayEventSource());
        var events = ((IGatewayClient)client).GetEventSource();

        bool useDelta = _config.ReplyDisplayMode != ReplyDisplayMode.Full;
        bool useFull = _config.ReplyDisplayMode != ReplyDisplayMode.Delta;

        // Agent thinking, tool calls, and audio are always wired (not duplicated paths)
        if (events != null)
        {
            events.AgentThinking += thinking =>
            {
                _uiAdapter.OnAgentThinking(thinking);
                AgentThinking?.Invoke(thinking);
            };

            events.AgentToolCall += (toolName, arguments) =>
            {
                _uiAdapter.OnAgentToolCall(toolName, arguments);
                AgentToolCall?.Invoke(toolName, arguments);
            };

            // Audio is wired unconditionally — audio content (TTS) is independent of display mode
            events.AgentReplyAudio += audioText =>
            {
                _uiAdapter.OnAgentReplyAudio(audioText);
                AgentReplyAudio?.Invoke(audioText);
            };

            events.EventReceived += (name, json) =>
            {
                EventReceived?.Invoke(name, json);
            };

            // Wire delta path
            if (useDelta)
            {
                events.AgentReplyDeltaStart += () =>
                {
                    _uiAdapter.OnAgentReplyDeltaStart();
                    AgentReplyDeltaStart?.Invoke();
                };

                events.AgentReplyDelta += delta =>
                {
                    _uiAdapter.OnAgentReplyDelta(delta);
                    AgentReplyDelta?.Invoke(delta);
                };

                events.AgentReplyDeltaEnd += () =>
                {
                    _uiAdapter.OnAgentReplyDeltaEnd();
                    AgentReplyDeltaEnd?.Invoke();
                };
            }

            // Wire full reply path
            if (useFull)
            {
                events.AgentReplyFull += body =>
                {
                    _uiAdapter.OnAgentReplyFull(body);
                    AgentReplyFull?.Invoke(body);
                };
            }
        }

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