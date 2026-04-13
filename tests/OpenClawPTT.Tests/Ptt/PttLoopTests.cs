using Moq;
using OpenClawPTT;
using OpenClawPTT.Services;
using System.Threading;
using Xunit;

namespace OpenClawPTT.Tests;

public class PttLoopTests
{
    [Fact]
    public async Task RunAsync_QuitViaInput_ReturnsOk()
    {
        var mockState = new Mock<IPttStateMachine>();
        var mockAudio = new Mock<IAudioService>();
        var mockSender = new Mock<ITextMessageSender>();
        var mockInput = new Mock<IInputHandler>();
        var mockPttCtrl = new Mock<IPttController>();

        mockInput.Setup(x => x.HandleInputAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(InputResult.Quit);

        var cfg = new AppConfig { HoldToTalk = true };
        var loop = new AppLoop(
            mockState.Object, mockAudio.Object, mockSender.Object,
            mockInput.Object, mockPttCtrl.Object);

        var cts = new CancellationTokenSource(50);
        var result = await loop.RunAsync(cts.Token);

        Assert.Equal(AppLoopExitCode.Ok, result);
    }

    [Fact]
    public async Task RunAsync_RestartViaInput_ReturnsRestart()
    {
        var mockState = new Mock<IPttStateMachine>();
        var mockAudio = new Mock<IAudioService>();
        var mockSender = new Mock<ITextMessageSender>();
        var mockInput = new Mock<IInputHandler>();
        var mockPttCtrl = new Mock<IPttController>();

        mockInput.Setup(x => x.HandleInputAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(InputResult.Restart);

        var cfg = new AppConfig { HoldToTalk = true };
        var loop = new AppLoop(
            mockState.Object, mockAudio.Object, mockSender.Object,
            mockInput.Object, mockPttCtrl.Object);

        var cts = new CancellationTokenSource(50);
        var result = await loop.RunAsync(cts.Token);

        Assert.Equal(AppLoopExitCode.Restart, result);
    }

    [Fact]
    public async Task RunAsync_HotkeyPressedStartsRecording()
    {
        var mockState = new Mock<IPttStateMachine>();
        var mockAudio = new Mock<IAudioService>();
        var mockSender = new Mock<ITextMessageSender>();
        var mockInput = new Mock<IInputHandler>();
        var mockPttCtrl = new Mock<IPttController>();

        mockPttCtrl.Setup(x => x.PollHotkeyPressed()).Returns(true);
        mockPttCtrl.Setup(x => x.PollHotkeyRelease()).Returns(false);

        mockState.SetupGet(x => x.CurrentState).Returns(PttState.Idle);
        mockState.Setup(x => x.ShouldStartRecording).Returns(true);
        mockState.Setup(x => x.ShouldStopRecording).Returns(false);

        mockAudio.Setup(x => x.IsRecording).Returns(false);

        // Quit after first input poll
        mockInput.Setup(x => x.HandleInputAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken ct) =>
            {
                await Task.Delay(5, ct);
                return InputResult.Quit;
            });

        var cfg = new AppConfig { HoldToTalk = true };
        var loop = new AppLoop(
            mockState.Object, mockAudio.Object, mockSender.Object,
            mockInput.Object, mockPttCtrl.Object);

        var cts = new CancellationTokenSource(100);
        await loop.RunAsync(cts.Token);

        mockAudio.Verify(x => x.StartRecording(), Times.Once);
    }
}
