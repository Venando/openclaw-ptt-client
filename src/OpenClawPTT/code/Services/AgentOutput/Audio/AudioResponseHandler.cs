using OpenClawPTT.TTS;

namespace OpenClawPTT.Services;

/// <summary>
/// Handles audio responses from the agent - detects [audio] markers,
/// synthesizes speech via TTS, and plays audio output.
/// </summary>
public sealed class AudioResponseHandler : IDisposable
{
    private readonly AppConfig _config;
    private readonly ITextToSpeech? _ttsProvider;
    private readonly AudioPlayerService _audioPlayer;
    private readonly TtsService? _ttsService;
    private bool _disposed;

    public AudioResponseHandler(AppConfig config)
    {
        _config = config;

        // Initialize TTS provider from config
        if (config.TtsProvider == TtsProviderType.OpenAI &&
            string.IsNullOrEmpty(config.TtsOpenAiApiKey) &&
            string.IsNullOrEmpty(config.OpenAiApiKey))
        {
            // OpenAI: skip if no API key
        }
        else
        {
            try
            {
                _ttsService = new TtsService(config);
                _ttsProvider = _ttsService.Provider;
            }
            catch (Exception ex)
            {
                var hint = config.TtsProvider switch
                {
                    TtsProviderType.OpenAI => "Set TtsOpenAiApiKey or OpenAiApiKey in config.",
                    TtsProviderType.Coqui => "Verify PythonPath, CoquiModelName, and that Coqui TTS is installed (pip install TTS).",
                    TtsProviderType.Piper => "Verify PiperPath and that a voice model (.onnx file) is downloaded.",
                    TtsProviderType.Edge => "Set TtsSubscriptionKey (Azure API key) in config.",
                    TtsProviderType.ElevenLabs => "Set TtsApiKey and TtsVoiceId for ElevenLabs in config.",
                    _ => "Check provider configuration."
                };
                ConsoleUi.PrintWarning($"TTS provider initialization failed: {ex.Message} — {hint}");
            }
        }

        _audioPlayer = new AudioPlayerService();
    }

    /// <summary>
    /// Handle [audio] marker specifically - synthesize and play.
    /// </summary>
    public Task HandleAudioMarkerAsync(string text, CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AudioResponseHandler));

        return PlayTtsAsync(text, ct);
    }

    private Task PlayTtsAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Task.CompletedTask;

        if (_ttsProvider == null)
        {
            ConsoleUi.PrintWarning("TTS not configured - set TtsProvider in settings to enable audio responses.");
            return Task.CompletedTask;
        }

        // Fire and forget — synthesize in background, play when ready
        _ = Task.Run(async () =>
        {
            try
            {
                var audioBytes = await _ttsProvider.SynthesizeAsync(text, _config.TtsVoice, null, ct);
                if (audioBytes != null && audioBytes.Length > 0)
                {
                    _audioPlayer.Play(audioBytes);
                }
            }
            catch (Exception ex)
            {
                ConsoleUi.PrintError($"TTS synthesis failed: {ex.Message}");
            }
        });

        return Task.CompletedTask;
    }

    /// <summary>
    /// Stop any currently playing audio.
    /// </summary>
    public void StopPlayback()
    {
        _audioPlayer.Stop();
    }

    /// <summary>
    /// Check if audio is currently playing.
    /// </summary>
    public bool IsPlaying => _audioPlayer.IsPlaying;

    public void Dispose()
    {
        if (!_disposed)
        {
            _ttsService?.Dispose();
            _audioPlayer.Dispose();
            _disposed = true;
        }
    }
}
