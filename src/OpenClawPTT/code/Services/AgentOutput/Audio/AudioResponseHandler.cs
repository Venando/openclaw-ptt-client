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
    private readonly IColorConsole _console;
    private readonly IBackgroundJobRunner _jobRunner;
    private bool _disposed;

    public AudioResponseHandler(AppConfig config, IColorConsole console, IBackgroundJobRunner? jobRunner = null)
    {
        _config = config;
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _jobRunner = jobRunner ?? new BackgroundJobRunner(msg => _console.Log("jobrunner", msg));
        _audioPlayer = new AudioPlayerService(console);

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
                _ttsService = new TtsService(config, console);
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
                _console.PrintWarning($"TTS provider initialization failed: {ex.Message} — {hint}");
            }
        }
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
            _console.PrintWarning("TTS not configured - set TtsProvider in settings to enable audio responses.");
            return Task.CompletedTask;
        }

        // Synthesize and play via background job runner
        var truncated = text.Length > 80 ? text[..80] + "..." : text;
        _jobRunner.RunAndForget(async () =>
        {
            var audioBytes = await _ttsProvider.SynthesizeAsync(text, _config.TtsVoice, null, ct);
            if (audioBytes != null && audioBytes.Length > 0)
            {
                _audioPlayer.Play(audioBytes);
            }
        }, $"tts-synthesis-{truncated}");

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
