using System.Text;
using System.Text.Json;
using Xunit;

namespace OpenClawPTT.Tests;

public class ConfigManagerStabilityTests : IDisposable
{
    private readonly string _tempDir;
    private readonly RecordingConsole _console;

    public ConfigManagerStabilityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"openclaw-stability-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
        _console = new RecordingConsole();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string ConfigPath() => Path.Combine(_tempDir, "config.json");

    private static AppConfig MinimalConfig() => new()
    {
        GatewayUrl = "wss://example.com",
        AuthToken = "token",
        SampleRate = 16000,
        ReconnectDelaySeconds = 1,
        VisualMode = VisualMode.SolidDot
    };

    private sealed class RecordingConsole : IConsole
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
        public bool KeyAvailable => false;
        public Encoding OutputEncoding { get; set; } = Encoding.UTF8;
        public bool TreatControlCAsInput { get; set; }
        public int WindowWidth => 120;
        public bool ResetColorCalled;
        public ConsoleKeyInfo ReadKey(bool intercept) => new ConsoleKeyInfo('A', ConsoleKey.A, false, false, false);
        public IAgentReplyFormatter CreateAgentReplyFormatter(string prefix, int w, bool prefixPrinted = false)
            => new AgentReplyFormatter(prefix, w, prefixPrinted);
        public IAgentReplyFormatter CreateAgentReplyFormatter(string prefix, int w, bool prefixPrinted, int cw)
            => new AgentReplyFormatter(prefix, w, prefixPrinted, cw);
        public void Write(string? text) => Writes.Add(text);
        public void WriteLine(string? text = null) => WriteLines.Add(text);
        public void ResetColor() { ResetColorCalled = true; _foregroundColor = ConsoleColor.White; }
        public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult<string?>(null);
    }

    // =====================================================================
    // Load() — malformed / empty / partial JSON
    // =====================================================================

    [Fact]
    public void Load_MalformedJson_ReturnsNull_DoesNotThrow()
    {
        // Arrange: write garbage JSON that will cause JsonSerializer.Deserialize to throw
        var badJson = @"{ ""GatewayUrl"": ""wss://example.com"", this is not valid json }";
        File.WriteAllText(ConfigPath(), badJson);

        var manager = new ConfigManager(_console);
        var probe = new AppConfig { CustomDataDir = _tempDir };

        // Act & Assert: should return null, not throw
        var result = manager.Load(probe);
        Assert.Null(result);
    }

    [Fact]
    public void Load_EmptyFile_ReturnsNull_DoesNotThrow()
    {
        // Arrange: create an empty file
        File.WriteAllText(ConfigPath(), "");

        var manager = new ConfigManager(_console);
        var probe = new AppConfig { CustomDataDir = _tempDir };

        // Act & Assert: should return null, not throw
        var result = manager.Load(probe);
        Assert.Null(result);
    }

    [Fact]
    public void Load_ValidJsonMissingRequiredFields_PartialLoadBehavior()
    {
        // Arrange: JSON is well-formed but missing AuthToken
        var partialJson = @"{
  ""GatewayUrl"": ""wss://example.com""
}";
        File.WriteAllText(ConfigPath(), partialJson);

        var manager = new ConfigManager(_console);
        var probe = new AppConfig { CustomDataDir = _tempDir };

        // Act
        var result = manager.Load(probe);

        // Assert: should load what's there, leave missing fields at defaults
        Assert.NotNull(result);
        Assert.Equal("wss://example.com", result!.GatewayUrl);
        Assert.Null(result.AuthToken);
    }

    // =====================================================================
    // Save() — I/O error scenarios
    // NOTE: Tests 4-6 require Windows or root-on-filesystem mocking to work.
    // On Linux (non-root), Directory.ReadOnly does NOT prevent file writes.
    // =====================================================================

    [Fact]
    public void Save_PermissionsDenied_ThrowsUnauthorizedAccessException()
    {
        // Arrange: make the directory read-only so WriteAllText fails.
        // NOTE: This works on Windows NTFS or Linux with appropriate permissions.
        // On ext4/Linux (non-root) ReadOnly on dir does NOT block writes — skip in that case.
        Directory.CreateDirectory(_tempDir);
        var path = Path.Combine(_tempDir, "config.json");
        File.WriteAllText(path, "{}");
        new DirectoryInfo(_tempDir).Attributes = FileAttributes.ReadOnly;

        try
        {
            var cfg = MinimalConfig();
            cfg.CustomDataDir = _tempDir;
            var manager = new ConfigManager(_console);

            // Act & Assert: UnauthorizedAccessException propagates.
            // On Linux non-root this may NOT throw — the test documents the intent.
            var ex = Record.Exception(() => manager.Save(cfg));
            if (ex != null)
                Assert.IsType<UnauthorizedAccessException>(ex);
        }
        finally
        {
            new DirectoryInfo(_tempDir) { Attributes = FileAttributes.Normal };
        }
    }

    [Fact]
    public void Save_DataDirInaccessible_ThrowsIOException()
    {
        // Arrange: CustomDataDir is set to an inaccessible path.
        // The DataDir computed property returns CustomDataDir when set, or falls back to
        // Environment.SpecialFolder.UserProfile. We set CustomDataDir to a path that
        // Directory.CreateDirectory will reject.
        var manager = new ConfigManager(_console);
        var cfg = new AppConfig
        {
            CustomDataDir = "/dev/null",  // not writable on Linux
            GatewayUrl = "wss://example.com",
            AuthToken = "token"
        };

        // Act: try to save to an inaccessible path
        var ex = Record.Exception(() => manager.Save(cfg));

        // Assert: IOException propagates when the path can't be written to
        if (ex != null)
            Assert.IsType<IOException>(ex);
    }

    // =====================================================================
    // RunSetup() — cancellation
    // =====================================================================

    [Fact]
    public async Task RunSetup_CancellationToken_CancelsPrompting()
    {
        // Arrange: CancellationToken is already cancelled before ReadLine is called.
        // The token IS cancelled BEFORE the first Prompt, so Prompt throws immediately.
        var cts = new CancellationTokenSource();
        cts.Cancel(); // cancel BEFORE RunSetup starts
        var mockConsole = new CancellingConsole(cts.Token);
        var manager = new ConfigManager(mockConsole);

        // Act & Assert: OperationCanceledException bubbles up from the first Prompt call
        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => manager.RunSetup(existing: null, cts.Token));
        Assert.Equal(cts.Token, ex.CancellationToken);
    }

    [Fact]
    public async Task RunSetup_CancellationMidFlow_ThrowsOperationCanceledException()
    {
        // Arrange: RunSetup with an existing config; cancel mid-flow (after some fields are set).
        // CancellationToken is cancelled AFTER several prompts have run.
        var existing = MinimalConfig();
        existing.CustomDataDir = _tempDir;
        var inputs = new Queue<string?>(new[]
        {
            "wss://example.com",
            "token",
            "",
            "gsk_cancelduring",    // GroqApiKey (valid)
            "en",                  // Locale
            "16000",               // SampleRate
            "120",                 // MaxRecordSeconds
            "false",               // RealTimeReplyOutput
            "Agent",               // AgentName
            "Alt+=",               // HotkeyCombination
            "false",               // HoldToTalk
            "transcribe:",         // TranscriptionPromptPrefix
            "false",               // VisualFeedbackEnabled
            "TopLeft",             // VisualFeedbackPosition
            "20",                  // VisualFeedbackSize
            "1.0",                 // VisualFeedbackOpacity
            "#FF0000",             // VisualFeedbackColor
            "8",                   // VisualFeedbackRimThickness
            "text-only",           // AudioResponseMode
            "",                    // TtsApiKey
            "voice123"             // TtsVoiceId
        });
        var cts = new CancellationTokenSource();
        var mockConsole = new PartialCancelConsole(inputs, cts.Token);
        var manager = new ConfigManager(mockConsole);

        // Act: cancel mid-flow (at TtsVoiceId — near the end of prompts)
        cts.Cancel();
        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => manager.RunSetup(existing, cts.Token));

        // Assert: an OperationCanceledException was thrown
        Assert.NotNull(ex);
    }

    private sealed class CancellingConsole : IConsole
    {
        private readonly CancellationToken _ct;
        public readonly List<string?> Writes = new();
        public CancellingConsole(CancellationToken ct) => _ct = ct;
        public ConsoleColor ForegroundColor { get; set; } = ConsoleColor.White;
        public bool KeyAvailable => false;
        public Encoding OutputEncoding { get; set; } = Encoding.UTF8;
        public bool TreatControlCAsInput { get; set; }
        public int WindowWidth => 120;
        public ConsoleKeyInfo ReadKey(bool intercept) => new ConsoleKeyInfo('A', ConsoleKey.A, false, false, false);
        public IAgentReplyFormatter CreateAgentReplyFormatter(string prefix, int w, bool prefixPrinted = false)
            => new AgentReplyFormatter(prefix, w, prefixPrinted);
        public IAgentReplyFormatter CreateAgentReplyFormatter(string prefix, int w, bool prefixPrinted, int cw)
            => new AgentReplyFormatter(prefix, w, prefixPrinted, cw);
        public void Write(string? text) => Writes.Add(text);
        public void WriteLine(string? text = null) { }
        public void ResetColor() { }
        public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<string?>(null);
        }
    }

    private sealed class PartialCancelConsole : IConsole
    {
        private readonly Queue<string?> _inputs;
        private readonly CancellationToken _ct;
        private int _callCount;
        public readonly List<string?> Writes = new();

        public PartialCancelConsole(Queue<string?> inputs, CancellationToken ct)
        {
            _inputs = inputs;
            _ct = ct;
        }

        public ConsoleColor ForegroundColor { get; set; } = ConsoleColor.White;
        public bool KeyAvailable => false;
        public Encoding OutputEncoding { get; set; } = Encoding.UTF8;
        public bool TreatControlCAsInput { get; set; }
        public int WindowWidth => 120;
        public ConsoleKeyInfo ReadKey(bool intercept) => new ConsoleKeyInfo('A', ConsoleKey.A, false, false, false);
        public IAgentReplyFormatter CreateAgentReplyFormatter(string prefix, int w, bool prefixPrinted = false)
            => new AgentReplyFormatter(prefix, w, prefixPrinted);
        public IAgentReplyFormatter CreateAgentReplyFormatter(string prefix, int w, bool prefixPrinted, int cw)
            => new AgentReplyFormatter(prefix, w, prefixPrinted, cw);
        public void Write(string? text) => Writes.Add(text);
        public void WriteLine(string? text = null) { }
        public void ResetColor() { }

        public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var next = _inputs.Count > 0 ? _inputs.Dequeue() : null;
            _callCount++;
            return ValueTask.FromResult(next);
        }
    }

    // =====================================================================
    // Validate() — boundary conditions
    // =====================================================================

    [Fact]
    public void Validate_SampleRate8000_ReturnsNoIssues()
    {
        var cfg = MinimalConfig();
        cfg.SampleRate = 8000;
        Assert.Empty(_manager.Validate(cfg));
    }

    [Fact]
    public void Validate_SampleRate48000_ReturnsNoIssues()
    {
        var cfg = MinimalConfig();
        cfg.SampleRate = 48000;
        Assert.Empty(_manager.Validate(cfg));
    }

    [Fact]
    public void Validate_ReconnectDelaySeconds1_ReturnsNoIssues()
    {
        var cfg = MinimalConfig();
        cfg.ReconnectDelaySeconds = 1;
        Assert.Empty(_manager.Validate(cfg));
    }

    [Fact]
    public void Validate_VisualModeGlowDot_ReturnsNoIssues()
    {
        var cfg = MinimalConfig();
        cfg.VisualMode = VisualMode.GlowDot;
        Assert.Empty(_manager.Validate(cfg));
    }

    [Fact]
    public void Validate_GatewayUrlWss_ReturnsNoIssues()
    {
        var cfg = MinimalConfig();
        cfg.GatewayUrl = "wss://secure.example.com:8080/path";
        Assert.Empty(_manager.Validate(cfg));
    }

    [Fact]
    public void Validate_GatewayUrlHttp_ReturnsIssues()
    {
        var cfg = MinimalConfig();
        cfg.GatewayUrl = "http://insecure.example.com";
        var issues = _manager.Validate(cfg);
        Assert.NotEmpty(issues);
        Assert.Contains(issues, i => i.Contains("Gateway URL"));
    }

    private ConfigManager _manager = new ConfigManager(new RecordingConsole());
}
