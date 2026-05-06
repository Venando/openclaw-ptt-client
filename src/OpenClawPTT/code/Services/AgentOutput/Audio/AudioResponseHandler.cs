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
    private readonly ITtsSummarizer? _summarizer;
    private readonly IPttStateMachine? _pttStateMachine;
    private readonly IColorConsole _console;
    private readonly IBackgroundJobRunner _jobRunner;
    private bool _disposed;

    public AudioResponseHandler(AppConfig config, IColorConsole console, IBackgroundJobRunner? jobRunner = null, ITtsSummarizer? summarizer = null, IPttStateMachine? pttStateMachine = null)
    {
        _config = config;
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _jobRunner = jobRunner ?? new BackgroundJobRunner(msg => _console.Log("jobrunner", msg));
        _audioPlayer = new AudioPlayerService(console);
        _summarizer = summarizer;
        _pttStateMachine = pttStateMachine;

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

    private async Task PlayTtsAsync(string text, CancellationToken ct)
    {
        _console.Log("tts-debug", $"[Audio] PlayTtsAsync entered. TtsOutputMode={_config.TtsOutputMode}, LastInputWasVoice={_pttStateMachine?.LastInputWasVoice}, textLen={text?.Length}");

        if (string.IsNullOrWhiteSpace(text))
        {
            _console.Log("tts-debug", "[Audio] Skipped: text is null/whitespace.");
            return;
        }

        // Validate config values
        if (_config.TtsMaxChars <= 0)
        {
            _console.PrintWarning($"Invalid TtsMaxChars config ({_config.TtsMaxChars}) - must be greater than 0.");
            return;
        }
        if (_config.TtsDirectMaxChars <= 0)
        {
            _console.PrintWarning($"Invalid TtsDirectMaxChars config ({_config.TtsDirectMaxChars}) - must be greater than 0.");
            return;
        }

        // Check if TTS is enabled (case-insensitive)
        if (string.Equals(_config.TtsOutputMode, "off", StringComparison.OrdinalIgnoreCase))
        {
            _console.Log("tts-debug", "[Audio] Skipped: TtsOutputMode is 'off'.");
            return;
        }

        // Check SISO mode (case-insensitive)
        if (string.Equals(_config.TtsOutputMode, "siso", StringComparison.OrdinalIgnoreCase))
        {
            if (_pttStateMachine == null)
            {
                _console.Log("tts-debug", "[Audio] Skipped in SISO: IPttStateMachine is null.");
                return;
            }
            if (_pttStateMachine.LastInputWasVoice != true)
            {
                _console.Log("tts-debug", $"[Audio] Skipped in SISO: LastInputWasVoice={_pttStateMachine.LastInputWasVoice} (expected true).");
                return;
            }
            _console.Log("tts-debug", "[Audio] SISO check PASSED — will speak.");
        }
        else
        {
            _console.Log("tts-debug", $"[Audio] Always-On mode — will speak (mode={_config.TtsOutputMode}).");
        }

        if (_ttsProvider == null)
        {
            _console.Log("tts-debug", "[Audio] Skipped: _ttsProvider is null (not configured).");
            return;
        }
        _console.Log("tts-debug", $"[Audio] TTS provider ready: {_config.TtsProvider}");

        // Determine if we need summarization
        var needsProcessing = TtsContentFilter.HasSpecialFormatting(text) ||
                              text.Length > _config.TtsDirectMaxChars;
        _console.Log("tts-debug", $"[Audio] needsProcessing={needsProcessing} (hasFormatting={TtsContentFilter.HasSpecialFormatting(text)}, len={text.Length}, directMax={_config.TtsDirectMaxChars})");

        string textToSpeak;

        if (needsProcessing && _config.TtsUseDirectLlmSummary && _summarizer != null)
        {
            _console.Log("tts-debug", "[Audio] Path: LLM summarization.");
            try
            {
                textToSpeak = await _summarizer.SummarizeForTtsAsync(text, _config, ct);
                _console.Log("tts-debug", $"[Audio] LLM summary result ({textToSpeak.Length} chars): {textToSpeak}");
            }
            catch (Exception ex)
            {
                _console.PrintWarning($"TTS summarization failed: {ex.Message}. Falling back to truncation.");
                textToSpeak = TtsContentFilter.SanitizeForTts(text);
                textToSpeak = TtsContentFilter.Truncate(textToSpeak, _config.TtsMaxChars);
            }
        }
        else if (needsProcessing)
        {
            _console.Log("tts-debug", "[Audio] Path: No LLM, sanitize + truncate.");
            // No Direct LLM available
            textToSpeak = TtsContentFilter.SanitizeForTts(text);

            if (textToSpeak.Length > _config.TtsMaxChars)
            {
                if (string.Equals(_config.TtsTooLongFallback, "skip", StringComparison.OrdinalIgnoreCase))
                {
                    _console.PrintWarning($"Response ({textToSpeak.Length} chars) exceeds TtsMaxChars ({_config.TtsMaxChars}) — skipping TTS.");
                    return; // Don't speak this response
                }
                else // truncate
                {
                    _console.Log("tts-debug", $"[Audio] Truncating from {textToSpeak.Length} to {_config.TtsMaxChars} chars.");
                    textToSpeak = TtsContentFilter.Truncate(textToSpeak, _config.TtsMaxChars);
                }
            }
        }
        else
        {
            // Short text, speak directly
            _console.Log("tts-debug", "[Audio] Path: Short text, speak directly.");
            textToSpeak = TtsContentFilter.SanitizeForTts(text);
        }

        if (string.IsNullOrWhiteSpace(textToSpeak))
        {
            _console.Log("tts-debug", "[Audio] Skipped: textToSpeak is empty after processing.");
            return;
        }

        _console.Log("tts-debug", $"[Audio] Final textToSpeak ({textToSpeak.Length} chars): {textToSpeak}");

        // Synthesize and play via background job runner
        var truncated = textToSpeak.Length > 80 ? textToSpeak[..80] + "..." : textToSpeak;
        _jobRunner.RunAndForget(async () =>
        {
            _console.Log("tts-debug", $"[Audio] Synthesis STARTED: '{truncated}'");
            // Use a fresh token to avoid cancellation exceptions in fire-and-forget
            var freshCt = CancellationToken.None;
            var audioBytes = await _ttsProvider.SynthesizeAsync(textToSpeak, _config.TtsVoice, null, freshCt);
            if (audioBytes != null && audioBytes.Length > 0)
            {
                _audioPlayer.Play(audioBytes);
                _console.Log("tts-debug", $"[Audio] Playback STARTED ({audioBytes.Length} bytes).");
            }
            else
            {
                _console.PrintWarning("TTS synthesis returned null/empty audio.");
            }
        }, $"tts-synthesis-{truncated}");
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
