using System;

namespace OpenClawPTT.Services;

/// <summary>
/// Owns the Direct LLM probing lifecycle: startup probe, config-change-triggered
/// re-probes, and last-called tracking. Extracted from AppRunner for SRP.
/// Disposable to unsubscribe from config change events.
/// </summary>
public sealed class DirectLlmProbeService : IDisposable
{
    private readonly IConfigurationService _configService;
    private readonly IStatusService _statusService;
    private readonly IColorConsole _console;
    private readonly IServiceFactory _factory;
    private string? _lastKnownLlmUrl;
    private string? _lastKnownLlmModel;
    private IDirectLlmFailureTracker? _tracker;
    private Action? _onFailureThresholdReached;
    private Action? _onFailureRecovered;
    private bool _disposed;

    public DirectLlmProbeService(
        IConfigurationService configService,
        IStatusService statusService,
        IColorConsole console,
        IServiceFactory factory)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _statusService = statusService ?? throw new ArgumentNullException(nameof(statusService));
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));

        _configService.ConfigSaved += OnConfigSaved;

        _onFailureThresholdReached = () =>
        {
            _statusService.SetServiceStatus(ServiceKind.DirectLlm, StatusColor.Red);
            _console.Log("llm", "Direct LLM send failure — consecutive failures detected");
        };
        _onFailureRecovered = () =>
        {
            _statusService.SetServiceStatus(ServiceKind.DirectLlm, StatusColor.Green);
            _console.LogOk("llm", "Direct LLM recovered — send succeeded after failure");
        };
    }

    /// <summary>
    /// Probes the Direct LLM endpoint and updates the status bar.
    /// Called on startup. Also subscribes to the service's failure tracker
    /// so that send failures update the status dynamically.
    /// </summary>
    public async Task ProbeOnStartupAsync(IDirectLlmService service, AppConfig config, CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DirectLlmProbeService));

        // Subscribe to failure tracker for dynamic status updates
        SubscribeToTracker(service.FailureTracker);

        // Remember initial LLM config for change detection
        _lastKnownLlmUrl = config.DirectLlmUrl;
        _lastKnownLlmModel = config.DirectLlmModelName;

        await ProbeAndUpdateAsync(service, config, ct);
    }

    /// <summary>
    /// Probes the Direct LLM and updates the status bar.
    /// </summary>
    private async Task ProbeAndUpdateAsync(IDirectLlmService service, AppConfig config, CancellationToken ct)
    {
        if (!service.IsConfigured)
        {
            _statusService.SetServiceStatus(ServiceKind.DirectLlm, StatusColor.Yellow);
            return;
        }

        try
        {
            _statusService.SetServiceStatus(ServiceKind.DirectLlm, StatusColor.Yellow);
            var ok = await service.ProbeAsync(ct);

            if (ok)
            {
                _statusService.SetServiceStatus(ServiceKind.DirectLlm, StatusColor.Green);
                _console.LogOk("llm", $"Direct LLM probed successfully ({config.DirectLlmModelName})");
            }
            else
            {
                _statusService.SetServiceStatus(ServiceKind.DirectLlm, StatusColor.Red);
                _console.Log("llm", "Direct LLM probe failed — endpoint not reachable");
            }
        }
        catch (OperationCanceledException)
        {
            _statusService.SetServiceStatus(ServiceKind.DirectLlm, StatusColor.Yellow);
            throw;
        }
        catch (Exception ex)
        {
            _statusService.SetServiceStatus(ServiceKind.DirectLlm, StatusColor.Red);
            _console.LogError("llm", $"Direct LLM probe error: {ex.Message}");
        }
    }

    /// <summary>
    /// Re-probes the Direct LLM whenever the app config is saved (e.g. via /appconfig).
    /// Filters to <c>DirectLlmUrl</c> / <c>DirectLlmModelName</c> changes only.
    /// </summary>
    private async void OnConfigSaved(ConfigChangedEventArgs e)
    {
        bool llmChanged = e.AnyChanged(nameof(AppConfig.DirectLlmUrl), nameof(AppConfig.DirectLlmModelName));
        if (!llmChanged)
            return;

        _lastKnownLlmUrl = e.NewConfig.DirectLlmUrl;
        _lastKnownLlmModel = e.NewConfig.DirectLlmModelName;

        try
        {
            using var freshService = _factory.CreateDirectLlmService(e.NewConfig);
            await ProbeAndUpdateAsync(freshService, e.NewConfig, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _console.LogError("llm", $"LLM re-probe failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Subscribes to the tracker's events for dynamic status updates.
    /// Unsubscribes from any previous tracker first.
    /// </summary>
    private void SubscribeToTracker(IDirectLlmFailureTracker? tracker)
    {
        if (_tracker != null)
        {
            if (_onFailureThresholdReached != null)
                _tracker.FailureThresholdReached -= _onFailureThresholdReached;
            if (_onFailureRecovered != null)
                _tracker.FailureRecovered -= _onFailureRecovered;
        }

        _tracker = tracker;

        if (_tracker != null)
        {
            if (_onFailureThresholdReached != null)
                _tracker.FailureThresholdReached += _onFailureThresholdReached;
            if (_onFailureRecovered != null)
                _tracker.FailureRecovered += _onFailureRecovered;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _configService.ConfigSaved -= OnConfigSaved;
            SubscribeToTracker(null); // unsubscribe from tracker events
            _onFailureThresholdReached = null;
            _onFailureRecovered = null;
            _disposed = true;
        }
    }
}