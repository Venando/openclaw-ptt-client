using System.Text.Json.Serialization;

namespace OpenClawPTT.TTS;

/// <summary>
/// TTS provider types
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TtsProviderType
{
    OpenAI,
    Edge,
    Coqui,
    Piper,
    Python
}

/// <summary>
/// TTS service - manages TTS providers and configuration
/// </summary>
public sealed class TtsService : IDisposable
{
    private readonly ITextToSpeech? _provider;
    private readonly TtsProviderType _providerType;
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    public CancellationToken CancellationToken => _cts.Token;

    public TtsProviderType ProviderType => _providerType;
    public ITextToSpeech? Provider => _provider;
    public bool IsConfigured => _provider != null;

    public TtsService(AppConfig config)
    {
        _providerType = config.TtsProvider;

        _provider = _providerType switch
        {
            TtsProviderType.OpenAI => new Providers.OpenAiTtsProvider(config.TtsOpenAiApiKey ?? config.OpenAiApiKey ?? throw new InvalidOperationException("OpenAI API key not configured")),
            TtsProviderType.Coqui => new Providers.CoquiTtsProvider(config.CoquiModelPath ?? "", config.CoquiModelName ?? "tts_models/multilingual/mxtts/vits", null, null),
            TtsProviderType.Piper => new Providers.PiperTtsProvider(config.PiperPath ?? "piper", config.PiperModelPath ?? "", config.PiperVoice ?? "en_US-lessac"),
            TtsProviderType.Edge => config.TtsSubscriptionKey != null
                ? new Providers.EdgeTtsProvider(config.TtsSubscriptionKey, config.TtsRegion ?? "eastus")
                : null,
            TtsProviderType.Python => config.UseUvPython
                ? new Providers.PythonTtsProvider(
                    baseDir: config.DataDir,
                    useUvManagement: true,
                    uvToolsPath: config.UvToolsPath,
                    pythonVersion: "3.11",
                    ttsServiceScript: config.TtsServiceScriptPath)
                : new Providers.PythonTtsProvider(
                    config.TtsServiceScriptPath ?? "",
                    config.PythonPath ?? ""),
            _ => null
        };

        if (_provider == null && _providerType != TtsProviderType.Edge && _providerType != TtsProviderType.Python)
        {
            throw new InvalidOperationException($"Failed to initialize TTS provider: {_providerType}");
        }

        // For uv-managed Python, defer InitializeAsync to avoid blocking the constructor
        if (_provider is Providers.PythonTtsProvider pythonProvider)
        {
            Task.Run(() => pythonProvider.InitializeAsync(_cts.Token), _cts.Token);
        }
    }

    /// <summary>
    /// Synthesize text to audio
    /// </summary>
    public async Task<byte[]> SynthesizeAsync(string text, CancellationToken ct = default)
    {
        if (_provider == null)
        {
            throw new InvalidOperationException("TTS provider not configured");
        }

        return await _provider.SynthesizeAsync(
            text,
            voice: null,
            model: null,
            ct);
    }

    /// <summary>
    /// Synthesize text with specific voice
    /// </summary>
    public async Task<byte[]> SynthesizeAsync(string text, string voice, CancellationToken ct = default)
    {
        if (_provider == null)
        {
            throw new InvalidOperationException("TTS provider not configured");
        }

        return await _provider.SynthesizeAsync(text, voice, null, ct);
    }

    /// <summary>
    /// Synthesize text with specific voice and model
    /// </summary>
    public async Task<byte[]> SynthesizeAsync(string text, string voice, string model, CancellationToken ct = default)
    {
        if (_provider == null)
        {
            throw new InvalidOperationException("TTS provider not configured");
        }

        return await _provider.SynthesizeAsync(text, voice, model, ct);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _cts.Cancel();
            _cts.Dispose();
            if (_provider is IAsyncDisposable asyncDisposable)
                asyncDisposable.DisposeAsync().Preserve();
            else
                (_provider as IDisposable)?.Dispose();
            _disposed = true;
        }
    }
}