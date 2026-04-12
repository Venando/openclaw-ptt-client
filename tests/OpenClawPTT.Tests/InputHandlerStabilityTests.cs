using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using OpenClawPTT.Services;
using Xunit;

namespace OpenClawPTT.Tests;

/// <summary>
/// Stability tests for InputHandler.
/// InputHandler uses raw Console.KeyAvailable / Console.ReadKey / Console.Write* calls,
/// NOT the injected IConsoleOutput. These tests verify behaviour that can be observed
/// through the injected dependencies (mockSender, mockConsole) and via the return value.
/// </summary>
public class InputHandlerStabilityTests : IDisposable
{
    /// <summary>
    /// IConsoleOutput double that records calls and optionally simulates key input.
    /// Mirrors the RecordingConsole pattern from ConsoleUiTests.cs but implements
    /// IConsoleOutput so it can be passed to InputHandler.
    /// </summary>
    private sealed class RecordingConsole : IConsoleOutput
    {
        public readonly List<string?> WriteLines = new();
        public readonly List<string?> Writes = new();
        private ConsoleColor _foregroundColor = ConsoleColor.White;
        public ConsoleColor LastForegroundColorBeforeReset { get; private set; } = ConsoleColor.White;
        public ConsoleColor ForegroundColor
        {
            get => _foregroundColor;
            set { _foregroundColor = value; LastForegroundColorBeforeReset = value; }
        }

        // Configurable key availability and key to return
        public bool KeyAvailable { get; set; }
        public ConsoleKeyInfo SimulatedKey { get; set; } = new ConsoleKeyInfo('A', ConsoleKey.A, false, false, false);
        public string SimulatedReadLineResult { get; set; } = "test message";

        public Encoding OutputEncoding { get; set; } = Encoding.UTF8;
        public bool TreatControlCAsInput { get; set; }
        public int WindowWidth => 120;
        public bool ResetColorCalled;

        public ConsoleKeyInfo ReadKey(bool intercept) => SimulatedKey;
        public IAgentReplyFormatter CreateAgentReplyFormatter(string prefix, int w, bool prefixPrinted = false)
            => new AgentReplyFormatter(prefix, w, prefixPrinted);
        public IAgentReplyFormatter CreateAgentReplyFormatter(string prefix, int w, bool prefixPrinted, int cw)
            => new AgentReplyFormatter(prefix, w, prefixPrinted, cw);

        public void Write(string? text) => Writes.Add(text);
        public void WriteLine(string? text = null) => WriteLines.Add(text);
        public void ResetColor() { ResetColorCalled = true; _foregroundColor = ConsoleColor.White; }
        public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult<string?>(SimulatedReadLineResult);

        // IConsoleOutput display methods — no-op for these stability tests
        public void PrintBanner() { }
        public void PrintHelpMenu(string hotkeyCombination, bool holdToTalk) { }
        public void PrintRecordingIndicator(bool isRecording, string hotkeyCombination, bool holdToTalk) { }
        public void PrintSuccess(string message) { }
        public void PrintSuccessWordWrap(string prefix, string message, int rightMarginIndent) { }
        public void PrintWarning(string message) { }
        public void PrintError(string message) { }
        public void PrintInfo(string message) { }
        public void PrintInlineInfo(string message) { }
        public void PrintInlineSuccess(string message) { }
        public void PrintGatewayError(string message, string? detailCode, string? recommendedStep) { }
        public void PrintAgentReply(string prefix, string body) { }
        public void PrintAgentReplyDelta(string prefix, string delta, string newlineSuffix) { }
        public void Log(string tag, string msg) { }
        public void LogOk(string tag, string msg) { }
        public void LogError(string tag, string msg) { }
    }

    /// <summary>
    /// Test double for ITextMessageSender that records calls and optionally throws.
    /// </summary>
    private sealed class RecordingTextSender : ITextMessageSender
    {
        public readonly List<string> SentMessages = new();
        public Exception? ExceptionToThrow;
        public bool ThrowOnSend;

        public Task SendAsync(string text, CancellationToken ct)
        {
            if (ThrowOnSend && ExceptionToThrow != null)
                throw ExceptionToThrow;
            SentMessages.Add(text);
            return Task.CompletedTask;
        }
    }

    private RecordingConsole _console = null!;
    private RecordingTextSender _sender = null!;
    private Mock<IConfigurationService> _mockConfig = null!;
    private InputHandler _handler = null!;

    private void SetupHandler()
    {
        _console = new RecordingConsole();
        // Route ConsoleUi static calls through our fake so HandleInputAsync
        // (which calls ConsoleUi.KeyAvailable / ConsoleUi.ReadKey) uses the mock.
        ConsoleUi.SetConsole(_console);
        _sender = new RecordingTextSender();
        _mockConfig = new Mock<IConfigurationService>();
        _mockConfig.Setup(c => c.Load()).Returns((AppConfig?)null);
        _handler = new InputHandler(_sender, _mockConfig.Object, _console);
    }

    public void Dispose()
    {
        // Restore system console so other tests aren't affected
        ConsoleUi.SetConsole(new SystemConsole());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 1: Mock Console.ReadKey returns 'Q' → HandleInputAsync returns Quit
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task HandleInputAsync_KeyQ_ReturnsQuit()
    {
        SetupHandler();
        _console.KeyAvailable = true;
        _console.SimulatedKey = new ConsoleKeyInfo('Q', ConsoleKey.Q, false, false, false);

        var result = await _handler.HandleInputAsync(CancellationToken.None);

        Assert.Equal(InputResult.Quit, result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 2: Mock Console.ReadKey returns 'T' → HandleInputAsync triggers text send
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task HandleInputAsync_KeyT_SendsTextMessage()
    {
        SetupHandler();
        _console.KeyAvailable = true;
        _console.SimulatedKey = new ConsoleKeyInfo('T', ConsoleKey.T, false, false, false);
        _console.SimulatedReadLineResult = "hello from test";

        var result = await _handler.HandleInputAsync(CancellationToken.None);

        Assert.Equal(InputResult.Continue, result);
        Assert.Contains("hello from test", _sender.SentMessages);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 3: Mock Console.ReadKey returns unknown key → returns Continue (no crash)
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task HandleInputAsync_UnknownKey_ReturnsContinue()
    {
        SetupHandler();
        _console.KeyAvailable = true;
        _console.SimulatedKey = new ConsoleKeyInfo('Z', ConsoleKey.Z, false, false, false);

        var result = await _handler.HandleInputAsync(CancellationToken.None);

        Assert.Equal(InputResult.Continue, result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 4: ITextMessageSender.SendAsync throws → HandleInputAsync swallows exception, returns Continue
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task HandleInputAsync_SenderThrows_ReturnsContinue()
    {
        SetupHandler();
        _console.KeyAvailable = true;
        _console.SimulatedKey = new ConsoleKeyInfo('T', ConsoleKey.T, false, false, false);
        _sender.ThrowOnSend = true;
        _sender.ExceptionToThrow = new InvalidOperationException("Network error");

        // Should NOT throw — exception must be swallowed
        var result = await _handler.HandleInputAsync(CancellationToken.None);

        Assert.Equal(InputResult.Continue, result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 5: HandleInputAsync with cancelled CancellationToken → returns immediately
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task HandleInputAsync_CancelledToken_ReturnsImmediately()
    {
        SetupHandler();
        _console.KeyAvailable = false; // no key, will hit Task.Delay path
        var cts = new CancellationTokenSource();
        cts.Cancel(); // cancel before call

        var result = await _handler.HandleInputAsync(cts.Token);

        Assert.Equal(InputResult.Continue, result);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Tests 6–7: Alt+R (HandleReconfiguration path)
    // ═══════════════════════════════════════════════════════════════════════

    // ─────────────────────────────────────────────────────────────────────────
    // Test 6: HandleInputAsync + Alt+R — null config → returns Continue (no crash)
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task HandleInputAsync_AltR_NullConfig_ReturnsContinue()
    {
        SetupHandler();
        _console.KeyAvailable = true;
        _console.SimulatedKey = new ConsoleKeyInfo('R', ConsoleKey.R, false, true, false); // Alt+R
        _mockConfig.Setup(c => c.Load()).Returns((AppConfig?)null);

        var result = await _handler.HandleInputAsync(CancellationToken.None);

        Assert.Equal(InputResult.Continue, result);
        _mockConfig.Verify(c => c.ReconfigureAsync(It.IsAny<AppConfig>()), Times.Never);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 7: HandleInputAsync + Alt+R — existing config → returns Restart
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task HandleInputAsync_AltR_WithExistingConfig_ReturnsRestart()
    {
        SetupHandler();
        _console.KeyAvailable = true;
        _console.SimulatedKey = new ConsoleKeyInfo('R', ConsoleKey.R, false, true, false); // Alt+R
        var existingCfg = new AppConfig { GatewayUrl = "ws://localhost:18789" };
        _mockConfig.Setup(c => c.Load()).Returns(existingCfg);
        _mockConfig.Setup(c => c.ReconfigureAsync(existingCfg)).Returns(Task.FromResult(existingCfg));


        var result = await _handler.HandleInputAsync(CancellationToken.None);

        Assert.Equal(InputResult.Restart, result);
        _mockConfig.Verify(c => c.ReconfigureAsync(existingCfg), Times.Once);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Tests 8–11: T key — HandleTypeMessageAsync path
    // ═══════════════════════════════════════════════════════════════════════

    // ─────────────────────────────────────────────────────────────────────────
    // Test 8: HandleInputAsync + T — text entered → sent via ITextMessageSender
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task HandleInputAsync_TKey_Text_SendsMessage()
    {
        SetupHandler();
        _console.KeyAvailable = true;
        _console.SimulatedKey = new ConsoleKeyInfo('T', ConsoleKey.T, false, false, false);
        _console.SimulatedReadLineResult = "hello from test";

        var result = await _handler.HandleInputAsync(CancellationToken.None);

        Assert.Equal(InputResult.Continue, result);
        Assert.Contains("hello from test", _sender.SentMessages);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 9: HandleInputAsync + T — empty input → no crash, no send
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task HandleInputAsync_TKey_EmptyInput_NoCrash()
    {
        SetupHandler();
        _console.KeyAvailable = true;
        _console.SimulatedKey = new ConsoleKeyInfo('T', ConsoleKey.T, false, false, false);
        _console.SimulatedReadLineResult = "";

        var result = await _handler.HandleInputAsync(CancellationToken.None);

        Assert.Equal(InputResult.Continue, result);
        Assert.Empty(_sender.SentMessages);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 10: HandleInputAsync + T — whitespace-only input → no crash, no send
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task HandleInputAsync_TKey_WhitespaceInput_NoCrash()
    {
        SetupHandler();
        _console.KeyAvailable = true;
        _console.SimulatedKey = new ConsoleKeyInfo('T', ConsoleKey.T, false, false, false);
        _console.SimulatedReadLineResult = "   \t  ";

        var result = await _handler.HandleInputAsync(CancellationToken.None);

        Assert.Equal(InputResult.Continue, result);
        Assert.Empty(_sender.SentMessages);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 11: HandleInputAsync + T — very long input → no crash, sent as-is
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task HandleInputAsync_TKey_LongInput_SendsMessage()
    {
        SetupHandler();
        _console.KeyAvailable = true;
        _console.SimulatedKey = new ConsoleKeyInfo('T', ConsoleKey.T, false, false, false);
        _console.SimulatedReadLineResult = new string('x', 10_000);

        var result = await _handler.HandleInputAsync(CancellationToken.None);

        Assert.Equal(InputResult.Continue, result);
        Assert.Single(_sender.SentMessages);
        Assert.Equal(10_000, _sender.SentMessages[0].Length);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 12: Full flow — T key → typed message end-to-end
    // ═══════════════════════════════════════════════════════════════════════

    // ─────────────────────────────────────────────────────────────────────────
    // Test 12: Full flow — T key → typed message → verified via sender
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task HandleInputAsync_TKey_FullFlow_SendsTypedMessage()
    {
        SetupHandler();
        _console.KeyAvailable = true;
        _console.SimulatedKey = new ConsoleKeyInfo('T', ConsoleKey.T, false, false, false);
        _console.SimulatedReadLineResult = "typed message from full flow test";

        var result = await _handler.HandleInputAsync(CancellationToken.None);

        Assert.Equal(InputResult.Continue, result);
        Assert.Contains("typed message from full flow test", _sender.SentMessages);
    }
}
