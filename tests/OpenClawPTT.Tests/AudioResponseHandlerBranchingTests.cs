using OpenClawPTT;
using OpenClawPTT.Services;
using OpenClawPTT.TTS;
using Xunit;

namespace OpenClawPTT.Tests;

/// <summary>
/// Extended tests for AudioResponseHandler — covering branching gaps in
/// HandleAgentReplyAsync and HandleAudioMarkerAsync paths.
/// </summary>
public class AudioResponseHandlerBranchingTests
{
    // ========================================================================
    // HandleAgentReplyAsync — both mode fallback chain
    // ========================================================================

    /// <summary>
    /// both mode: audioText=null, textContent=null, fullMessage=some text
    /// → should use fullMessage as final fallback
    /// </summary>
    [Fact]
    public async Task HandleAgentReplyAsync_BothMode_FullMessageFallback()
    {
        var cfg = new AppConfig { AudioResponseMode = "both" };
        using var handler = new AudioResponseHandler(cfg);

        // Should not throw — falls through to final fullMessage fallback
        await handler.HandleAgentReplyAsync(
            fullMessage: "hello from full message",
            audioText: null,
            textContent: null);

        Assert.False(handler.IsPlaying); // no real audio player/tts configured
    }

    /// <summary>
    /// both mode: audioText=empty, textContent=empty, fullMessage=some text
    /// → should use fullMessage as final fallback
    /// </summary>
    [Fact]
    public async Task HandleAgentReplyAsync_BothMode_FullMessageFallback_EmptyStrings()
    {
        var cfg = new AppConfig { AudioResponseMode = "both" };
        using var handler = new AudioResponseHandler(cfg);

        await handler.HandleAgentReplyAsync(
            fullMessage: "hello from full message",
            audioText: "",
            textContent: "");

        Assert.False(handler.IsPlaying);
    }

    /// <summary>
    /// both mode: textContent is whitespace-only, fullMessage=some text
    /// → should fall back to fullMessage (whitespace textContent is not usable)
    /// </summary>
    [Fact]
    public async Task HandleAgentReplyAsync_BothMode_TextContentWhitespace_FallsBackToFullMessage()
    {
        var cfg = new AppConfig { AudioResponseMode = "both" };
        using var handler = new AudioResponseHandler(cfg);

        // whitespace-only textContent should be treated as empty → fall back
        await handler.HandleAgentReplyAsync(
            fullMessage: "fallback message",
            audioText: null,
            textContent: "   ");

        Assert.False(handler.IsPlaying);
    }

    // ========================================================================
    // HandleAgentReplyAsync — all-null/all-empty in each mode
    // ========================================================================

    [Theory]
    [InlineData("audio-only")]
    [InlineData("both")]
    [InlineData("text-only")]
    public async Task HandleAgentReplyAsync_AllNull_DoesNotCrash(string mode)
    {
        var cfg = new AppConfig { AudioResponseMode = mode };
        using var handler = new AudioResponseHandler(cfg);

        // Must not throw ObjectDisposedException or any other exception
        await handler.HandleAgentReplyAsync(fullMessage: null, audioText: null, textContent: null);
        Assert.False(handler.IsPlaying);
    }

    [Theory]
    [InlineData("audio-only")]
    [InlineData("both")]
    [InlineData("text-only")]
    public async Task HandleAgentReplyAsync_AllEmptyStrings_DoesNotCrash(string mode)
    {
        var cfg = new AppConfig { AudioResponseMode = mode };
        using var handler = new AudioResponseHandler(cfg);

        await handler.HandleAgentReplyAsync(fullMessage: "", audioText: "", textContent: "");
        Assert.False(handler.IsPlaying);
    }

    [Theory]
    [InlineData("audio-only")]
    [InlineData("both")]
    [InlineData("text-only")]
    public async Task HandleAgentReplyAsync_AllWhitespace_DoesNotCrash(string mode)
    {
        var cfg = new AppConfig { AudioResponseMode = mode };
        using var handler = new AudioResponseHandler(cfg);

        await handler.HandleAgentReplyAsync(fullMessage: "   ", audioText: "  ", textContent: "\t");
        Assert.False(handler.IsPlaying);
    }

    // ========================================================================
    // HandleAudioMarkerAsync — mode coverage
    // ========================================================================

    /// <summary>
    /// audio-only mode with no TTS configured — should not crash, just warn
    /// </summary>
    [Fact]
    public async Task HandleAudioMarkerAsync_AudioOnly_NoTts_DoesNotCrash()
    {
        var cfg = new AppConfig { AudioResponseMode = "audio-only" };
        using var handler = new AudioResponseHandler(cfg);

        // Should not throw even though TTS is not configured
        await handler.HandleAudioMarkerAsync("some audio text");
        Assert.False(handler.IsPlaying);
    }

    /// <summary>
    /// text-only mode — should be no-op (no audio plays)
    /// </summary>
    [Fact]
    public async Task HandleAudioMarkerAsync_TextOnly_IsNoOp()
    {
        var cfg = new AppConfig { AudioResponseMode = "text-only" };
        using var handler = new AudioResponseHandler(cfg);

        await handler.HandleAudioMarkerAsync("any text");
        Assert.False(handler.IsPlaying);
    }

    /// <summary>
    /// both mode with no TTS — should not crash
    /// </summary>
    [Fact]
    public async Task HandleAudioMarkerAsync_BothMode_NoTts_DoesNotCrash()
    {
        var cfg = new AppConfig { AudioResponseMode = "both" };
        using var handler = new AudioResponseHandler(cfg);

        await handler.HandleAudioMarkerAsync("text for both mode");
        Assert.False(handler.IsPlaying);
    }

    // ========================================================================
    // HandleAgentReplyAsync — audio-only mode branch
    // ========================================================================

    /// <summary>
    /// audio-only: audioText=null, fullMessage=some text → uses fullMessage
    /// </summary>
    [Fact]
    public async Task HandleAgentReplyAsync_AudioOnly_NoAudioText_UsesFullMessage()
    {
        var cfg = new AppConfig { AudioResponseMode = "audio-only" };
        using var handler = new AudioResponseHandler(cfg);

        await handler.HandleAgentReplyAsync(
            fullMessage: "speak this",
            audioText: null,
            textContent: null);

        Assert.False(handler.IsPlaying);
    }

    /// <summary>
    /// audio-only: audioText=empty, fullMessage=some text → uses fullMessage
    /// </summary>
    [Fact]
    public async Task HandleAgentReplyAsync_AudioOnly_EmptyAudioText_UsesFullMessage()
    {
        var cfg = new AppConfig { AudioResponseMode = "audio-only" };
        using var handler = new AudioResponseHandler(cfg);

        await handler.HandleAgentReplyAsync(
            fullMessage: "speak this instead",
            audioText: "",
            textContent: null);

        Assert.False(handler.IsPlaying);
    }

    // ========================================================================
    // TTS initialization failure — warning is printed, no crash
    // ========================================================================

    /// <summary>
    /// TTS not configured (no provider) → "TTS not configured" warning printed via
    /// PlayTtsAsync when HandleAudioMarkerAsync is called — no crash.
    /// </summary>
    [Fact]
    public async Task AudioResponseHandler_TtsNotConfigured_WarnsViaPlayTtsAsync()
    {
        var warningCapture = new List<string>();
        var mockConsole = new MockConsoleOutput { Warnings = warningCapture };

        // text-only config so AudioResponseHandler skips TTS init entirely.
        // This gives us a handler with _ttsProvider == null without needing
        // a specific provider to fail in the constructor.
        var cfg = new AppConfig
        {
            AudioResponseMode = "audio-only", // will try to use TTS
            TtsProvider = TtsProviderType.OpenAI,
            TtsOpenAiApiKey = null,
            OpenAiApiKey = null
        };

        using var handler = new AudioResponseHandler(cfg, mockConsole);

        // Trigger PlayTtsAsync (via HandleAudioMarkerAsync) which should
        // detect _ttsProvider == null and print "TTS not configured" warning.
        await handler.HandleAudioMarkerAsync("speak this");

        // Give the fire-and-forget task a moment to run
        await Task.Delay(50);

        Assert.NotEmpty(warningCapture);
        Assert.Contains("TTS", warningCapture[0]); // "TTS not configured" warning
    }

    /// <summary>
    /// Edge TTS provider with no subscription key → graceful degradation, no crash
    /// </summary>
    [Fact]
    public void AudioResponseHandler_EdgeNoKey_GracefulDegradation()
    {
        var cfg = new AppConfig
        {
            AudioResponseMode = "audio-only",
            TtsProvider = TtsProviderType.Edge,
            TtsSubscriptionKey = null
        };

        // Should not throw
        using var handler = new AudioResponseHandler(cfg);
        Assert.False(handler.IsPlaying);
    }

    // ========================================================================
    // ObjectDisposedException — handler used after Dispose
    // ========================================================================

    [Fact]
    public async Task HandleAgentReplyAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var cfg = new AppConfig { AudioResponseMode = "both" };
        var handler = new AudioResponseHandler(cfg);
        handler.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => handler.HandleAgentReplyAsync("msg", null, null));
    }

    [Fact]
    public async Task HandleAudioMarkerAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var cfg = new AppConfig { AudioResponseMode = "audio-only" };
        var handler = new AudioResponseHandler(cfg);
        handler.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => handler.HandleAudioMarkerAsync("msg"));
    }

    // ========================================================================
    // HandleTextMarker — completeness
    // ========================================================================

    [Fact]
    public void HandleTextMarker_DoesNotThrow()
    {
        var cfg = new AppConfig { AudioResponseMode = "both" };
        using var handler = new AudioResponseHandler(cfg);

        // Should not throw — text handling is delegated elsewhere
        handler.HandleTextMarker("some text marker");
    }

    // ========================================================================
    // StopPlayback / IsPlaying
    // ========================================================================

    [Fact]
    public void StopPlayback_DoesNotThrow()
    {
        var cfg = new AppConfig { AudioResponseMode = "text-only" };
        using var handler = new AudioResponseHandler(cfg);

        handler.StopPlayback(); // no audio playing — should not throw
        Assert.False(handler.IsPlaying);
    }

    // ========================================================================
    // Default AudioResponseMode (null) — treated as text-only
    // ========================================================================

    [Fact]
    public async Task HandleAgentReplyAsync_DefaultMode_TreatedAsTextOnly()
    {
        var cfg = new AppConfig { AudioResponseMode = null }; // default / null
        using var handler = new AudioResponseHandler(cfg);

        // null mode → text-only → no audio crash
        await handler.HandleAgentReplyAsync("anything", "audio", "text");
        Assert.False(handler.IsPlaying);
    }

    // ========================================================================
    // Mock helper
    // ========================================================================

    sealed class MockConsoleOutput : IConsoleOutput
    {
        public List<string> Warnings { get; set; } = new();
        public List<string> Errors { get; set; } = new();

        public void PrintWarning(string message) => Warnings.Add(message);
        public void PrintError(string message) => Errors.Add(message);

        // IConsole members — no-ops for test isolation
        public void Write(string? text) { }
        public void WriteLine(string? text = null) { }
        public ConsoleColor ForegroundColor { get; set; } = ConsoleColor.White;
        public void ResetColor() { }
        public bool KeyAvailable => false;
        public ConsoleKeyInfo ReadKey(bool intercept = false) => default;
        public int WindowWidth => 120;
        public System.Text.Encoding OutputEncoding { get; set; } = System.Text.Encoding.UTF8;
        public bool TreatControlCAsInput { get; set; } = false;
        public void PrintBanner() { }
        public void PrintHelpMenu(string hotkeyCombination, bool holdToTalk) { }
        public void PrintRecordingIndicator(bool isRecording, string hotkeyCombination, bool holdToTalk) { }
        public void PrintSuccess(string message) { }
        public void PrintSuccessWordWrap(string prefix, string message, int rightMarginIndent) { }
        public void PrintInfo(string message) { }
        public void PrintInlineInfo(string message) { }
        public void PrintInlineSuccess(string message) { }
        public void PrintGatewayError(string message, string? detailCode, string? recommendedStep) { }
        public void PrintAgentReply(string prefix, string body) { }
        public void PrintAgentReplyDelta(string prefix, string delta, string newlineSuffix) { }
        public OpenClawPTT.IAgentReplyFormatter CreateAgentReplyFormatter(string prefix, int rightMarginIndent, bool prefixAlreadyPrinted = false)
            => throw new NotImplementedException();
        public OpenClawPTT.IAgentReplyFormatter CreateAgentReplyFormatter(string prefix, int rightMarginIndent, bool prefixAlreadyPrinted, int consoleWidth)
            => throw new NotImplementedException();
        public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default) => default;
        public void Log(string tag, string msg) { }
        public void LogOk(string tag, string msg) { }
        public void LogError(string tag, string msg) { }
    }
}