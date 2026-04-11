using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using OpenClawPTT.TTS;

namespace OpenClawPTT.Services;

/// <summary>
/// Handles audio responses from the agent - detects [audio] and [text] markers,
/// synthesizes speech via TTS, and plays audio output.
/// </summary>
public sealed class AudioResponseHandler : IDisposable
{
    private readonly AppConfig _config;
    private readonly ITextToSpeech? _ttsProvider;
    private readonly AudioPlayerService _audioPlayer;
    private readonly OpenClawPTT.TTS.TtsService? _ttsService;
    private readonly IConsoleOutput? _console;
    private bool _disposed;

    public AudioResponseHandler(AppConfig config)
        : this(config, null)
    {
    }

    public AudioResponseHandler(AppConfig config, IConsoleOutput? console)
    {
        _config = config;
        _console = console;

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
                _ttsService = new OpenClawPTT.TTS.TtsService(config);
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
                _console?.PrintWarning($"TTS provider initialization failed: {ex.Message} — {hint}");
            }
        }

        _audioPlayer = new AudioPlayerService(console);
    }
    
    /// <summary>
    /// Handle an agent reply. Based on config, will:
    /// - text-only: just print the text
    /// - audio-only: synthesize TTS and play
    /// - both: print text AND synthesize and play TTS
    /// </summary>
    public async Task HandleAgentReplyAsync(
        string? fullMessage,
        string? audioText,
        string? textContent,
        CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AudioResponseHandler));
        
        var mode = _config.AudioResponseMode?.ToLowerInvariant() ?? "text-only";
        
        switch (mode)
        {
            case "audio-only":
                if (!string.IsNullOrEmpty(audioText))
                {
                    await PlayTtsAsync(audioText, ct);
                }
                else if (!string.IsNullOrEmpty(fullMessage))
                {
                    // Use full message if no explicit [audio] marker
                    await PlayTtsAsync(fullMessage, ct);
                }
                break;
                
            case "both":
                // Print text to console (already handled by GatewayService)
                if (!string.IsNullOrEmpty(audioText))
                {
                    await PlayTtsAsync(audioText, ct);
                }
                else if (!string.IsNullOrEmpty(fullMessage) && !string.IsNullOrEmpty(textContent))
                {
                    // If full message but no explicit [audio], use text content for TTS
                    await PlayTtsAsync(textContent, ct);
                }
                else if (!string.IsNullOrEmpty(fullMessage))
                {
                    await PlayTtsAsync(fullMessage, ct);
                }
                break;
                
            case "text-only":
            default:
                // Just print text - already handled by GatewayService
                break;
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
    
    /// <summary>
    /// Handle [text] marker specifically - just print (handled elsewhere).
    /// </summary>
    public void HandleTextMarker(string text)
    {
        // Text handling is done by GatewayService - this is for completeness
    }

    private Task PlayTtsAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Task.CompletedTask;

        if (_ttsProvider == null)
        {
            _console?.PrintWarning("TTS not configured - set TtsProvider in settings to enable audio responses.");
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
                _console?.PrintError($"TTS synthesis failed: {ex.Message}");
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
