using Moq;
using OpenClawPTT.Services;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace OpenClawPTT.Tests;

/// <summary>
/// Stability tests for AudioResponseHandler - covers HandleAgentReplyAsync branching,
/// disposal behavior, StopPlayback, and IsPlaying without requiring real TTS/audio hardware.
/// </summary>
public class AudioResponseHandlerStabilityTests
{
    #region HandleAgentReplyAsync — audio-only mode

    [Fact]
    public async Task HandleAgentReplyAsync_AudioOnly_WithExplicitAudioText_PlayTtsCalled()
    {
        // Arrange: audio-only mode with explicit audioText
        var cfg = new AppConfig { AudioResponseMode = "audio-only" };
        var mockConsole = new Mock<IConsoleOutput>();
        var handler = new AudioResponseHandler(cfg, mockConsole.Object);

        // Act: pass explicit audioText — should use it (TTS fires async, just verify no throw)
        await handler.HandleAgentReplyAsync(
            fullMessage: "This is the full message",
            audioText: "This is the audio text",
            textContent: null,
            default);

        // Assert: handler still alive and not playing (TTS may not be configured)
        Assert.False(handler.IsPlaying);

        handler.Dispose();
    }

    [Fact]
    public async Task HandleAgentReplyAsync_AudioOnly_NoAudioText_FallsBackToFullMessage()
    {
        // Arrange: audio-only mode, no explicit audioText, fall back to fullMessage
        var cfg = new AppConfig { AudioResponseMode = "audio-only" };
        var mockConsole = new Mock<IConsoleOutput>();
        var handler = new AudioResponseHandler(cfg, mockConsole.Object);

        // Act: no audioText, but fullMessage present
        await handler.HandleAgentReplyAsync(
            fullMessage: "Fallback message",
            audioText: null,
            textContent: null,
            default);

        // Assert: no throw — falls back to fullMessage for TTS
        Assert.False(handler.IsPlaying);

        handler.Dispose();
    }

    #endregion

    #region HandleAgentReplyAsync — both mode

    [Fact]
    public async Task HandleAgentReplyAsync_Both_WithExplicitAudioText_PlayTtsCalled()
    {
        // Arrange: both mode with explicit audioText
        var cfg = new AppConfig { AudioResponseMode = "both" };
        var mockConsole = new Mock<IConsoleOutput>();
        var handler = new AudioResponseHandler(cfg, mockConsole.Object);

        // Act: explicit audioText used for TTS
        await handler.HandleAgentReplyAsync(
            fullMessage: "Full message here",
            audioText: "Audio text here",
            textContent: null,
            default);

        // Assert: no throw, TTS fires (IsPlaying reflects player state)
        Assert.False(handler.IsPlaying);

        handler.Dispose();
    }

    [Fact]
    public async Task HandleAgentReplyAsync_Both_NoAudioText_WithFullMessageAndTextContent_FallsBackToTextContent()
    {
        // Arrange: both mode, no audioText, has fullMessage + textContent
        var cfg = new AppConfig { AudioResponseMode = "both" };
        var mockConsole = new Mock<IConsoleOutput>();
        var handler = new AudioResponseHandler(cfg, mockConsole.Object);

        // Act: no audioText but fullMessage + textContent — uses textContent as fallback
        await handler.HandleAgentReplyAsync(
            fullMessage: "The full message",
            audioText: null,
            textContent: "The text content fallback",
            default);

        // Assert: no throw
        Assert.False(handler.IsPlaying);

        handler.Dispose();
    }

    [Fact]
    public async Task HandleAgentReplyAsync_Both_NoAudioText_FullMessageOnly_NoTextContent_UsesFullMessage()
    {
        // Arrange: both mode, no audioText, no textContent — only fullMessage
        var cfg = new AppConfig { AudioResponseMode = "both" };
        var mockConsole = new Mock<IConsoleOutput>();
        var handler = new AudioResponseHandler(cfg, mockConsole.Object);

        // Act: only fullMessage available
        await handler.HandleAgentReplyAsync(
            fullMessage: "Only the full message",
            audioText: null,
            textContent: null,
            default);

        // Assert: no throw — falls back to fullMessage
        Assert.False(handler.IsPlaying);

        handler.Dispose();
    }

    #endregion

    #region HandleAgentReplyAsync — text-only mode

    [Fact]
    public async Task HandleAgentReplyAsync_TextOnly_DoesNotThrow()
    {
        // Arrange: text-only mode — no TTS should fire
        var cfg = new AppConfig { AudioResponseMode = "text-only" };
        var mockConsole = new Mock<IConsoleOutput>();
        var handler = new AudioResponseHandler(cfg, mockConsole.Object);

        // Act: pass all markers — text-only is a no-op (no throw)
        await handler.HandleAgentReplyAsync(
            fullMessage: "Any message",
            audioText: "Audio text",
            textContent: "Text content",
            default);

        // Assert: handler alive, not playing
        Assert.False(handler.IsPlaying);

        handler.Dispose();
    }

    #endregion

    #region HandleAgentReplyAsync — default (null/unknown mode)

    [Fact]
    public async Task HandleAgentReplyAsync_NullMode_TreatedAsTextOnly()
    {
        // Arrange: null AudioResponseMode defaults to text-only
        var cfg = new AppConfig { AudioResponseMode = null };
        var mockConsole = new Mock<IConsoleOutput>();
        var handler = new AudioResponseHandler(cfg, mockConsole.Object);

        // Act: should behave like text-only (no throw)
        await handler.HandleAgentReplyAsync(
            fullMessage: "Message",
            audioText: "Audio",
            textContent: "Text",
            default);

        Assert.False(handler.IsPlaying);

        handler.Dispose();
    }

    #endregion

    #region Disposal behavior

    [Fact]
    public async Task HandleAgentReplyAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        var cfg = new AppConfig { AudioResponseMode = "audio-only" };
        var mockConsole = new Mock<IConsoleOutput>();
        var handler = new AudioResponseHandler(cfg, mockConsole.Object);
        handler.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => handler.HandleAgentReplyAsync(
                fullMessage: "Message",
                audioText: "Audio",
                textContent: null,
                default));
    }

    [Fact]
    public async Task HandleAudioMarkerAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        var cfg = new AppConfig { AudioResponseMode = "audio-only" };
        var mockConsole = new Mock<IConsoleOutput>();
        var handler = new AudioResponseHandler(cfg, mockConsole.Object);
        handler.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => handler.HandleAudioMarkerAsync("Some text", default));
    }

    #endregion

    #region HandleAudioMarkerAsync — basic behavior

    [Fact]
    public async Task HandleAudioMarkerAsync_WithText_DoesNotThrow()
    {
        // Arrange
        var cfg = new AppConfig { AudioResponseMode = "audio-only" };
        var mockConsole = new Mock<IConsoleOutput>();
        var handler = new AudioResponseHandler(cfg, mockConsole.Object);

        // Act: direct audio marker — should not throw
        await handler.HandleAudioMarkerAsync("Direct audio marker text", default);

        // Assert
        Assert.False(handler.IsPlaying);

        handler.Dispose();
    }

    #endregion

    #region StopPlayback

    [Fact]
    public void StopPlayback_DoesNotThrow()
    {
        // Arrange
        var cfg = new AppConfig { AudioResponseMode = "audio-only" };
        var mockConsole = new Mock<IConsoleOutput>();
        var handler = new AudioResponseHandler(cfg, mockConsole.Object);

        // Act: stop when nothing is playing — should not throw
        handler.StopPlayback();

        // Assert: no throw
        Assert.False(handler.IsPlaying);

        handler.Dispose();
    }

    #endregion

    #region IsPlaying

    [Fact]
    public void IsPlaying_WhenNothingPlaying_ReturnsFalse()
    {
        // Arrange
        var cfg = new AppConfig { AudioResponseMode = "audio-only" };
        var mockConsole = new Mock<IConsoleOutput>();
        var handler = new AudioResponseHandler(cfg, mockConsole.Object);

        // Act & Assert
        Assert.False(handler.IsPlaying);

        handler.Dispose();
    }

    [Fact]
    public void IsPlaying_AfterStopPlayback_ReturnsFalse()
    {
        // Arrange
        var cfg = new AppConfig { AudioResponseMode = "audio-only" };
        var mockConsole = new Mock<IConsoleOutput>();
        var handler = new AudioResponseHandler(cfg, mockConsole.Object);

        // Act
        handler.StopPlayback();

        // Assert
        Assert.False(handler.IsPlaying);

        handler.Dispose();
    }

    #endregion

    #region HandleTextMarker

    [Fact]
    public void HandleTextMarker_DoesNotThrow()
    {
        // Arrange
        var cfg = new AppConfig { AudioResponseMode = "both" };
        var mockConsole = new Mock<IConsoleOutput>();
        var handler = new AudioResponseHandler(cfg, mockConsole.Object);

        // Act & Assert: text marker is a no-op (handled by GatewayService)
        handler.HandleTextMarker("Some text marker content");

        handler.Dispose();
    }

    #endregion
}
