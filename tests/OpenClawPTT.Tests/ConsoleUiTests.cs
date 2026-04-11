using System;
using System.Text;
using System.Linq;
using Moq;
using OpenClawPTT.Services;
using Xunit;

namespace OpenClawPTT.Tests;

/// <summary>
/// Tests for ConsoleUi static methods using a Mock IConsole.
/// </summary>
public class ConsoleUiTests : IDisposable
{
    private Mock<IConsole> _mockConsole = null!;

    private void SetupMock()
    {
        // Dispose old mock before replacing to avoid Moq collection state issues
        if (_mockConsole != null)
        {
            ConsoleUi.SetConsole(new SystemConsole());
            _mockConsole.Object.Dispose();
            _mockConsole = null;
        }
        _mockConsole = new Mock<IConsole>();
        _mockConsole.Setup(x => x.WindowWidth).Returns(120);
        _mockConsole.Setup(x => x.OutputEncoding).Returns(Encoding.UTF8);
        _mockConsole.Setup(x => x.TreatControlCAsInput).Returns(false);
        ConsoleUi.SetConsole(_mockConsole.Object);
    }

    private void ResetToSystemConsole()
    {
        ConsoleUi.SetConsole(new SystemConsole());
    }

    public void Dispose()
    {
        ResetToSystemConsole();
    }

    #region PrintBanner

    [Fact]
    public void PrintBanner_CallsWriteLineFiveTimes()
    {
        SetupMock();
        try
        {
            var writeLineCalls = new List<string?>();
            _mockConsole.Setup(x => x.WriteLine(It.IsAny<string?>())).Callback<string?>(writeLineCalls.Add);

            ConsoleUi.PrintBanner();

            // WriteLine is called 5 times: blank line at start + 4 banner lines + blank line at end
            Assert.Equal(5, writeLineCalls.Count);
        }
        finally
        {
            ResetToSystemConsole();
        }
    }

    [Fact]
    public void PrintBanner_SetsForegroundColorToCyanBeforeBox()
    {
        SetupMock();
        try
        {
            var capturedColors = new List<ConsoleColor>();
            _mockConsole.SetupSet(x => x.ForegroundColor = It.IsAny<ConsoleColor>())
                .Callback<ConsoleColor>(c => capturedColors.Add(c));

            ConsoleUi.PrintBanner();

            Assert.Contains(ConsoleColor.Cyan, capturedColors);
        }
        finally
        {
            ResetToSystemConsole();
        }
    }

    [Fact]
    public void PrintBanner_ResetsColorAfterBox()
    {
        SetupMock();
        try
        {
            var capturedColors = new List<ConsoleColor>();
            _mockConsole.SetupSet(x => x.ForegroundColor = It.IsAny<ConsoleColor>())
                .Callback<ConsoleColor>(c => capturedColors.Add(c));

            ConsoleUi.PrintBanner();

            // After Cyan is set, ResetColor should be called
            Assert.Contains(ConsoleColor.Cyan, capturedColors);
            _mockConsole.Verify(x => x.ResetColor(), Times.AtLeastOnce);
        }
        finally
        {
            ResetToSystemConsole();
        }
    }

    #endregion

    #region PrintHelpMenu

    [Fact]
    public void PrintHelpMenu_HoldToTalkMode_IncludesCorrectModeDescription()
    {
        SetupMock();
        try
        {
            var writeLineCalls = new List<string?>();
            _mockConsole.Setup(x => x.WriteLine(It.IsAny<string?>())).Callback<string?>(writeLineCalls.Add);

            ConsoleUi.PrintHelpMenu("Alt+=", true);

            var allOutput = string.Join("", writeLineCalls.Where(s => s != null));
            Assert.Contains("Hold-to-talk", allOutput);
            Assert.Contains("Alt+=", allOutput);
        }
        finally
        {
            ResetToSystemConsole();
        }
    }

    [Fact]
    public void PrintHelpMenu_ToggleMode_IncludesCorrectModeDescription()
    {
        SetupMock();
        try
        {
            var writeLineCalls = new List<string?>();
            _mockConsole.Setup(x => x.WriteLine(It.IsAny<string?>())).Callback<string?>(writeLineCalls.Add);

            ConsoleUi.PrintHelpMenu("Space", false);

            var allOutput = string.Join("", writeLineCalls.Where(s => s != null));
            Assert.Contains("Toggle recording", allOutput);
            Assert.Contains("Space", allOutput);
        }
        finally
        {
            ResetToSystemConsole();
        }
    }

    [Fact]
    public void PrintHelpMenu_SetsGreenForegroundAndResets()
    {
        SetupMock();
        try
        {
            var capturedColors = new List<ConsoleColor>();
            _mockConsole.SetupSet(x => x.ForegroundColor = It.IsAny<ConsoleColor>())
                .Callback<ConsoleColor>(c => capturedColors.Add(c));

            ConsoleUi.PrintHelpMenu("Alt+=", true);

            Assert.Contains(ConsoleColor.Green, capturedColors);
            _mockConsole.Verify(x => x.ResetColor(), Times.AtLeastOnce);
        }
        finally
        {
            ResetToSystemConsole();
        }
    }

    #endregion

    #region PrintSuccess

    [Fact]
    public void PrintSuccess_SetsGreenForegroundAndResets()
    {
        SetupMock();
        try
        {
            var capturedColors = new List<ConsoleColor>();
            _mockConsole.SetupSet(x => x.ForegroundColor = It.IsAny<ConsoleColor>())
                .Callback<ConsoleColor>(c => capturedColors.Add(c));

            ConsoleUi.PrintSuccess("test message");

            Assert.Contains(ConsoleColor.Green, capturedColors);
            _mockConsole.Verify(x => x.ResetColor(), Times.Once);
        }
        finally
        {
            ResetToSystemConsole();
        }
    }

    [Fact]
    public void PrintSuccess_WritesFormattedMessageWithCheckmark()
    {
        SetupMock();
        try
        {
            string? capturedLine = null;
            _mockConsole.Setup(x => x.Write(It.IsAny<string?>())).Callback<string?>(s => capturedLine = s);

            ConsoleUi.PrintSuccess("upload complete");

            Assert.Contains("✓", capturedLine ?? "");
            Assert.Contains("upload complete", capturedLine ?? "");
        }
        finally
        {
            ResetToSystemConsole();
        }
    }

    #endregion

    #region PrintError

    [Fact]
    public void PrintError_SetsRedForegroundAndResets()
    {
        SetupMock();
        try
        {
            var capturedColors = new List<ConsoleColor>();
            _mockConsole.SetupSet(x => x.ForegroundColor = It.IsAny<ConsoleColor>())
                .Callback<ConsoleColor>(c => capturedColors.Add(c));

            ConsoleUi.PrintError("something went wrong");

            Assert.Contains(ConsoleColor.Red, capturedColors);
            _mockConsole.Verify(x => x.ResetColor(), Times.Once);
        }
        finally
        {
            ResetToSystemConsole();
        }
    }

    [Fact]
    public void PrintError_WritesFormattedMessageWithCross()
    {
        SetupMock();
        try
        {
            string? capturedLine = null;
            _mockConsole.Setup(x => x.WriteLine(It.IsAny<string?>())).Callback<string?>(s => capturedLine = s);

            ConsoleUi.PrintError("connection failed");

            Assert.Contains("✗", capturedLine ?? "");
            Assert.Contains("connection failed", capturedLine ?? "");
        }
        finally
        {
            ResetToSystemConsole();
        }
    }

    #endregion

    #region PrintWarning

    [Fact]
    public void PrintWarning_SetsYellowForegroundAndResets()
    {
        SetupMock();
        try
        {
            var capturedColors = new List<ConsoleColor>();
            _mockConsole.SetupSet(x => x.ForegroundColor = It.IsAny<ConsoleColor>())
                .Callback<ConsoleColor>(c => capturedColors.Add(c));

            ConsoleUi.PrintWarning("low battery");

            Assert.Contains(ConsoleColor.Yellow, capturedColors);
            _mockConsole.Verify(x => x.ResetColor(), Times.Once);
        }
        finally
        {
            ResetToSystemConsole();
        }
    }

    [Fact]
    public void PrintWarning_WritesFormattedMessage()
    {
        SetupMock();
        try
        {
            string? capturedLine = null;
            _mockConsole.Setup(x => x.WriteLine(It.IsAny<string?>())).Callback<string?>(s => capturedLine = s);

            ConsoleUi.PrintWarning("check settings");

            Assert.Contains("⚠", capturedLine ?? "");
            Assert.Contains("check settings", capturedLine ?? "");
        }
        finally
        {
            ResetToSystemConsole();
        }
    }

    #endregion

    #region PrintInfo

    [Fact]
    public void PrintInfo_SetsDarkGrayForegroundAndResets()
    {
        SetupMock();
        try
        {
            var capturedColors = new List<ConsoleColor>();
            _mockConsole.SetupSet(x => x.ForegroundColor = It.IsAny<ConsoleColor>())
                .Callback<ConsoleColor>(c => capturedColors.Add(c));

            ConsoleUi.PrintInfo("some info");

            Assert.Contains(ConsoleColor.DarkGray, capturedColors);
            _mockConsole.Verify(x => x.ResetColor(), Times.Once);
        }
        finally
        {
            ResetToSystemConsole();
        }
    }

    [Fact]
    public void PrintInfo_WritesFormattedMessage()
    {
        SetupMock();
        try
        {
            string? capturedLine = null;
            _mockConsole.Setup(x => x.WriteLine(It.IsAny<string?>())).Callback<string?>(s => capturedLine = s);

            ConsoleUi.PrintInfo("listening...");

            Assert.Contains("listening...", capturedLine ?? "");
        }
        finally
        {
            ResetToSystemConsole();
        }
    }

    #endregion

    #region PrintRecordingIndicator

    [Fact]
    public void PrintRecordingIndicator_WhenIsRecordingTrue_SetsRedForeground()
    {
        SetupMock();
        try
        {
            var capturedColors = new List<ConsoleColor>();
            _mockConsole.SetupSet(x => x.ForegroundColor = It.IsAny<ConsoleColor>())
                .Callback<ConsoleColor>(c => capturedColors.Add(c));

            ConsoleUi.PrintRecordingIndicator(true, "Alt+=", true);

            Assert.Contains(ConsoleColor.Red, capturedColors);
        }
        finally
        {
            ResetToSystemConsole();
        }
    }

    [Fact]
    public void PrintRecordingIndicator_WhenIsRecordingTrue_WritesRecIndicator()
    {
        SetupMock();
        try
        {
            string? capturedWrite = null;
            _mockConsole.Setup(x => x.Write(It.IsAny<string?>())).Callback<string?>(s => capturedWrite = s);

            ConsoleUi.PrintRecordingIndicator(true, "Alt+=", true);

            Assert.Contains("●", capturedWrite ?? "");
            Assert.Contains("REC", capturedWrite ?? "");
        }
        finally
        {
            ResetToSystemConsole();
        }
    }

    [Fact]
    public void PrintRecordingIndicator_WhenIsRecordingFalse_DoesNotWrite()
    {
        SetupMock();
        try
        {
            var writeCallCount = 0;
            _mockConsole.Setup(x => x.Write(It.IsAny<string?>())).Callback(() => writeCallCount++);
            _mockConsole.Setup(x => x.WriteLine(It.IsAny<string?>())).Callback(() => writeCallCount++);

            ConsoleUi.PrintRecordingIndicator(false, "Alt+=", true);

            Assert.Equal(0, writeCallCount);
        }
        finally
        {
            ResetToSystemConsole();
        }
    }

    [Fact]
    public void PrintRecordingIndicator_WhenIsRecordingTrue_ResetsColor()
    {
        SetupMock();
        try
        {
            var capturedColors = new List<ConsoleColor>();
            _mockConsole.SetupSet(x => x.ForegroundColor = It.IsAny<ConsoleColor>())
                .Callback<ConsoleColor>(c => capturedColors.Add(c));

            ConsoleUi.PrintRecordingIndicator(true, "Alt+=", true);

            _mockConsole.Verify(x => x.ResetColor(), Times.Once);
        }
        finally
        {
            ResetToSystemConsole();
        }
    }

    #endregion

    #region PrintAgentReply

    [Fact]
    public void PrintAgentReply_WritesBlankLineFirst()
    {
        SetupMock();
        try
        {
            var writeLineCalls = new List<string?>();
            _mockConsole.Setup(x => x.WriteLine(It.IsAny<string?>())).Callback<string?>(writeLineCalls.Add);

            ConsoleUi.PrintAgentReply("  🤖 Agent: ", "Hello world");

            // WriteLine() with no arg is called first — produces a blank line
            // We expect the first entry to be null (no-arg WriteLine → null param)
            Assert.True(writeLineCalls.Count >= 1, $"Expected at least 1 WriteLine call, got {writeLineCalls.Count}");
            // First call should be blank (null or empty — represents blank line)
            Assert.True(string.IsNullOrEmpty(writeLineCalls[0]), $"First WriteLine should be blank, got: {writeLineCalls[0]}");
        }
        finally
        {
            ResetToSystemConsole();
        }
    }

    [Fact]
    public void PrintAgentReply_SetsCyanForeground()
    {
        SetupMock();
        try
        {
            var capturedColors = new List<ConsoleColor>();
            _mockConsole.SetupSet(x => x.ForegroundColor = It.IsAny<ConsoleColor>())
                .Callback<ConsoleColor>(c => capturedColors.Add(c));

            ConsoleUi.PrintAgentReply("  🤖 Agent: ", "Hello world");

            Assert.Contains(ConsoleColor.Cyan, capturedColors);
        }
        finally
        {
            ResetToSystemConsole();
        }
    }

    [Fact]
    public void PrintAgentReply_WritesPrefixWithCyanThenBodyThenTrailingBlankLine()
    {
        SetupMock();
        try
        {
            var writeCalls = new List<string?>();
            var writeLineCalls = new List<string?>();
            _mockConsole.Setup(x => x.Write(It.IsAny<string?>())).Callback<string?>(writeCalls.Add);
            _mockConsole.Setup(x => x.WriteLine(It.IsAny<string?>())).Callback<string?>(writeLineCalls.Add);

            ConsoleUi.PrintAgentReply("  🤖 Agent: ", "Hello world");

            Assert.Contains("  🤖 Agent: ", writeCalls);
            Assert.Contains("Hello world", writeLineCalls);
            // Trailing blank line: last WriteLine call should produce a blank line (null or empty)
            Assert.True(string.IsNullOrEmpty(writeLineCalls.Last()), $"Last WriteLine should be blank, got: {writeLineCalls.Last()}");
        }
        finally
        {
            ResetToSystemConsole();
        }
    }

    [Fact]
    public void PrintAgentReply_ResetsColorAfterPrefix()
    {
        SetupMock();
        try
        {
            var capturedColors = new List<ConsoleColor>();
            _mockConsole.SetupSet(x => x.ForegroundColor = It.IsAny<ConsoleColor>())
                .Callback<ConsoleColor>(c => capturedColors.Add(c));

            ConsoleUi.PrintAgentReply("  🤖 Agent: ", "Hello world");

            // ResetColor should be called after Cyan prefix
            _mockConsole.Verify(x => x.ResetColor(), Times.AtLeastOnce);
        }
        finally
        {
            ResetToSystemConsole();
        }
    }

    #endregion

    #region Log / LogOk / LogError

    [Fact]
    public void Log_FormatsTagAndMessage()
    {
        SetupMock();
        try
        {
            string? capturedWrite = null;
            string? capturedWriteLine = null;
            _mockConsole.Setup(x => x.Write(It.IsAny<string?>())).Callback<string?>(s => capturedWrite = s);
            _mockConsole.Setup(x => x.WriteLine(It.IsAny<string?>())).Callback<string?>(s => capturedWriteLine = s);

            ConsoleUi.Log("AGENT", "hello");

            Assert.Contains("[AGENT]", capturedWrite ?? "");
            Assert.Contains("hello", capturedWriteLine ?? "");
        }
        finally
        {
            ResetToSystemConsole();
        }
    }

    [Fact]
    public void Log_SetsDarkGrayForegroundAndResets()
    {
        SetupMock();
        try
        {
            var capturedColors = new List<ConsoleColor>();
            _mockConsole.SetupSet(x => x.ForegroundColor = It.IsAny<ConsoleColor>())
                .Callback<ConsoleColor>(c => capturedColors.Add(c));

            ConsoleUi.Log("TAG", "msg");

            Assert.Contains(ConsoleColor.DarkGray, capturedColors);
            _mockConsole.Verify(x => x.ResetColor(), Times.Once);
        }
        finally
        {
            ResetToSystemConsole();
        }
    }

    [Fact]
    public void LogOk_FormatsTagAndMessage()
    {
        SetupMock();
        try
        {
            string? capturedWrite = null;
            string? capturedWriteLine = null;
            _mockConsole.Setup(x => x.Write(It.IsAny<string?>())).Callback<string?>(s => capturedWrite = s);
            _mockConsole.Setup(x => x.WriteLine(It.IsAny<string?>())).Callback<string?>(s => capturedWriteLine = s);

            ConsoleUi.LogOk("OK", "done");

            Assert.Contains("[OK]", capturedWrite ?? "");
            Assert.Contains("done", capturedWriteLine ?? "");
        }
        finally
        {
            ResetToSystemConsole();
        }
    }

    [Fact]
    public void LogOk_SetsGreenForegroundAndResets()
    {
        SetupMock();
        try
        {
            var capturedColors = new List<ConsoleColor>();
            _mockConsole.SetupSet(x => x.ForegroundColor = It.IsAny<ConsoleColor>())
                .Callback<ConsoleColor>(c => capturedColors.Add(c));

            ConsoleUi.LogOk("OK", "done");

            Assert.Contains(ConsoleColor.Green, capturedColors);
            _mockConsole.Verify(x => x.ResetColor(), Times.Once);
        }
        finally
        {
            ResetToSystemConsole();
        }
    }

    [Fact]
    public void LogError_FormatsTagAndMessage()
    {
        SetupMock();
        try
        {
            string? capturedWrite = null;
            string? capturedWriteLine = null;
            _mockConsole.Setup(x => x.Write(It.IsAny<string?>())).Callback<string?>(s => capturedWrite = s);
            _mockConsole.Setup(x => x.WriteLine(It.IsAny<string?>())).Callback<string?>(s => capturedWriteLine = s);

            ConsoleUi.LogError("ERR", "failed");

            Assert.Contains("[ERR]", capturedWrite ?? "");
            Assert.Contains("failed", capturedWriteLine ?? "");
        }
        finally
        {
            ResetToSystemConsole();
        }
    }

    [Fact]
    public void LogError_SetsRedForegroundAndResets()
    {
        SetupMock();
        try
        {
            var capturedColors = new List<ConsoleColor>();
            _mockConsole.SetupSet(x => x.ForegroundColor = It.IsAny<ConsoleColor>())
                .Callback<ConsoleColor>(c => capturedColors.Add(c));

            ConsoleUi.LogError("ERR", "failed");

            Assert.Contains(ConsoleColor.Red, capturedColors);
            _mockConsole.Verify(x => x.ResetColor(), Times.Once);
        }
        finally
        {
            ResetToSystemConsole();
        }
    }

    #endregion

    #region PrintGatewayError

    [Fact]
    public void PrintGatewayError_SetsRedForeground()
    {
        SetupMock();
        try
        {
            var capturedColors = new List<ConsoleColor>();
            _mockConsole.SetupSet(x => x.ForegroundColor = It.IsAny<ConsoleColor>())
                .Callback<ConsoleColor>(c => capturedColors.Add(c));

            ConsoleUi.PrintGatewayError("connection refused");

            Assert.Contains(ConsoleColor.Red, capturedColors);
        }
        finally
        {
            ResetToSystemConsole();
        }
    }

    [Fact]
    public void PrintGatewayError_FormatsMessageWithPrefix()
    {
        SetupMock();
        try
        {
            string? capturedLine = null;
            _mockConsole.Setup(x => x.WriteLine(It.IsAny<string?>())).Callback<string?>(s => capturedLine = s);

            ConsoleUi.PrintGatewayError("timeout");

            Assert.Contains("Gateway error:", capturedLine ?? "");
            Assert.Contains("timeout", capturedLine ?? "");
        }
        finally
        {
            ResetToSystemConsole();
        }
    }

    [Fact]
    public void PrintGatewayError_WithDetailCode_WritesDetailOnSeparateLine()
    {
        SetupMock();
        try
        {
            var writeLineCalls = new List<string?>();
            _mockConsole.Setup(x => x.WriteLine(It.IsAny<string?>())).Callback<string?>(writeLineCalls.Add);

            ConsoleUi.PrintGatewayError("auth failed", "AUTH_401", null);

            var allOutput = string.Join("", writeLineCalls.Where(s => s != null));
            Assert.Contains("AUTH_401", allOutput);
        }
        finally
        {
            ResetToSystemConsole();
        }
    }

    [Fact]
    public void PrintGatewayError_WithRecommendedStep_WritesRecommendedOnSeparateLine()
    {
        SetupMock();
        try
        {
            var writeLineCalls = new List<string?>();
            _mockConsole.Setup(x => x.WriteLine(It.IsAny<string?>())).Callback<string?>(writeLineCalls.Add);

            ConsoleUi.PrintGatewayError("auth failed", null, "Run openclaw auth login");

            var allOutput = string.Join("", writeLineCalls.Where(s => s != null));
            Assert.Contains("Run openclaw auth login", allOutput);
        }
        finally
        {
            ResetToSystemConsole();
        }
    }

    [Fact]
    public void PrintGatewayError_ResetsColorAfterMessage()
    {
        SetupMock();
        try
        {
            var capturedColors = new List<ConsoleColor>();
            _mockConsole.SetupSet(x => x.ForegroundColor = It.IsAny<ConsoleColor>())
                .Callback<ConsoleColor>(c => capturedColors.Add(c));

            ConsoleUi.PrintGatewayError("timeout", null, null);

            _mockConsole.Verify(x => x.ResetColor(), Times.AtLeastOnce);
        }
        finally
        {
            ResetToSystemConsole();
        }
    }

    #endregion

    #region SetConsole / Static IConsole

    [Fact]
    public void SetConsole_AfterSet_MockReceivesCalls()
    {
        SetupMock();
        try
        {
            var writeCallCount = 0;
            _mockConsole.Setup(x => x.Write(It.IsAny<string?>())).Callback(() => writeCallCount++);

            ConsoleUi.PrintSuccess("test");

            Assert.Equal(1, writeCallCount);
        }
        finally
        {
            ResetToSystemConsole();
        }
    }

    [Fact]
    public void SetConsole_AfterResetToSystemConsole_SystemConsoleReceivesCalls()
    {
        var mock = new Mock<IConsole>();
        mock.Setup(x => x.WindowWidth).Returns(120);
        mock.Setup(x => x.OutputEncoding).Returns(Encoding.UTF8);
        mock.Setup(x => x.TreatControlCAsInput).Returns(false);
        mock.Setup(x => x.Write(It.IsAny<string?>()));
        mock.Setup(x => x.WriteLine(It.IsAny<string?>()));
        mock.SetupSet(x => x.ForegroundColor = It.IsAny<ConsoleColor>());
        mock.Setup(x => x.ResetColor());

        ConsoleUi.SetConsole(mock.Object);
        ConsoleUi.PrintSuccess("test with mock");

        // Reset to system console
        ConsoleUi.SetConsole(new SystemConsole());
        ConsoleUi.PrintSuccess("test with real console");

        // Mock should not have received the second call
        var callCountBeforeReset = mock.Invocations.Count;
        mock.Verify(x => x.WriteLine(It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public void SetConsole_CanSwitchBetweenMocks()
    {
        var mock1 = new Mock<IConsole>();
        mock1.Setup(x => x.WindowWidth).Returns(120);
        mock1.Setup(x => x.OutputEncoding).Returns(Encoding.UTF8);
        mock1.Setup(x => x.TreatControlCAsInput).Returns(false);
        var callCount1 = 0;
        mock1.Setup(x => x.Write(It.IsAny<string?>())).Callback(() => callCount1++);

        var mock2 = new Mock<IConsole>();
        mock2.Setup(x => x.WindowWidth).Returns(120);
        mock2.Setup(x => x.OutputEncoding).Returns(Encoding.UTF8);
        mock2.Setup(x => x.TreatControlCAsInput).Returns(false);
        var callCount2 = 0;
        mock2.Setup(x => x.Write(It.IsAny<string?>())).Callback(() => callCount2++);

        try
        {
            ConsoleUi.SetConsole(mock1.Object);
            ConsoleUi.PrintSuccess("one");

            ConsoleUi.SetConsole(mock2.Object);
            ConsoleUi.PrintSuccess("two");

            Assert.Equal(1, callCount1);
            Assert.Equal(1, callCount2);
        }
        finally
        {
            ResetToSystemConsole();
        }
    }

    #endregion

    #region Non-input methods do not call ReadKey

    [Fact]
    public void PrintBanner_DoesNotCallReadKey()
    {
        SetupMock();
        try
        {
            ConsoleUi.PrintBanner();
            _mockConsole.Verify(x => x.ReadKey(It.IsAny<bool>()), Times.Never);
        }
        finally
        {
            ResetToSystemConsole();
        }
    }

    [Fact]
    public void PrintSuccess_DoesNotCallReadKey()
    {
        SetupMock();
        try
        {
            ConsoleUi.PrintSuccess("test");
            _mockConsole.Verify(x => x.ReadKey(It.IsAny<bool>()), Times.Never);
        }
        finally
        {
            ResetToSystemConsole();
        }
    }

    #endregion
}
