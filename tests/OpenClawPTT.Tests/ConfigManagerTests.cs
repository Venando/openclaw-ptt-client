using System.Text;
using System.Text.Json;
using Xunit;

namespace OpenClawPTT.Tests;

public class ConfigManagerTests : IDisposable
{
    private readonly ConfigManager _manager;
    private readonly string _tempDir;
    private readonly RecordingConsole _console;

    public ConfigManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"openclaw-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
        _console = new RecordingConsole();
        _manager = new ConfigManager(_console);
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

    // === Existing Validate tests ===

    [Fact]
    public void Validate_MissingGatewayUrl_ReturnsIssues()
    {
        var cfg = new AppConfig { GatewayUrl = "", AuthToken = "some-token" };
        var issues = _manager.Validate(cfg);
        Assert.Contains(issues, i => i.Contains("Gateway URL"));
    }

    [Fact]
    public void Validate_ValidConfig_ReturnsNoIssues()
    {
        var cfg = MinimalConfig();
        Assert.Empty(_manager.Validate(cfg));
    }

    [Fact]
    public void Validate_InvalidSampleRate_ReturnsIssues()
    {
        var cfg = MinimalConfig();
        cfg.SampleRate = 96000;
        Assert.Contains(_manager.Validate(cfg), i => i.Contains("Sample rate"));
    }

    [Fact]
    public void Validate_NonPositiveReconnectDelay_ReturnsIssues()
    {
        var cfg = MinimalConfig();
        cfg.ReconnectDelaySeconds = 0;
        Assert.Contains(_manager.Validate(cfg), i => i.Contains("Reconnect delay"));
    }

    [Fact]
    public void Validate_InvalidGatewayUrlScheme_ReturnsIssues()
    {
        var cfg = MinimalConfig();
        cfg.GatewayUrl = "http://not-ws.example.com";
        Assert.Contains(_manager.Validate(cfg), i => i.Contains("Gateway URL"));
    }

    [Fact]
    public void Validate_MissingAuthAndDeviceToken_ReturnsIssues()
    {
        var cfg = MinimalConfig();
        cfg.AuthToken = "";
        cfg.DeviceToken = "";
        Assert.Contains(_manager.Validate(cfg), i => i.Contains("Auth token"));
    }

    // === Save tests ===

    [Fact]
    public void Save_CreatesConfigFile()
    {
        var cfg = MinimalConfig();
        cfg.CustomDataDir = _tempDir;

        _manager.Save(cfg);

        Assert.True(File.Exists(ConfigPath()));
    }

    [Fact]
    public void Save_SerializesValidJson()
    {
        var cfg = MinimalConfig();
        cfg.CustomDataDir = _tempDir;

        _manager.Save(cfg);

        var json = File.ReadAllText(ConfigPath());
        var deserialized = JsonSerializer.Deserialize<AppConfig>(json);
        Assert.NotNull(deserialized);
        Assert.Equal(cfg.GatewayUrl, deserialized.GatewayUrl);
        Assert.Equal(cfg.AuthToken, deserialized.AuthToken);
    }

    // === Load tests ===

    [Fact]
    public void Load_NoConfigFile_ReturnsNull()
    {
        var probe = new AppConfig { CustomDataDir = _tempDir };
        var manager = new ConfigManager(new RecordingConsole());
        Assert.False(File.Exists(Path.Combine(_tempDir, "config.json")));

        var result = manager.Load(probe);

        Assert.Null(result);
    }

    [Fact]
    public void Load_ValidConfigFile_ReturnsConfig()
    {
        var cfg = MinimalConfig();
        cfg.CustomDataDir = _tempDir;
        _manager.Save(cfg);

        var loaded = _manager.Load(cfg);

        Assert.NotNull(loaded);
        Assert.Equal(cfg.GatewayUrl, loaded!.GatewayUrl);
    }

    [Fact]
    public void Load_BackwardCompat_VisualModeNone_DisablesVisualFeedback()
    {
        var json = @"{
  ""GatewayUrl"": ""wss://example.com"",
  ""AuthToken"": ""token"",
  ""VisualMode"": 0
}";
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(ConfigPath(), json);

        var loaded = _manager.Load(new AppConfig { CustomDataDir = _tempDir });

        Assert.NotNull(loaded);
        Assert.False(loaded!.VisualFeedbackEnabled);
        Assert.Equal(VisualMode.SolidDot, loaded.VisualMode);
    }

    [Fact]
    public void Load_MissingVisualMode_DisablesVisualFeedback()
    {
        var json = @"{
  ""GatewayUrl"": ""wss://example.com"",
  ""AuthToken"": ""token""
}";
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(ConfigPath(), json);

        var loaded = _manager.Load(new AppConfig { CustomDataDir = _tempDir });

        Assert.NotNull(loaded);
        Assert.False(loaded!.VisualFeedbackEnabled);
    }

    [Fact]
    public void Load_EspeakNgPath_Windows_SetsDefault()
    {
        var cfg = MinimalConfig();
        cfg.CustomDataDir = _tempDir;
        _manager.Save(cfg);

        var loaded = _manager.Load(cfg);

        Assert.NotNull(loaded);
        Assert.Equal(@"C:\Program Files\eSpeak NG", loaded!.EspeakNgPath);
    }

    // === RunSetup tests ===

    private sealed class MockConsoleForSetup : IConsole
    {
        private readonly Queue<string?> _inputs;
        public readonly List<string?> WriteLines = new();
        public readonly List<string?> Writes = new();

        public MockConsoleForSetup(Queue<string?> inputs) => _inputs = inputs;

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
        public void WriteLine(string? text = null) => WriteLines.Add(text);
        public void ResetColor() { }
        public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default)
        {
            if (_inputs.Count == 0)
                throw new InvalidOperationException("Test ran out of input values for ReadLineAsync");
            var next = _inputs.Dequeue();
            return ValueTask.FromResult(next);
        }
    }

    [Fact]
    public async Task RunSetup_WithExistingConfig_NewValuesAccepted_UpdatesConfig()
    {
        // Test that provided inputs override the existing config values.
        // Uses existing config so defaults are pre-populated (no validation loops).
        var existing = MinimalConfig();
        existing.CustomDataDir = _tempDir;

        var inputs = new Queue<string?>(new[]
        {
            "wss://new.example.com",  // GatewayUrl (override)
            "new-token",              // AuthToken (override)
            "",                       // TlsFingerprint (blank = skip, since wss://)
            "gsk_newkey",             // GroqApiKey (override - starts with gsk_)
            "en-GB",                  // Locale (override)
            "48000",                  // SampleRate (override)
            "300",                    // MaxRecordSeconds (override)
            "true",                   // RealTimeReplyOutput
            "MyAgent",                // AgentName
            "Ctrl+Shift+Space",       // HotkeyCombination
            "true",                   // HoldToTalk
            "prefix:",                // TranscriptionPromptPrefix
            "true",                   // VisualFeedbackEnabled
            "BottomLeft",             // VisualFeedbackPosition
            "30",                     // VisualFeedbackSize
            "0.8",                    // VisualFeedbackOpacity
            "#00FF00",                // VisualFeedbackColor
            "10",                     // VisualFeedbackRimThickness
            "audio-only",             // AudioResponseMode
            "",                       // TtsApiKey (blank = skip)
            ""                        // TtsVoiceId (blank = skip)
        });
        var mockConsole = new MockConsoleForSetup(inputs);
        var manager = new ConfigManager(mockConsole);

        var result = await manager.RunSetup(existing);

        Assert.Equal("wss://new.example.com", result.GatewayUrl);
        Assert.Equal("gsk_newkey", result.GroqApiKey);
        Assert.Equal("en-GB", result.Locale);
        Assert.Equal(48000, result.SampleRate);
        Assert.Equal(300, result.MaxRecordSeconds);
        Assert.True(result.RealTimeReplyOutput);
        Assert.Equal("MyAgent", result.AgentName);
        Assert.Equal("Ctrl+Shift+Space", result.HotkeyCombination);
        Assert.True(result.HoldToTalk);
        Assert.Equal("audio-only", result.AudioResponseMode);
        Assert.True(result.VisualFeedbackEnabled);
        Assert.Equal("BottomLeft", result.VisualFeedbackPosition);
        Assert.Equal(30, result.VisualFeedbackSize);
        Assert.Equal(0.8, result.VisualFeedbackOpacity);
        Assert.Equal("#00FF00", result.VisualFeedbackColor);
        Assert.Equal(10, result.VisualFeedbackRimThickness);
    }

    [Fact]
    public async Task RunSetup_WithNullConfig_AllInputsProvided()
    {
        // Test RunSetup(null) when ALL input values are explicitly provided.
        // GroqApiKey must start with "gsk_" since no default is pre-populated.
        var inputs = new Queue<string?>(new[]
        {
            "wss://example.com",       // GatewayUrl
            "token",                   // AuthToken
            "",                        // TlsFingerprint (blank = skip)
            "gsk_testkey",             // GroqApiKey (must start with gsk_)
            "en-US",                   // Locale
            "44100",                   // SampleRate
            "60",                      // MaxRecordSeconds
            "false",                   // RealTimeReplyOutput
            "TestAgent",               // AgentName
            "Alt+=",                   // HotkeyCombination
            "false",                   // HoldToTalk
            "transcribe:",             // TranscriptionPromptPrefix
            "false",                   // VisualFeedbackEnabled
            "TopRight",                // VisualFeedbackPosition
            "25",                      // VisualFeedbackSize
            "0.9",                     // VisualFeedbackOpacity
            "#0000FF",                 // VisualFeedbackColor
            "5",                       // VisualFeedbackRimThickness
            "both",                    // AudioResponseMode
            "",                        // TtsApiKey (blank = skip)
            "voice123"                 // TtsVoiceId
        });
        var mockConsole = new MockConsoleForSetup(inputs);
        var manager = new ConfigManager(mockConsole);

        var result = await manager.RunSetup(null);

        Assert.Equal("wss://example.com", result.GatewayUrl);
        Assert.Equal("gsk_testkey", result.GroqApiKey);
        Assert.Equal("TestAgent", result.AgentName);
        Assert.Equal(44100, result.SampleRate);
        Assert.Equal("both", result.AudioResponseMode);
    }

    [Fact]
    public async Task RunSetup_BlankAuthToken_RemainsUnchanged()
    {
        // Uses existing config so defaults are populated.
        var existing = MinimalConfig();
        existing.CustomDataDir = _tempDir;

        var inputs = new Queue<string?>(new[]
        {
            "",                         // GatewayUrl (blank = use existing)
            "",                         // AuthToken (blank = use existing)
            "",                         // TlsFingerprint (blank = skip)
            "gsk_blanktest",            // GroqApiKey (must start with gsk_)
            "en",                       // Locale
            "16000",                    // SampleRate
            "120",                      // MaxRecordSeconds
            "false",                    // RealTimeReplyOutput
            "Agent",                    // AgentName
            "Alt+=",                    // HotkeyCombination
            "false",                    // HoldToTalk
            "transcribe:",              // TranscriptionPromptPrefix
            "false",                    // VisualFeedbackEnabled
            "TopLeft",                  // VisualFeedbackPosition
            "20",                       // VisualFeedbackSize
            "1.0",                      // VisualFeedbackOpacity
            "#FF0000",                  // VisualFeedbackColor
            "8",                        // VisualFeedbackRimThickness
            "text-only",                // AudioResponseMode
            "",                         // TtsApiKey (blank = skip)
            ""                          // TtsVoiceId (blank = skip)
        });
        var mockConsole = new MockConsoleForSetup(inputs);
        var manager = new ConfigManager(mockConsole);

        var result = await manager.RunSetup(existing);

        Assert.Equal(result.AuthToken, existing.AuthToken); 
        Assert.Equal("gsk_blanktest", result.GroqApiKey);
    }

    [Fact]
    public async Task RunSetup_InvalidInput_RetriesUntilValid()
    {
        // Uses existing config so defaults are populated.
        var existing = MinimalConfig();
        existing.CustomDataDir = _tempDir;

        var inputs = new Queue<string?>(new[]
        {
            "http://invalid",           // GatewayUrl: first invalid (not ws/wss) - retries
            "wss://good.example.com",  // GatewayUrl: third valid
            "token",                    // AuthToken
            "",                         // TlsFingerprint (blank = skip)
            "gsk_retry",                // GroqApiKey
            "en",                       // Locale
            "16000",                    // SampleRate
            "120",                      // MaxRecordSeconds
            "false",                    // RealTimeReplyOutput
            "Agent",                    // AgentName
            "Alt+=",                    // HotkeyCombination
            "false",                    // HoldToTalk
            "transcribe:",              // TranscriptionPromptPrefix
            "false",                    // VisualFeedbackEnabled
            "TopLeft",                  // VisualFeedbackPosition
            "20",                       // VisualFeedbackSize
            "1.0",                      // VisualFeedbackOpacity
            "#FF0000",                  // VisualFeedbackColor
            "8",                        // VisualFeedbackRimThickness
            "text-only",                // AudioResponseMode
            "",                         // TtsApiKey (blank = skip)
            ""                          // TtsVoiceId (blank = skip)
        });
        var mockConsole = new MockConsoleForSetup(inputs);
        var manager = new ConfigManager(mockConsole);

        var result = await manager.RunSetup(existing);

        Assert.Equal("wss://good.example.com", result.GatewayUrl);
        // Should have written "Invalid value" at least once for the retry
        Assert.Contains(mockConsole.WriteLines, l => l != null && l.Contains("Invalid"));
    }
}
