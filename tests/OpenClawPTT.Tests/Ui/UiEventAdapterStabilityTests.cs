using System.Text;
using System.Text.Json;
using OpenClawPTT;
using OpenClawPTT.Services;
using Xunit;

namespace OpenClawPTT.Tests;

/// <summary>
/// Stability and behavior tests for UiEventAdapter.
/// Verifies event wiring, formatter state machine, audio handler lifecycle,
/// and dispose behavior.
/// </summary>
public class UiEventAdapterStabilityTests : IDisposable
{
    #region Test Double Implementations

    /// <summary>
    /// Test double for IGatewayUIEvents — records subscription counts for each event.
    /// Uses explicit backing fields so the Raise methods can invoke delegates directly.
    /// </summary>
    private sealed class MockGatewayUIEvents : IGatewayUIEvents
    {
        private Action<string>? _agentReplyFull;
        private Action? _agentReplyDeltaStart;
        private Action<string>? _agentReplyDelta;
        private Action? _agentReplyDeltaEnd;
        private Action<string>? _agentThinking;
        private Action<string, string>? _agentToolCall;
        private Action<string, JsonElement>? _eventReceived;
        private Action<string>? _agentReplyAudio;

        // Subscription counters
        public int AgentReplyFullSubs { get; private set; }
        public int AgentReplyDeltaStartSubs { get; private set; }
        public int AgentReplyDeltaSubs { get; private set; }
        public int AgentReplyDeltaEndSubs { get; private set; }
        public int AgentThinkingSubs { get; private set; }
        public int AgentToolCallSubs { get; private set; }
        public int AgentReplyAudioSubs { get; private set; }

        public event Action<string>? AgentReplyFull
        {
            add { AgentReplyFullSubs++; _agentReplyFull += value; }
            remove { AgentReplyFullSubs--; _agentReplyFull -= value; }
        }
        public event Action? AgentReplyDeltaStart
        {
            add { AgentReplyDeltaStartSubs++; _agentReplyDeltaStart += value; }
            remove { AgentReplyDeltaStartSubs--; _agentReplyDeltaStart -= value; }
        }
        public event Action<string>? AgentReplyDelta
        {
            add { AgentReplyDeltaSubs++; _agentReplyDelta += value; }
            remove { AgentReplyDeltaSubs--; _agentReplyDelta -= value; }
        }
        public event Action? AgentReplyDeltaEnd
        {
            add { AgentReplyDeltaEndSubs++; _agentReplyDeltaEnd += value; }
            remove { AgentReplyDeltaEndSubs--; _agentReplyDeltaEnd -= value; }
        }
        public event Action<string>? AgentThinking
        {
            add { AgentThinkingSubs++; _agentThinking += value; }
            remove { AgentThinkingSubs--; _agentThinking -= value; }
        }
        public event Action<string, string>? AgentToolCall
        {
            add { AgentToolCallSubs++; _agentToolCall += value; }
            remove { AgentToolCallSubs--; _agentToolCall -= value; }
        }
        public event Action<string, JsonElement>? EventReceived
        {
            add { _eventReceived += value; }
            remove { _eventReceived -= value; }
        }
        public event Action<string>? AgentReplyAudio
        {
            add { AgentReplyAudioSubs++; _agentReplyAudio += value; }
            remove { AgentReplyAudioSubs--; _agentReplyAudio -= value; }
        }

        // Raise methods — invoke backing fields directly
        public void RaiseAgentReplyFull(string body) => _agentReplyFull?.Invoke(body);
        public void RaiseAgentThinking(string thinking) => _agentThinking?.Invoke(thinking);
        public void RaiseAgentToolCall(string toolName, string arguments) => _agentToolCall?.Invoke(toolName, arguments);
        public void RaiseAgentReplyDeltaStart() => _agentReplyDeltaStart?.Invoke();
        public void RaiseAgentReplyDelta(string delta) => _agentReplyDelta?.Invoke(delta);
        public void RaiseAgentReplyDeltaEnd() => _agentReplyDeltaEnd?.Invoke();
        public void RaiseAgentReplyAudio(string audioText) => _agentReplyAudio?.Invoke(audioText);
    }

    /// <summary>
    /// Test IConsoleOutput that records all calls and provides a real AgentReplyFormatter
    /// for formatter-path tests. Suppresses real console output.
    /// </summary>
    private sealed class MockConsoleOutput : IConsoleOutput
    {
        public readonly List<string> Writes = new();
        public readonly List<string?> WriteLines = new();
        public readonly List<(string prefix, string body)> PrintAgentReplyCalls = new();
        public readonly List<(string prefix, string delta, string suffix)> PrintAgentReplyDeltaCalls = new();
        public readonly List<(string prefix, int rightMargin, bool prefixPrinted)> CreateFormatterCalls = new();

        private ConsoleColor _foregroundColor = ConsoleColor.White;
        public ConsoleColor ForegroundColor
        {
            get => _foregroundColor;
            set => _foregroundColor = value;
        }

        public bool ResetColorCalled;
        public void ResetColor() => ResetColorCalled = true;

        public bool KeyAvailable => false;
        public int WindowWidth => 120;
        public Encoding OutputEncoding { get; set; } = Encoding.UTF8;
        public bool TreatControlCAsInput { get; set; }
        public ConsoleKeyInfo ReadKey(bool intercept = false) => new ConsoleKeyInfo('A', ConsoleKey.A, false, false, false);

        public void Write(string? text) => Writes.Add(text ?? "");
        public void WriteLine(string? text = null) => WriteLines.Add(text);

        public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult<string?>(null);

        public IAgentReplyFormatter CreateAgentReplyFormatter(string prefix, int rightMarginIndent, bool prefixAlreadyPrinted = false)
        {
            CreateFormatterCalls.Add((prefix, rightMarginIndent, prefixAlreadyPrinted));
            return AgentReplyFormatter.CreateSytemConsoleFormatter(prefix, rightMarginIndent, prefixAlreadyPrinted, 120);
        }

        public IAgentReplyFormatter CreateAgentReplyFormatter(string prefix, int rightMarginIndent, bool prefixAlreadyPrinted, int consoleWidth)
        {
            CreateFormatterCalls.Add((prefix, rightMarginIndent, prefixAlreadyPrinted));
            return AgentReplyFormatter.CreateSytemConsoleFormatter(prefix, rightMarginIndent, prefixAlreadyPrinted, consoleWidth);
        }

        // ── IConsoleOutput display methods (no-op for these tests) ──

        public void PrintBanner() { }
        public void PrintHelpMenu(string hotkeyCombination, bool holdToTalk) { }
        public void PrintRecordingIndicator(bool isRecording, string hotkeyCombination, bool holdToTalk) { }
        public void PrintUserMessage(string text) { }
        public void PrintSuccess(string message) { }
        public void PrintSuccessWordWrap(string prefix, string message, int rightMarginIndent) { }
        public void PrintWarning(string message) { }
        public void PrintError(string message) { }
        public void PrintInfo(string message) { }
        public void PrintInlineInfo(string message) { }
        public void PrintInlineSuccess(string message) { }
        public void PrintGatewayError(string message, string? detailCode, string? recommendedStep) { }
        public void PrintAgentReply(string prefix, string body)
            => PrintAgentReplyCalls.Add((prefix, body));
        public void PrintAgentReplyDelta(string prefix, string delta, string newlineSuffix)
            => PrintAgentReplyDeltaCalls.Add((prefix, delta, newlineSuffix));
        public void Log(string tag, string msg) { }
        public void LogOk(string tag, string msg) { }
        public void LogError(string tag, string msg) { }
    }

    #endregion

    private MockConsoleOutput _console = null!;
    private MockGatewayUIEvents _service = null!;

    private void Setup()
    {
        _console = new MockConsoleOutput();
        _service = new MockGatewayUIEvents();
    }

    public void Dispose() { }

    #region Attach / Detach

    [Fact]
    public void AttachToService_SubscribesAllSevenEvents()
    {
        Setup();
        var adapter = new AgentOutputAdapter(new AppConfig(), _console);
        adapter.AttachToService(_service);

        Assert.Equal(1, _service.AgentReplyFullSubs);
        Assert.Equal(1, _service.AgentReplyDeltaStartSubs);
        Assert.Equal(1, _service.AgentReplyDeltaSubs);
        Assert.Equal(1, _service.AgentReplyDeltaEndSubs);
        Assert.Equal(1, _service.AgentThinkingSubs);
        Assert.Equal(1, _service.AgentToolCallSubs);
        Assert.Equal(1, _service.AgentReplyAudioSubs);
    }

    [Fact]
    public void DetachFromService_UnsubscribesAllSevenEvents()
    {
        Setup();
        var adapter = new AgentOutputAdapter(new AppConfig(), _console);
        adapter.AttachToService(_service);
        adapter.DetachFromService(_service);

        Assert.Equal(0, _service.AgentReplyFullSubs);
        Assert.Equal(0, _service.AgentReplyDeltaStartSubs);
        Assert.Equal(0, _service.AgentReplyDeltaSubs);
        Assert.Equal(0, _service.AgentReplyDeltaEndSubs);
        Assert.Equal(0, _service.AgentThinkingSubs);
        Assert.Equal(0, _service.AgentToolCallSubs);
        Assert.Equal(0, _service.AgentReplyAudioSubs);
    }

    [Fact]
    public void DoubleAttachToService_BothSubscriptionsActive()
    {
        Setup();
        var adapter = new AgentOutputAdapter(new AppConfig(), _console);
        adapter.AttachToService(_service);
        adapter.AttachToService(_service);

        Assert.Equal(2, _service.AgentReplyFullSubs);
        Assert.Equal(2, _service.AgentReplyAudioSubs);
    }

    #endregion

    #region OnAgentReplyFull

    [Fact]
    public void OnAgentReplyFull_WordWrapEnabled_CreatesFormatterAndProcesses()
    {
        Setup();
        var cfg = new AppConfig { AgentName = "TestAgent", EnableWordWrap = true, RightMarginIndent = 10 };
        var adapter = new AgentOutputAdapter(cfg, _console);

        adapter.OnAgentReplyFull("Hello world");

        Assert.NotEmpty(_console.CreateFormatterCalls);
        var (prefix, rightMargin, prefixPrinted) = _console.CreateFormatterCalls.Last();
        Assert.Equal("  🤖 TestAgent: ", prefix);
        Assert.Equal(10, rightMargin);
        Assert.True(prefixPrinted);
    }

    [Fact]
    public void OnAgentReplyFull_WordWrapDisabled_CallsPrintAgentReplyDirectly()
    {
        Setup();
        var cfg = new AppConfig { AgentName = "TestAgent", EnableWordWrap = false };
        var adapter = new AgentOutputAdapter(cfg, _console);

        adapter.OnAgentReplyFull("Hello world");

        Assert.NotEmpty(_console.PrintAgentReplyCalls);
        var (prefix, body) = _console.PrintAgentReplyCalls.Last();
        Assert.Contains("TestAgent", prefix);
        Assert.Equal("Hello world", body);
    }

    [Fact]
    public void OnAgentReplyFull_WithPriorDelta_ProcessesExistingFormatter()
    {
        Setup();
        var cfg = new AppConfig { AgentName = "TestAgent", EnableWordWrap = true, RightMarginIndent = 10 };
        var adapter = new AgentOutputAdapter(cfg, _console);

        adapter.OnAgentReplyDeltaStart();
        adapter.OnAgentReplyDelta("partial ");
        adapter.OnAgentReplyDeltaEnd();
        adapter.OnAgentReplyFull("full reply body");

        // Full reply should have created its own formatter
        Assert.True(_console.CreateFormatterCalls.Count >= 2);
    }

    #endregion

    #region OnAgentThinking

    [Fact]
    public void OnAgentThinking_ShowThinkingFalse_DoesNotThrow()
    {
        Setup();
        var cfg = new AppConfig { AgentName = "TestAgent", ShowThinking = false };
        var adapter = new AgentOutputAdapter(cfg, _console);

        // ShowThinking=false path: writes _thinkingInfo directly to real Console.
        // We verify the code runs without throwing.
        adapter.OnAgentThinking("thinking content");
    }

    [Fact]
    public void OnAgentThinking_ShowThinkingTrue_WordWrapEnabled_DoesNotThrow()
    {
        Setup();
        var cfg = new AppConfig { AgentName = "TestAgent", ShowThinking = true, EnableWordWrap = true, RightMarginIndent = 10 };
        var adapter = new AgentOutputAdapter(cfg, _console);

        // ShowThinking=true + EnableWordWrap: _thinkingFormatter is created via
        // AgentReplyFormatter.CreateSytemConsoleFormatter(...) directly (not via _console mock).
        adapter.OnAgentThinking("working through the problem");
    }

    [Fact]
    public void OnAgentThinking_ShowThinkingTrue_WordWrapDisabled_DoesNotCreateFormatter()
    {
        Setup();
        var cfg = new AppConfig { AgentName = "TestAgent", ShowThinking = true, EnableWordWrap = false };
        var adapter = new AgentOutputAdapter(cfg, _console);

        adapter.OnAgentThinking("thinking content");

        // No formatter created (direct Console.Write path via _thinkingFormatter == null)
        Assert.Empty(_console.CreateFormatterCalls);
    }

    #endregion

    #region OnAgentReplyDelta

    [Fact]
    public void OnAgentReplyDelta_BeforeDeltaStart_IsIgnored()
    {
        Setup();
        var cfg = new AppConfig { AgentName = "TestAgent", EnableWordWrap = true };
        var adapter = new AgentOutputAdapter(cfg, _console);

        adapter.OnAgentReplyDelta("some text");

        Assert.Empty(_console.CreateFormatterCalls);
        Assert.Empty(_console.PrintAgentReplyDeltaCalls);
    }

    [Fact]
    public void OnAgentReplyDelta_AfterDeltaStart_WordWrapEnabled_CreatesFormatterAndProcesses()
    {
        Setup();
        var cfg = new AppConfig { AgentName = "TestAgent", EnableWordWrap = true, RightMarginIndent = 10 };
        var adapter = new AgentOutputAdapter(cfg, _console);

        adapter.OnAgentReplyDeltaStart();
        adapter.OnAgentReplyDelta("streaming chunk");

        Assert.NotEmpty(_console.CreateFormatterCalls);
    }

    [Fact]
    public void OnAgentReplyDelta_AfterDeltaStart_WordWrapDisabled_CallsPrintAgentReplyDelta()
    {
        Setup();
        var cfg = new AppConfig { AgentName = "TestAgent", EnableWordWrap = false };
        var adapter = new AgentOutputAdapter(cfg, _console);

        adapter.OnAgentReplyDeltaStart();
        adapter.OnAgentReplyDelta("streaming chunk");

        Assert.NotEmpty(_console.PrintAgentReplyDeltaCalls);
    }

    [Fact]
    public void OnAgentReplyDelta_MultipleChunks_OneFormatterCreated()
    {
        Setup();
        var cfg = new AppConfig { AgentName = "TestAgent", EnableWordWrap = true, RightMarginIndent = 10 };
        var adapter = new AgentOutputAdapter(cfg, _console);

        adapter.OnAgentReplyDeltaStart();
        adapter.OnAgentReplyDelta("chunk1 ");
        adapter.OnAgentReplyDelta("chunk2");
        adapter.OnAgentReplyDeltaEnd();

        Assert.Single(_console.CreateFormatterCalls);
    }

    #endregion

    #region OnAgentReplyDeltaEnd

    [Fact]
    public void OnAgentReplyDeltaEnd_WithActiveFormatter_CallsFinish()
    {
        Setup();
        var cfg = new AppConfig { AgentName = "TestAgent", EnableWordWrap = true, RightMarginIndent = 10 };
        var adapter = new AgentOutputAdapter(cfg, _console);

        adapter.OnAgentReplyDeltaStart();
        adapter.OnAgentReplyDelta("content");
        adapter.OnAgentReplyDeltaEnd();

        Assert.NotEmpty(_console.CreateFormatterCalls);
    }

    [Fact]
    public void OnAgentReplyDeltaEnd_BeforeDeltaStart_IsIgnored()
    {
        Setup();
        var cfg = new AppConfig { AgentName = "TestAgent", EnableWordWrap = true };
        var adapter = new AgentOutputAdapter(cfg, _console);

        adapter.OnAgentReplyDeltaEnd();

        Assert.Empty(_console.CreateFormatterCalls);
    }

    #endregion

    #region OnAgentReplyAudio

    [Fact]
    public void OnAgentReplyAudio_TextOnlyConfig_AudioHandlerIsNull()
    {
        Setup();
        var cfg = new AppConfig { AgentName = "TestAgent", AudioResponseMode = "text-only" };
        var adapter = new AgentOutputAdapter(cfg, _console);

        Assert.Null(adapter.AudioResponseHandler);
    }

    [Fact]
    public void OnAgentReplyAudio_TextOnlyConfig_DoesNotThrow()
    {
        Setup();
        var cfg = new AppConfig { AgentName = "TestAgent", AudioResponseMode = "text-only" };
        var adapter = new AgentOutputAdapter(cfg, _console);

        adapter.OnAgentReplyAudio("some audio text");
    }

    [Fact]
    public void OnAgentReplyAudio_AudioEnabled_AudioResponseHandlerIsCreated()
    {
        Setup();
        var cfg = new AppConfig { AgentName = "TestAgent", AudioResponseMode = "audio-only" };
        var adapter = new AgentOutputAdapter(cfg, _console);

        Assert.NotNull(adapter.AudioResponseHandler);
    }

    [Fact]
    public void OnAgentReplyAudio_AudioModeBoth_AudioResponseHandlerIsCreated()
    {
        Setup();
        var cfg = new AppConfig { AgentName = "TestAgent", AudioResponseMode = "both" };
        var adapter = new AgentOutputAdapter(cfg, _console);

        Assert.NotNull(adapter.AudioResponseHandler);
    }

    #endregion

    #region Dispose

    [Fact]
    public void Dispose_WithoutDetach_StillDisposesAudioResponseHandler()
    {
        Setup();
        var cfg = new AppConfig { AgentName = "TestAgent", AudioResponseMode = "audio-only" };
        var adapter = new AgentOutputAdapter(cfg, _console);

        adapter.AttachToService(_service);
        adapter.Dispose();
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        Setup();
        var cfg = new AppConfig { AgentName = "TestAgent", AudioResponseMode = "audio-only" };
        var adapter = new AgentOutputAdapter(cfg, _console);
        adapter.AttachToService(_service);

        adapter.Dispose();
        adapter.Dispose();
    }

    [Fact]
    public void Dispose_TextOnlyConfig_DoesNotThrow()
    {
        Setup();
        var cfg = new AppConfig { AgentName = "TestAgent", AudioResponseMode = "text-only" };
        var adapter = new AgentOutputAdapter(cfg, _console);

        adapter.Dispose();
    }

    #endregion

    #region State Machine

    [Fact]
    public void StateMachine_DeltaStart_ResetsForNewDeltaSequence()
    {
        Setup();
        var cfg = new AppConfig { AgentName = "TestAgent", EnableWordWrap = true, RightMarginIndent = 10 };
        var adapter = new AgentOutputAdapter(cfg, _console);

        // First delta sequence
        adapter.OnAgentReplyDeltaStart();
        adapter.OnAgentReplyDelta("first chunk");
        adapter.OnAgentReplyDeltaEnd();

        var afterFirst = _console.CreateFormatterCalls.Count;

        // Second delta sequence — should get fresh formatter
        adapter.OnAgentReplyDeltaStart();
        adapter.OnAgentReplyDelta("second chunk");
        adapter.OnAgentReplyDeltaEnd();

        Assert.True(_console.CreateFormatterCalls.Count > afterFirst);
    }

    [Fact]
    public void StateMachine_DeltaEnd_ResetsPrefixPrintedFlag()
    {
        Setup();
        var cfg = new AppConfig { AgentName = "TestAgent", EnableWordWrap = true };
        var adapter = new AgentOutputAdapter(cfg, _console);

        adapter.OnAgentReplyDeltaStart();
        adapter.OnAgentReplyDelta("text");
        adapter.OnAgentReplyDeltaEnd();
        adapter.OnAgentReplyFull("next reply");

        Assert.NotEmpty(_console.CreateFormatterCalls);
    }

    #endregion

    #region End-to-End Sequences

    [Fact]
    public void Sequence_AudioMarker_Delta_DeltaEnd_FullReply_Works()
    {
        Setup();
        var cfg = new AppConfig
        {
            AgentName = "TestAgent",
            EnableWordWrap = true,
            RightMarginIndent = 10,
            AudioResponseMode = "text-only"
        };
        var adapter = new AgentOutputAdapter(cfg, _console);

        adapter.OnAgentReplyAudio("audio text");
        adapter.OnAgentReplyDeltaStart();
        adapter.OnAgentReplyDelta("chunk1");
        adapter.OnAgentReplyDelta("chunk2");
        adapter.OnAgentReplyDeltaEnd();
        adapter.OnAgentReplyFull("final body");

        Assert.NotEmpty(_console.CreateFormatterCalls);
    }

    #endregion
}
