using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.Services;
using Spectre.Console;

namespace OpenClawPTT;

public sealed class ConfigurationWizard
{
    private enum Step
    {
        GatewayUrl,
        AuthToken,
        TlsFingerprint,
        GroqApiKey,
        Locale,
        SampleRate,
        MaxRecordSeconds,
        RealTimeReplyOutput,
        AgentName,
        HotkeyCombination,
        HoldToTalk,
        TranscriptionPromptPrefix,
        VisualFeedbackEnabled,
        VisualFeedbackPosition,
        VisualFeedbackSize,
        VisualFeedbackOpacity,
        VisualFeedbackColor,
        VisualFeedbackRimThickness,
        AudioResponseMode,
        TtsApiKey,
        TtsVoiceId,
        Done
    }

    private IStreamShellHost? _host;
    private AppConfig? _existing;
    private AppConfig _config = new();
    private Step _currentStep = Step.GatewayUrl;
    private TaskCompletionSource<AppConfig>? _tcs;
    private CancellationTokenRegistration? _ctReg;

    public Task<AppConfig> RunSetupAsync(IStreamShellHost host, AppConfig? existing = null, CancellationToken ct = default)
    {
        _host = host;
        _existing = existing;
        _config = existing != null ? Clone(existing) : new AppConfig();
        _currentStep = Step.GatewayUrl;
        _tcs = new TaskCompletionSource<AppConfig>();

        if (ct.CanBeCanceled)
        {
            _ctReg = ct.Register(() =>
            {
                Unsubscribe();
                _tcs.TrySetCanceled(ct);
            });
        }

        host.UserInputSubmitted += OnUserInputSubmitted;
        SendPrompt(Step.GatewayUrl);

        return _tcs.Task;
    }

    private void OnUserInputSubmitted(string input)
    {
        if (_host == null || _tcs == null)
            return;

        var rawInput = input.Trim();
        var step = _currentStep;

        // Empty input accepts the default value (unless it's an optional blank field)
        if (string.IsNullOrEmpty(rawInput))
        {
            if (step == Step.TlsFingerprint)
            {
                _config.TlsFingerprint = null;
                Advance();
                return;
            }
            if (step == Step.TtsApiKey)
            {
                _config.TtsApiKey = null;
                Advance();
                return;
            }
            if (step == Step.TtsVoiceId)
            {
                _config.TtsVoiceId = null;
                Advance();
                return;
            }
            if (step == Step.AuthToken)
            {
                _config.AuthToken = null;
                Advance();
                return;
            }

            rawInput = GetDefaultValue(step);
        }

        if (!Validate(step, rawInput, out var parsedValue))
        {
            _host.AddMessage("[red]Invalid value, try again.[/]");
            SendPrompt(step);
            return;
        }

        ApplyValue(step, parsedValue ?? rawInput);
        Advance();
    }

    private void Advance()
    {
        _currentStep++;

        if (_currentStep == Step.TlsFingerprint)
        {
            // Skip TLS fingerprint prompt if not using wss://
            if (!_config.GatewayUrl.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
            {
                _config.TlsFingerprint = null;
                _currentStep++;
            }
        }

        if (_currentStep == Step.Done)
        {
            Unsubscribe();
            _tcs?.TrySetResult(_config);
            return;
        }

        SendPrompt(_currentStep);
    }

    private void Unsubscribe()
    {
        if (_host != null)
        {
            _host.UserInputSubmitted -= OnUserInputSubmitted;
        }
        _ctReg?.Dispose();
        _ctReg = null;
    }

    private void SendPrompt(Step step)
    {
        var label = GetLabel(step);
        var defaultVal = GetDefaultValue(step);
        var def = string.IsNullOrEmpty(defaultVal) ? "" : $" [{defaultVal}]";
        _host?.AddMessage($"[yellow]{label}[grey]{Markup.Escape(def)}[/]:[/]");
    }

    private static string GetLabel(Step step) => step switch
    {
        Step.GatewayUrl => "Gateway URL",
        Step.AuthToken => "Auth token (OPENCLAW_GATEWAY_TOKEN)",
        Step.TlsFingerprint => "TLS cert fingerprint (blank to skip pinning)",
        Step.GroqApiKey => "Groq API key",
        Step.Locale => "Locale",
        Step.SampleRate => "Audio sample rate",
        Step.MaxRecordSeconds => "Max recording seconds",
        Step.RealTimeReplyOutput => "Real-time reply output",
        Step.AgentName => "Agent name (shown in reply prefix)",
        Step.HotkeyCombination => "Hotkey combination (e.g., Alt+=, Ctrl+Shift+Space)",
        Step.HoldToTalk => "Hold-to-talk mode (true/false)",
        Step.TranscriptionPromptPrefix => "Transcription prompt prefix",
        Step.VisualFeedbackEnabled => "Visual feedback enabled (true/false)",
        Step.VisualFeedbackPosition => "Visual feedback position (TopLeft, TopRight, BottomLeft, BottomRight)",
        Step.VisualFeedbackSize => "Visual feedback dot size (pixels)",
        Step.VisualFeedbackOpacity => "Visual feedback opacity (0.0 to 1.0)",
        Step.VisualFeedbackColor => "Visual feedback color (hex #RRGGBB)",
        Step.VisualFeedbackRimThickness => "Visual feedback rim thickness (0 = off, 1-50 pixels)",
        Step.AudioResponseMode => "Audio response mode (text-only, audio-only, both)",
        Step.TtsApiKey => "ElevenLabs API key (optional, for audio responses)",
        Step.TtsVoiceId => "ElevenLabs voice ID",
        _ => throw new ArgumentOutOfRangeException(nameof(step), step, null)
    };

    private string GetDefaultValue(Step step) => step switch
    {
        Step.GatewayUrl => _config.GatewayUrl,
        Step.AuthToken => _config.AuthToken ?? Environment.GetEnvironmentVariable("OPENCLAW_GATEWAY_TOKEN") ?? "",
        Step.TlsFingerprint => _config.TlsFingerprint ?? "",
        Step.GroqApiKey => _config.GroqApiKey,
        Step.Locale => _config.Locale,
        Step.SampleRate => _config.SampleRate.ToString(),
        Step.MaxRecordSeconds => _config.MaxRecordSeconds.ToString(),
        Step.RealTimeReplyOutput => _config.RealTimeReplyOutput.ToString(),
        Step.AgentName => _config.AgentName,
        Step.HotkeyCombination => _config.HotkeyCombination,
        Step.HoldToTalk => _config.HoldToTalk.ToString(),
        Step.TranscriptionPromptPrefix => _config.TranscriptionPromptPrefix,
        Step.VisualFeedbackEnabled => _config.VisualFeedbackEnabled.ToString(),
        Step.VisualFeedbackPosition => _config.VisualFeedbackPosition,
        Step.VisualFeedbackSize => _config.VisualFeedbackSize.ToString(),
        Step.VisualFeedbackOpacity => _config.VisualFeedbackOpacity.ToString("F2"),
        Step.VisualFeedbackColor => _config.VisualFeedbackColor,
        Step.VisualFeedbackRimThickness => _config.VisualFeedbackRimThickness.ToString(),
        Step.AudioResponseMode => _config.AudioResponseMode ?? "text-only",
        Step.TtsApiKey => _config.TtsApiKey ?? "",
        Step.TtsVoiceId => _config.TtsVoiceId ?? "",
        _ => throw new ArgumentOutOfRangeException(nameof(step), step, null)
    };

    private static bool Validate(Step step, string rawInput, out string? parsedValue)
    {
        parsedValue = rawInput;
        switch (step)
        {
            case Step.GatewayUrl:
                return Uri.TryCreate(rawInput, UriKind.Absolute, out var uri)
                       && (uri.Scheme == "ws" || uri.Scheme == "wss");
            case Step.AuthToken:
                return true;
            case Step.TlsFingerprint:
                return true;
            case Step.GroqApiKey:
                return rawInput.StartsWith("gsk_");
            case Step.Locale:
                return rawInput.Length >= 2;
            case Step.SampleRate:
                return int.TryParse(rawInput, out var n) && n is >= 8000 and <= 48000;
            case Step.MaxRecordSeconds:
                return int.TryParse(rawInput, out var n2) && n2 is >= 5 and <= 600;
            case Step.RealTimeReplyOutput:
                return bool.TryParse(rawInput, out _);
            case Step.AgentName:
                return !string.IsNullOrWhiteSpace(rawInput);
            case Step.HotkeyCombination:
                try
                {
                    HotkeyMapping.Parse(rawInput);
                    return true;
                }
                catch
                {
                    return false;
                }
            case Step.HoldToTalk:
                return bool.TryParse(rawInput, out _);
            case Step.TranscriptionPromptPrefix:
                return !string.IsNullOrWhiteSpace(rawInput);
            case Step.VisualFeedbackEnabled:
                return bool.TryParse(rawInput, out _);
            case Step.VisualFeedbackPosition:
                return new[] { "TopLeft", "TopRight", "BottomLeft", "BottomRight" }
                    .Contains(rawInput, StringComparer.OrdinalIgnoreCase);
            case Step.VisualFeedbackSize:
                return int.TryParse(rawInput, out var n3) && n3 is >= 1 and <= 200;
            case Step.VisualFeedbackOpacity:
                return double.TryParse(rawInput, out var d) && d >= 0.0 && d <= 1.0;
            case Step.VisualFeedbackColor:
                return Regex.IsMatch(rawInput, @"^#?([0-9A-Fa-f]{6})$");
            case Step.VisualFeedbackRimThickness:
                return int.TryParse(rawInput, out var n4) && n4 is >= 0 and <= 50;
            case Step.AudioResponseMode:
                return new[] { "text-only", "audio-only", "both" }
                    .Contains(rawInput, StringComparer.OrdinalIgnoreCase);
            case Step.TtsApiKey:
                return true;
            case Step.TtsVoiceId:
                return true;
            default:
                throw new ArgumentOutOfRangeException(nameof(step), step, null);
        }
    }

    private void ApplyValue(Step step, string rawInput)
    {
        switch (step)
        {
            case Step.GatewayUrl:
                _config.GatewayUrl = rawInput;
                break;
            case Step.AuthToken:
                _config.AuthToken = string.IsNullOrWhiteSpace(rawInput) ? null : rawInput;
                break;
            case Step.TlsFingerprint:
                _config.TlsFingerprint = string.IsNullOrWhiteSpace(rawInput) ? null : rawInput;
                break;
            case Step.GroqApiKey:
                _config.GroqApiKey = rawInput;
                break;
            case Step.Locale:
                _config.Locale = rawInput;
                break;
            case Step.SampleRate:
                _config.SampleRate = int.Parse(rawInput);
                break;
            case Step.MaxRecordSeconds:
                _config.MaxRecordSeconds = int.Parse(rawInput);
                break;
            case Step.RealTimeReplyOutput:
                _config.RealTimeReplyOutput = bool.Parse(rawInput);
                break;
            case Step.AgentName:
                _config.AgentName = rawInput;
                break;
            case Step.HotkeyCombination:
                _config.HotkeyCombination = rawInput;
                break;
            case Step.HoldToTalk:
                _config.HoldToTalk = bool.Parse(rawInput);
                break;
            case Step.TranscriptionPromptPrefix:
                _config.TranscriptionPromptPrefix = rawInput;
                break;
            case Step.VisualFeedbackEnabled:
                _config.VisualFeedbackEnabled = bool.Parse(rawInput);
                break;
            case Step.VisualFeedbackPosition:
                _config.VisualFeedbackPosition = new[] { "TopLeft", "TopRight", "BottomLeft", "BottomRight" }
                    .First(p => p.Equals(rawInput, StringComparison.OrdinalIgnoreCase));
                break;
            case Step.VisualFeedbackSize:
                _config.VisualFeedbackSize = int.Parse(rawInput);
                break;
            case Step.VisualFeedbackOpacity:
                _config.VisualFeedbackOpacity = double.Parse(rawInput);
                break;
            case Step.VisualFeedbackColor:
                _config.VisualFeedbackColor = rawInput.StartsWith("#")
                    ? rawInput
                    : $"#{rawInput}";
                break;
            case Step.VisualFeedbackRimThickness:
                _config.VisualFeedbackRimThickness = int.Parse(rawInput);
                break;
            case Step.AudioResponseMode:
                _config.AudioResponseMode = new[] { "text-only", "audio-only", "both" }
                    .First(m => string.Equals(m, rawInput, StringComparison.OrdinalIgnoreCase));
                break;
            case Step.TtsApiKey:
                _config.TtsApiKey = string.IsNullOrWhiteSpace(rawInput) ? null : rawInput;
                break;
            case Step.TtsVoiceId:
                _config.TtsVoiceId = string.IsNullOrWhiteSpace(rawInput) ? null : rawInput;
                break;
        }
    }

    private static AppConfig Clone(AppConfig source)
    {
        return new AppConfig
        {
            GatewayUrl = source.GatewayUrl,
            AuthToken = source.AuthToken,
            DeviceToken = source.DeviceToken,
            TlsFingerprint = source.TlsFingerprint,
            Locale = source.Locale,
            SampleRate = source.SampleRate,
            Channels = source.Channels,
            BitsPerSample = source.BitsPerSample,
            MaxRecordSeconds = source.MaxRecordSeconds,
            LogConnect = source.LogConnect,
            LogHello = source.LogHello,
            LogSnapshot = source.LogSnapshot,
            GroqApiKey = source.GroqApiKey,
            RealTimeReplyOutput = source.RealTimeReplyOutput,
            ReplyDisplayMode = source.ReplyDisplayMode,
            SttProvider = source.SttProvider,
            OpenAiApiKey = source.OpenAiApiKey,
            OpenAiModel = source.OpenAiModel,
            WhisperCppPath = source.WhisperCppPath,
            WhisperCppModelPath = source.WhisperCppModelPath,
            GroqModel = source.GroqModel,
            HotkeyCombination = source.HotkeyCombination,
            HoldToTalk = source.HoldToTalk,
            ShowThinking = source.ShowThinking,
            DebugToolCalls = source.DebugToolCalls,
            AgentName = source.AgentName,
            TranscriptionPromptPrefix = source.TranscriptionPromptPrefix,
            AudioWrapPrompt = source.AudioWrapPrompt,
            GroqRetryCount = source.GroqRetryCount,
            GroqRetryDelayMs = source.GroqRetryDelayMs,
            GroqRetryBackoffFactor = source.GroqRetryBackoffFactor,
            ReconnectDelaySeconds = source.ReconnectDelaySeconds,
            RightMarginIndent = source.RightMarginIndent,
            EnableWordWrap = source.EnableWordWrap,
            VisualMode = source.VisualMode,
            VisualFeedbackEnabled = source.VisualFeedbackEnabled,
            VisualFeedbackPosition = source.VisualFeedbackPosition,
            VisualFeedbackSize = source.VisualFeedbackSize,
            VisualFeedbackOpacity = source.VisualFeedbackOpacity,
            VisualFeedbackColor = source.VisualFeedbackColor,
            VisualFeedbackRimThickness = source.VisualFeedbackRimThickness,
            TtsProvider = source.TtsProvider,
            TtsOpenAiApiKey = source.TtsOpenAiApiKey,
            TtsSubscriptionKey = source.TtsSubscriptionKey,
            TtsRegion = source.TtsRegion,
            TtsVoice = source.TtsVoice,
            TtsModel = source.TtsModel,
            CoquiModelPath = source.CoquiModelPath,
            CoquiModelName = source.CoquiModelName,
            CoquiConfigPath = source.CoquiConfigPath,
            PythonPath = source.PythonPath,
            PiperPath = source.PiperPath,
            PiperModelPath = source.PiperModelPath,
            PiperVoice = source.PiperVoice,
            EspeakNgPath = source.EspeakNgPath,
            PythonTtsDebugLog = source.PythonTtsDebugLog,
            AudioResponseMode = source.AudioResponseMode,
            TtsApiKey = source.TtsApiKey,
            TtsVoiceId = source.TtsVoiceId,
            SessionKey = source.SessionKey,
            CustomDataDir = source.CustomDataDir,
        };
    }
}
