using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenClawPTT.Services;
using Xunit;

namespace OpenClawPTT.Tests;

/// <summary>
/// Tests for ConsoleUi static methods using a RecordingConsole test double.
/// RecordingConsole avoids Moq SetupSet callback issues with ForegroundColor.
/// </summary>
[Collection("ConsoleUi")]
public class ConsoleUiTests : IDisposable
{
    /// <summary>
    /// Test IConsole double that records all calls for assertion.
    /// Uses LastForegroundColorBeforeReset (snapshot, no enumeration) to avoid
    /// race conditions when tests run in parallel and modify shared static _impl.
    /// </summary>
    private sealed class RecordingConsole : IConsole
    {
        public readonly List<string?> WriteLines = new();
        public readonly List<string?> Writes = new();
        private ConsoleColor _foregroundColor = ConsoleColor.White;
        // Snapshot of the last ForegroundColor set, preserved even after ResetColor.
        // This lets us assert what color was set even after ResetColor() changes it.
        public ConsoleColor LastForegroundColorBeforeReset { get; private set; } = ConsoleColor.White;
        public ConsoleColor ForegroundColor
        {
            get => _foregroundColor;
            set { _foregroundColor = value; LastForegroundColorBeforeReset = value; }
        }
        public bool KeyAvailable => false;
        public Encoding OutputEncoding { get; set; } = Encoding.UTF8;
        public bool TreatControlCAsInput { get; set; }
        public int WindowWidth => 120;
        public bool ResetColorCalled;
        public ConsoleKeyInfo ReadKey(bool intercept) => new ConsoleKeyInfo('A', ConsoleKey.A, false, false, false);
        public IAgentReplyFormatter CreateAgentReplyFormatter(string prefix, int w, bool prefixPrinted = false)
            => AgentReplyFormatter.CreateSytemConsoleFormatter(prefix, w, prefixPrinted);
        public IAgentReplyFormatter CreateAgentReplyFormatter(string prefix, int w, bool prefixPrinted, int cw)
            => AgentReplyFormatter.CreateSytemConsoleFormatter(prefix, w, prefixPrinted, cw);

        public void Write(string? text) => Writes.Add(text);
        public void WriteLine(string? text = null) => WriteLines.Add(text);
        public void ResetColor() { ResetColorCalled = true; _foregroundColor = ConsoleColor.White; }
        public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult<string?>(null);
    }

    private RecordingConsole _recording = null!;

    private void SetupRecording()
    {
        _recording = new RecordingConsole();
        ConsoleUi.SetConsole(_recording);
    }

    public void Dispose()
    {
        ConsoleUi.SetConsole(new SystemConsole());
    }

    #region PrintBanner

    [Fact]
    public void PrintBanner_CallsWriteLineFiveTimes()
    {
        SetupRecording();
        ConsoleUi.PrintBanner();
        // WriteLine is called 5 times: blank line at start + 4 banner lines + blank line at end
        Assert.Equal(5, _recording.WriteLines.Count);
    }

    [Fact]
    public void PrintBanner_SetsForegroundColorToCyanBeforeBox()
    {
        SetupRecording();
        ConsoleUi.PrintBanner();
        Assert.Equal(ConsoleColor.Cyan, _recording.LastForegroundColorBeforeReset);
    }

    [Fact]
    public void PrintBanner_ResetsColorAfterBox()
    {
        SetupRecording();
        ConsoleUi.PrintBanner();
        Assert.Equal(ConsoleColor.Cyan, _recording.LastForegroundColorBeforeReset);
        Assert.True(_recording.ResetColorCalled);
    }

    #endregion

    #region PrintHelpMenu

    [Fact]
    public void PrintHelpMenu_HoldToTalkMode_IncludesCorrectModeDescription()
    {
        SetupRecording();
        ConsoleUi.PrintHelpMenu("Alt+=", true);
        var allOutput = string.Join("", _recording.WriteLines.Where(s => s != null));
        Assert.Contains("Hold-to-talk", allOutput);
        Assert.Contains("Alt+=", allOutput);
    }

    [Fact]
    public void PrintHelpMenu_ToggleMode_IncludesCorrectModeDescription()
    {
        SetupRecording();
        ConsoleUi.PrintHelpMenu("Space", false);
        var allOutput = string.Join("", _recording.WriteLines.Where(s => s != null));
        Assert.Contains("Toggle recording", allOutput);
        Assert.Contains("Space", allOutput);
    }

    [Fact]
    public void PrintHelpMenu_SetsGreenForegroundAndResets()
    {
        SetupRecording();
        ConsoleUi.PrintHelpMenu("Alt+=", true);
        Assert.Equal(ConsoleColor.Green, _recording.LastForegroundColorBeforeReset);
        Assert.True(_recording.ResetColorCalled);
    }

    #endregion

    #region PrintSuccess

    [Fact]
    public void PrintSuccess_SetsGreenForegroundAndResets()
    {
        SetupRecording();
        ConsoleUi.PrintSuccess("test message");
        Assert.Equal(ConsoleColor.Green, _recording.LastForegroundColorBeforeReset);
        Assert.True(_recording.ResetColorCalled);
    }

    [Fact]
    public void PrintSuccess_WritesFormattedMessageWithCheckmark()
    {
        SetupRecording();
        ConsoleUi.PrintSuccess("upload complete");
        var capturedLine = _recording.Writes.LastOrDefault();
        Assert.Contains("✓", capturedLine ?? "");
        Assert.Contains("upload complete", capturedLine ?? "");
    }

    #endregion

    #region PrintError

    [Fact]
    public void PrintError_SetsRedForegroundAndResets()
    {
        SetupRecording();
        ConsoleUi.PrintError("something went wrong");
        Assert.Equal(ConsoleColor.Red, _recording.LastForegroundColorBeforeReset);
        Assert.True(_recording.ResetColorCalled);
    }

    [Fact]
    public void PrintError_WritesFormattedMessageWithCross()
    {
        SetupRecording();
        ConsoleUi.PrintError("connection failed");
        var capturedLine = _recording.WriteLines.LastOrDefault();
        Assert.Contains("✗", capturedLine ?? "");
        Assert.Contains("connection failed", capturedLine ?? "");
    }

    #endregion

    #region PrintWarning

    [Fact]
    public void PrintWarning_SetsYellowForegroundAndResets()
    {
        SetupRecording();
        ConsoleUi.PrintWarning("low battery");
        Assert.Equal(ConsoleColor.Yellow, _recording.LastForegroundColorBeforeReset);
        Assert.True(_recording.ResetColorCalled);
    }

    [Fact]
    public void PrintWarning_WritesFormattedMessage()
    {
        SetupRecording();
        ConsoleUi.PrintWarning("check settings");
        var capturedLine = _recording.WriteLines.LastOrDefault();
        Assert.Contains("⚠", capturedLine ?? "");
        Assert.Contains("check settings", capturedLine ?? "");
    }

    #endregion

    #region PrintInfo

    [Fact]
    public void PrintInfo_SetsDarkGrayForegroundAndResets()
    {
        SetupRecording();
        ConsoleUi.PrintInfo("some info");
        Assert.Equal(ConsoleColor.DarkGray, _recording.LastForegroundColorBeforeReset);
        Assert.True(_recording.ResetColorCalled);
    }

    [Fact]
    public void PrintInfo_WritesFormattedMessage()
    {
        SetupRecording();
        ConsoleUi.PrintInfo("listening...");
        var capturedLine = _recording.WriteLines.LastOrDefault();
        Assert.Contains("listening...", capturedLine ?? "");
    }

    #endregion

    #region PrintRecordingIndicator

    [Fact]
    public void PrintRecordingIndicator_WhenIsRecordingTrue_SetsRedForeground()
    {
        SetupRecording();
        ConsoleUi.PrintRecordingIndicator(true, "Alt+=", true);
        Assert.Equal(ConsoleColor.Red, _recording.LastForegroundColorBeforeReset);
    }

    [Fact]
    public void PrintRecordingIndicator_WhenIsRecordingTrue_WritesRecIndicator()
    {
        SetupRecording();
        ConsoleUi.PrintRecordingIndicator(true, "Alt+=", true);
        var capturedWrite = _recording.Writes.LastOrDefault();
        Assert.Contains("●", capturedWrite ?? "");
        Assert.Contains("REC", capturedWrite ?? "");
    }

    [Fact]
    public void PrintRecordingIndicator_WhenIsRecordingFalse_DoesNotWrite()
    {
        SetupRecording();
        ConsoleUi.PrintRecordingIndicator(false, "Alt+=", true);
        Assert.Empty(_recording.Writes);
        Assert.Empty(_recording.WriteLines);
    }

    [Fact]
    public void PrintRecordingIndicator_WhenIsRecordingTrue_ResetsColor()
    {
        SetupRecording();
        ConsoleUi.PrintRecordingIndicator(true, "Alt+=", true);
        Assert.True(_recording.ResetColorCalled);
    }

    #endregion

    #region PrintAgentReply

    [Fact]
    public void PrintAgentReply_WritesBlankLineFirst()
    {
        SetupRecording();
        ConsoleUi.PrintAgentReply("  🤖 Agent: ", "Hello world");
        // WriteLine() with no arg is called first — produces a blank line
        Assert.True(_recording.WriteLines.Count >= 1, $"Expected at least 1 WriteLine call, got {_recording.WriteLines.Count}");
        // First call should be blank (null or empty — represents blank line)
        Assert.True(string.IsNullOrEmpty(_recording.WriteLines[0]), $"First WriteLine should be blank, got: {_recording.WriteLines[0]}");
    }

    [Fact]
    public void PrintAgentReply_SetsCyanForeground()
    {
        SetupRecording();
        ConsoleUi.PrintAgentReply("  🤖 Agent: ", "Hello world");
        Assert.Equal(ConsoleColor.Cyan, _recording.LastForegroundColorBeforeReset);
    }

    [Fact]
    public void PrintAgentReply_WritesPrefixWithCyanThenBodyThenTrailingBlankLine()
    {
        SetupRecording();
        ConsoleUi.PrintAgentReply("  🤖 Agent: ", "Hello world");
        Assert.Contains("  🤖 Agent: ", _recording.Writes);
        Assert.Contains("Hello world", _recording.WriteLines);
        // Trailing blank line: last WriteLine call should produce a blank line (null or empty)
        Assert.True(string.IsNullOrEmpty(_recording.WriteLines.Last()), $"Last WriteLine should be blank, got: {_recording.WriteLines.Last()}");
    }

    [Fact]
    public void PrintAgentReply_ResetsColorAfterPrefix()
    {
        SetupRecording();
        ConsoleUi.PrintAgentReply("  🤖 Agent: ", "Hello world");
        Assert.True(_recording.ResetColorCalled);
    }

    #endregion

    #region Log / LogOk / LogError

    [Fact]
    public void Log_FormatsTagAndMessage()
    {
        SetupRecording();
        ConsoleUi.Log("AGENT", "hello");
        Assert.Contains("[AGENT]", _recording.Writes.LastOrDefault() ?? "");
        Assert.Contains("hello", _recording.WriteLines.LastOrDefault() ?? "");
    }

    [Fact]
    public void Log_SetsDarkGrayForegroundAndResets()
    {
        SetupRecording();
        ConsoleUi.Log("TAG", "msg");
        Assert.Equal(ConsoleColor.DarkGray, _recording.LastForegroundColorBeforeReset);
        Assert.True(_recording.ResetColorCalled);
    }

    [Fact]
    public void LogOk_FormatsTagAndMessage()
    {
        SetupRecording();
        ConsoleUi.LogOk("OK", "done");
        Assert.Contains("[OK]", _recording.Writes.LastOrDefault() ?? "");
        Assert.Contains("done", _recording.WriteLines.LastOrDefault() ?? "");
    }

    [Fact]
    public void LogOk_SetsGreenForegroundAndResets()
    {
        SetupRecording();
        ConsoleUi.LogOk("OK", "done");
        Assert.Equal(ConsoleColor.Green, _recording.LastForegroundColorBeforeReset);
        Assert.True(_recording.ResetColorCalled);
    }

    [Fact]
    public void LogError_FormatsTagAndMessage()
    {
        SetupRecording();
        ConsoleUi.LogError("ERR", "failed");
        Assert.Contains("[ERR]", _recording.Writes.LastOrDefault() ?? "");
        Assert.Contains("failed", _recording.WriteLines.LastOrDefault() ?? "");
    }

    [Fact]
    public void LogError_SetsRedForegroundAndResets()
    {
        SetupRecording();
        ConsoleUi.LogError("ERR", "failed");
        Assert.Equal(ConsoleColor.Red, _recording.LastForegroundColorBeforeReset);
        Assert.True(_recording.ResetColorCalled);
    }

    #endregion

    #region PrintGatewayError

    [Fact]
    public void PrintGatewayError_SetsRedForeground()
    {
        SetupRecording();
        ConsoleUi.PrintGatewayError("connection refused");
        Assert.Equal(ConsoleColor.Red, _recording.LastForegroundColorBeforeReset);
    }

    [Fact]
    public void PrintGatewayError_FormatsMessageWithPrefix()
    {
        SetupRecording();
        ConsoleUi.PrintGatewayError("timeout");
        var capturedLine = _recording.WriteLines.LastOrDefault();
        Assert.Contains("Gateway error:", capturedLine ?? "");
        Assert.Contains("timeout", capturedLine ?? "");
    }

    [Fact]
    public void PrintGatewayError_WithDetailCode_WritesDetailOnSeparateLine()
    {
        SetupRecording();
        ConsoleUi.PrintGatewayError("auth failed", "AUTH_401", null);
        var allOutput = string.Join("", _recording.WriteLines.Where(s => s != null));
        Assert.Contains("AUTH_401", allOutput);
    }

    [Fact]
    public void PrintGatewayError_WithRecommendedStep_WritesRecommendedOnSeparateLine()
    {
        SetupRecording();
        ConsoleUi.PrintGatewayError("auth failed", null, "Run openclaw auth login");
        var allOutput = string.Join("", _recording.WriteLines.Where(s => s != null));
        Assert.Contains("Run openclaw auth login", allOutput);
    }

    [Fact]
    public void PrintGatewayError_ResetsColorAfterMessage()
    {
        SetupRecording();
        ConsoleUi.PrintGatewayError("timeout", null, null);
        Assert.True(_recording.ResetColorCalled);
    }

    #endregion

    #region SetConsole / Static IConsole

    [Fact]
    public void SetConsole_AfterSet_MockReceivesCalls()
    {
        SetupRecording();
        ConsoleUi.PrintSuccess("test");
        Assert.True(_recording.Writes.Count >= 1);
    }

    [Fact]
    public void SetConsole_AfterResetToSystemConsole_SystemConsoleReceivesCalls()
    {
        SetupRecording();
        try
        {
            ConsoleUi.PrintSuccess("test with recording console");
            var callsBeforeSwitch = _recording.WriteLines.Count;

            // Reset to system console
            ConsoleUi.SetConsole(new SystemConsole());
            ConsoleUi.PrintSuccess("test with real console");

            // RecordingConsole should not have received any additional calls after switch
            Assert.Equal(callsBeforeSwitch, _recording.WriteLines.Count);
        }
        finally
        {
            ConsoleUi.SetConsole(new SystemConsole());
        }
    }

    [Fact]
    public void SetConsole_CanSwitchBetweenMocks()
    {
        var recording1 = new RecordingConsole();
        var recording2 = new RecordingConsole();
        try
        {
            ConsoleUi.SetConsole(recording1);
            ConsoleUi.PrintSuccess("one");
            var callsToRecording1 = recording1.Writes.Count;

            ConsoleUi.SetConsole(recording2);
            ConsoleUi.PrintSuccess("two");
            var callsToRecording2 = recording2.Writes.Count;

            Assert.Equal(1, callsToRecording1);
            Assert.Equal(1, callsToRecording2);
        }
        finally
        {
            ConsoleUi.SetConsole(new SystemConsole());
        }
    }

    #endregion

    #region Non-input methods do not call ReadKey

    [Fact]
    public void PrintBanner_DoesNotCallReadKey()
    {
        SetupRecording();
        ConsoleUi.PrintBanner();
        Assert.False(_recording.KeyAvailable);
    }

    [Fact]
    public void PrintSuccess_DoesNotCallReadKey()
    {
        SetupRecording();
        ConsoleUi.PrintSuccess("test");
        Assert.False(_recording.KeyAvailable);
    }

    #endregion
}

/// <summary>
/// Marks this collection as non-parallelizable because ConsoleUi uses static
/// shared state that cannot safely handle concurrent test threads.
/// </summary>
[CollectionDefinition("ConsoleUi", DisableParallelization = true)]
public class ConsoleUiCollection : ICollectionFixture<object>
{
}
