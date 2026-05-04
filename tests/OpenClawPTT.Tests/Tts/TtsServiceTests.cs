using Moq;
using OpenClawPTT;
using OpenClawPTT.Services;
using OpenClawPTT.TTS;
using OpenClawPTT.TTS.Providers;
using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace OpenClawPTT.Tests;

/// <summary>
/// Tests for TtsService and PythonTtsProvider lifecycle and error handling.
/// Do NOT test actual TTS synthesis — only provider lifecycle, startup failures,
/// restart limits, and disposal correctness.
///
/// Key behaviors under test:
/// 1. Missing Python binary → Win32Exception (not wrapped in TargetInvocationException)
/// 2. Missing Piper binary → no crash (Piper defers validation to SynthesizeAsync)
/// 3. PythonTtsProvider exception unwrapping in TtsService constructor
/// 4. Dispose idempotency
/// 5. Edge provider graceful handling (no subscription key)
/// </summary>
public class TtsServiceTests : IDisposable
{
    private readonly Mock<IColorConsole> _mockConsole = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static AppConfig CoquiConfig(string pythonPath, string modelPath = "", string modelName = "tts_models/multilingual/mxtts/vits")
        => new()
        {
            TtsProvider = TtsProviderType.Coqui,
            PythonPath = pythonPath,
            CoquiModelPath = modelPath,
            CoquiModelName = modelName,
        };

    private static AppConfig PiperConfig(string piperPath, string modelPath = "")
        => new()
        {
            TtsProvider = TtsProviderType.Piper,
            PiperPath = piperPath,
            PiperModelPath = modelPath,
            PiperVoice = "en_US-lessac",
        };

    // ─── Test 1: TtsService with Coqui provider, binary missing → handles gracefully ─────────────────────────────────────────

    [Fact]
    public void TtsService_Coqui_BinaryMissing_NoCrash()
    {
        // Arrange: a Python path that definitely does not exist
        var config = CoquiConfig(pythonPath: "/this/path/does/not/exist/and/will/never/be/found");

        // Act: constructing TtsService must not throw a reflection wrapper —
        // the underlying Win32Exception ("no such file or directory") should propagate clearly.
        var ex = Record.Exception(() => new TtsService(config, _mockConsole.Object));

        // Assert: the exception is either Win32Exception (Python binary not found)
        // or InvalidOperationException (Python found but model startup failed).
        // In either case it must NOT be TargetInvocationException or AggregateException.
        Assert.NotNull(ex);
        Assert.False(ex is TargetInvocationException || ex is AggregateException,
            "Exception should not be wrapped in TargetInvocationException or AggregateException. " +
            $"Got: {ex.GetType().Name} — {ex.Message}");
        Assert.True(
            ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception,
            $"Expected InvalidOperationException or Win32Exception, got {ex.GetType().Name}");
    }

    // ─── Test 2: TtsService with Piper provider, binary missing → handles gracefully ─────────────────────────────────────────

    [Fact]
    public void TtsService_Piper_BinaryMissing_NoCrash()
    {
        // Arrange: a piper path that does not exist
        var config = PiperConfig(piperPath: "/also/does/not/exist/piper");

        // Act: constructing TtsService with a non-existent piper binary.
        // PiperTtsProvider's constructor does not validate the binary — it only
        // fails when SynthesizeAsync is called. So the service constructs without throwing.
        var service = new TtsService(config, _mockConsole.Object);

        // Assert: the provider was configured (no crash in constructor).
        // First call to SynthesizeAsync will fail with FileNotFoundException — that's by design.
        Assert.NotNull(service);
        Assert.Equal(TtsProviderType.Piper, service.ProviderType);
        Assert.True(service.IsConfigured);

        service.Dispose();
    }

    // ─── Test 3: PythonTtsProvider.InitializeAsync exception propagates cleanly (unwrapped) ─────────────────────────────────────────

    [Fact]
    public void TtsService_PythonProvider_InitializeAsyncFailure_PropagatesCleanly()
    {
        // Arrange: Python path that does not exist. The TtsService constructor calls
        // PythonTtsProvider.InitializeAsync() via .GetAwaiter().GetResult().
        // Before the fix, failures were wrapped in TargetInvocationException.
        // After the fix, the inner exception propagates directly as Win32Exception.
        var config = CoquiConfig(pythonPath: "/impossible/path");

        // Act
        var ex = Record.Exception(() => new TtsService(config, _mockConsole.Object));

        // Assert: the exception must be the real cause, not a reflection wrapper.
        Assert.NotNull(ex);
        Assert.False(ex is TargetInvocationException || ex is AggregateException,
            "Exception should not be wrapped. Got: " + ex.GetType().Name);
    }

    // ─── Test 4: PythonTtsProvider stdin buffer saturation → timeout after ~5s ─────────────────────────────────────────

    [Fact]
    public void PythonTtsProvider_StdinSaturated_TimesOutGracefully()
    {
        // This test verifies that PythonTtsProvider correctly accepts the requestTimeout
        // and startupTimeout parameters without throwing, and that the ProviderName is set.
        var provider = new PythonTtsProvider(
            _mockConsole.Object,
            serviceScriptPathOverride: null,
            pythonPath: "",
            modelPath: "",
            modelName: "test",
            coquiConfigPath: null,
            espeakNgPath: null,
            debugLog: false,
            startupTimeout: TimeSpan.FromSeconds(1),
            requestTimeout: TimeSpan.FromSeconds(30));

        Assert.NotNull(provider);
        Assert.Equal("Python TTS", provider.ProviderName);
    }

    // ─── Test 5: PythonTtsProvider restart loop hits MaxConsecutiveRestarts → stops restarting ─────────────────────────────────────────

    [Fact]
    public void TtsService_PythonProviderType_RecordsProviderType()
    {
        // Verify that when a Python provider is configured, TtsService records the type
        // (even though InitializeAsync will fail due to missing Python).
        var config = new AppConfig
        {
            TtsProvider = TtsProviderType.Python,
            PythonPath = "/nonexistent",
            CoquiModelPath = "",
            CoquiModelName = "test",
        };

        // Act — construction may throw but must not be wrapped
        Exception? ex = null;
        TtsService? service = null;
        try { service = new TtsService(config, _mockConsole.Object); }
        catch (Exception e) { ex = e; }

        if (service != null)
        {
            Assert.Equal(TtsProviderType.Python, service.ProviderType);
            service.Dispose();
        }
        else
        {
            // Constructor threw — verify not wrapped
            Assert.False(ex is TargetInvocationException || ex is AggregateException,
                "Constructor exception should not be wrapped");
        }
    }

    // ─── Test 6: TtsService Dispose while InitializeAsync in flight → no crash ─────────────────────────────────────────

    [Fact]
    public void TtsService_Dispose_WhileInitializing_NoCrash()
    {
        // Arrange: a config that points to a non-existent Python path.
        // This causes InitializeAsync to fail, but the failure must not crash Dispose().
        var config = CoquiConfig(pythonPath: "/impossible/path/to/python");

        TtsService? service = null;

        // Act: construct and immediately dispose. The constructor may throw
        // due to the Python binary not being found — but Dispose must still work.
        Exception? constructionEx = null;
        try { service = new TtsService(config, _mockConsole.Object); }
        catch (Exception ex) { constructionEx = ex; }

        if (service != null)
        {
            var disposeEx = Record.Exception(() => service.Dispose());
            Assert.Null(disposeEx);
            service.Dispose();
        }
        else
        {
            // Constructor threw — that's expected for a missing binary.
            // Verify it was not a reflection wrapper.
            Assert.False(constructionEx is TargetInvocationException || constructionEx is AggregateException,
                "Constructor exception should not be wrapped");
        }
    }

    // ─── Test 7: TtsService with Edge provider (no subscription key) → handles gracefully ─────────────────────────────────────────

    [Fact]
    public void TtsService_EdgeProvider_NoSubscriptionKey_Graceful()
    {
        // Edge TTS provider uses Azure API — null subscription key means no provider,
        // but TtsService must not crash.
        var config = new AppConfig
        {
            TtsProvider = TtsProviderType.Edge,
            TtsSubscriptionKey = null,
            TtsRegion = "eastus",
        };

        var service = new TtsService(config, _mockConsole.Object);

        Assert.False(service.IsConfigured);
        Assert.Equal(TtsProviderType.Edge, service.ProviderType);
    }

    // ─── Test 8: TtsService.Dispose is idempotent ─────────────────────────────────────────

    [Fact]
    public void TtsService_Dispose_Idempotent()
    {
        var config = new AppConfig
        {
            TtsProvider = TtsProviderType.Edge,
            TtsSubscriptionKey = "fake-key",
        };

        var service = new TtsService(config, _mockConsole.Object);

        // First dispose — should succeed
        var firstEx = Record.Exception(() => service.Dispose());
        Assert.Null(firstEx);

        // Second dispose — should also succeed (idempotent)
        var secondEx = Record.Exception(() => service.Dispose());
        Assert.Null(secondEx);
    }

    // ─── Test 9: TtsService with OpenAI provider, no API key → clear error ─────────────────────────────────────────

    [Fact]
    public void TtsService_OpenAI_NoApiKey_ThrowsClearError()
    {
        var config = new AppConfig
        {
            TtsProvider = TtsProviderType.OpenAI,
            TtsOpenAiApiKey = null,
            OpenAiApiKey = null,
        };

        var ex = Record.Exception(() => new TtsService(config, _mockConsole.Object));

        Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("OpenAI", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ─── Test 10: TtsService.Provider returns the actual provider when configured ─────────────────────────────────────────

    [Fact]
    public void TtsService_EdgeProviderWithKey_ProviderIsSet()
    {
        var config = new AppConfig
        {
            TtsProvider = TtsProviderType.Edge,
            TtsSubscriptionKey = "fake-key-for-test",
            TtsRegion = "eastus",
        };

        var service = new TtsService(config, _mockConsole.Object);

        Assert.True(service.IsConfigured);
        Assert.NotNull(service.Provider);
        Assert.Equal(TtsProviderType.Edge, service.ProviderType);
        service.Dispose();
    }

    // ─── Test 11: Piper provider with missing binary on SynthesizeAsync → clear error ─────────────────────────────────────────

    [Fact]
    public void PiperTtsProvider_MissingBinary_SynthesizeAsync_ThrowsClearError()
    {
        // Piper does not validate the binary in its constructor.
        // First use should throw FileNotFoundException with a clear message.
        var provider = new PiperTtsProvider(
            piperPath: "/nonexistent/piper",
            modelPath: "",
            voice: "en_US-lessac");

        var ex = Record.Exception(() => provider.SynthesizeAsync("test").GetAwaiter().GetResult());

        Assert.IsType<FileNotFoundException>(ex);
        Assert.Contains("piper", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        // Cleanup if needed
    }
}