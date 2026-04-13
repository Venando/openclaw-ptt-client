using Moq;
using OpenClawPTT.Services;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace OpenClawPTT.Tests;

public class PttControllerTests
{
    [Fact]
    public void IsRecording_DelegatesToAudioService()
    {
        var mockAudio = new Mock<IAudioService>();
        mockAudio.Setup(x => x.IsRecording).Returns(true);

        var cfg = new AppConfig { HotkeyCombination = "Alt+=", HoldToTalk = false };
        var ptt = new PttController(cfg, mockAudio.Object);

        Assert.True(ptt.IsRecording);
    }

    [Fact]
    public async Task StopAndTranscribeAsync_DelegatesToAudioService()
    {
        var mockAudio = new Mock<IAudioService>();
        mockAudio.Setup(x => x.StopAndTranscribeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("transcribed text");

        var cfg = new AppConfig { HotkeyCombination = "Alt+=", HoldToTalk = false };
        var ptt = new PttController(cfg, mockAudio.Object);

        var result = await ptt.StopAndTranscribeAsync();

        Assert.Equal("transcribed text", result);
        mockAudio.Verify(x => x.StopAndTranscribeAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void ShouldToggleRecording_WhenNotHoldToTalk_ReturnsFalse()
    {
        var mockAudio = new Mock<IAudioService>();
        var cfg = new AppConfig { HotkeyCombination = "Alt+=", HoldToTalk = false };
        var ptt = new PttController(cfg, mockAudio.Object);

        // In toggle mode, ShouldToggleRecording checks for hotkey pressed
        // Since no hotkey was pressed, should return false
        Assert.False(ptt.ShouldToggleRecording());
    }

    [Fact]
    public void ShouldStopRecording_WhenHoldToTalk_ChecksReleaseAndRecording()
    {
        var mockAudio = new Mock<IAudioService>();
        mockAudio.Setup(x => x.IsRecording).Returns(true);
        var cfg = new AppConfig { HotkeyCombination = "Alt+=", HoldToTalk = true };
        var ptt = new PttController(cfg, mockAudio.Object);

        // No release detected, should return false
        Assert.False(ptt.ShouldStopRecording());
    }
}
