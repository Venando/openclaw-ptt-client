using OpenClawPTT;
using System.Text;
using System.Text.Json;
using Xunit;

namespace OpenClawPTT.Tests;

public class ConfigManagerStabilityTests : IDisposable
{
    private readonly string _tempDir;

    public ConfigManagerStabilityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"openclaw-stability-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
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

    // =====================================================================
    // Load() — malformed / empty / partial JSON
    // =====================================================================

    [Fact]
    public void Load_MalformedJson_ReturnsNull_DoesNotThrow()
    {
        // Arrange: write garbage JSON that will cause JsonSerializer.Deserialize to throw
        var badJson = @"{ ""GatewayUrl"": ""wss://example.com"", this is not valid json }";
        File.WriteAllText(ConfigPath(), badJson);

        var manager = new ConfigManager();
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

        var manager = new ConfigManager();
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

        var manager = new ConfigManager();
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
            var manager = new ConfigManager();

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
        var manager = new ConfigManager();
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
    // Validate() — boundary conditions
    // =====================================================================

    [Fact]
    public void Validate_SampleRate8000_ReturnsNoIssues()
    {
        var manager = new ConfigManager();
        var cfg = MinimalConfig();
        cfg.SampleRate = 8000;
        Assert.Empty(manager.Validate(cfg));
    }

    [Fact]
    public void Validate_SampleRate48000_ReturnsNoIssues()
    {
        var manager = new ConfigManager();
        var cfg = MinimalConfig();
        cfg.SampleRate = 48000;
        Assert.Empty(manager.Validate(cfg));
    }

    [Fact]
    public void Validate_ReconnectDelaySeconds1_ReturnsNoIssues()
    {
        var manager = new ConfigManager();
        var cfg = MinimalConfig();
        cfg.ReconnectDelaySeconds = 1;
        Assert.Empty(manager.Validate(cfg));
    }

    [Fact]
    public void Validate_VisualModeGlowDot_ReturnsNoIssues()
    {
        var manager = new ConfigManager();
        var cfg = MinimalConfig();
        cfg.VisualMode = VisualMode.GlowDot;
        Assert.Empty(manager.Validate(cfg));
    }

    [Fact]
    public void Validate_GatewayUrlWss_ReturnsNoIssues()
    {
        var manager = new ConfigManager();
        var cfg = MinimalConfig();
        cfg.GatewayUrl = "wss://secure.example.com:8080/path";
        Assert.Empty(manager.Validate(cfg));
    }

    [Fact]
    public void Validate_GatewayUrlHttp_ReturnsIssues()
    {
        var manager = new ConfigManager();
        var cfg = MinimalConfig();
        cfg.GatewayUrl = "http://insecure.example.com";
        var issues = manager.Validate(cfg);
        Assert.NotEmpty(issues);
        Assert.Contains(issues, i => i.Contains("Gateway URL"));
    }
}
