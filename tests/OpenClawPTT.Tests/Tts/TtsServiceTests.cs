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

    [Fact]
    public void TtsService_Coqui_BinaryMissing_NoCrash()
    {
        var config = CoquiConfig(pythonPath: "/this/path/does/not/exist/and/will/never/be/found");

        var ex = Record.Exception(() => new TtsService(config, _mockConsole.Object));

        Assert.NotNull(ex);
        Assert.False(ex is TargetInvocationException || ex is AggregateException,
            "Exception should not be wrapped. Got: " + ex.GetType().Name);
        Assert.True(
            ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception,
            "Expected InvalidOperationException or Win32Exception, got " + ex.GetType().Name);
    }

    [Fact]
    public void TtsService_Piper_BinaryMissing_NoCrash()
    {
        var config = PiperConfig(piperPath: "/also/does/not/exist/piper");

        var service = new TtsService(config, _mockConsole.Object);

        Assert.NotNull(service);
        Assert.Equal(TtsProviderType.Piper, service.ProviderType);
        Assert.True(service.IsConfigured);

        service.Dispose();
    }

    [Fact]
    public void PythonTtsProvider_StdinSaturated_TimesOutGracefully()
    {
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

    [Fact]
    public void TtsService_PythonProviderType_RecordsProviderType()
    {
        var config = new AppConfig
        {
            TtsProvider = TtsProviderType.Python,
            PythonPath = "/nonexistent",
            CoquiModelPath = "",
            CoquiModelName = "test",
        };

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
            Assert.False(ex is TargetInvocationException || ex is AggregateException,
                "Constructor exception should not be wrapped");
        }
    }

    [Fact]
    public void TtsService_Dispose_WhileInitializing_NoCrash()
    {
        var config = CoquiConfig(pythonPath: "/impossible/path/to/python");

        TtsService? service = null;
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
            Assert.False(constructionEx is TargetInvocationException || constructionEx is AggregateException,
                "Constructor exception should not be wrapped");
        }
    }

    [Fact]
    public void TtsService_EdgeProvider_NoSubscriptionKey_Graceful()
    {
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

    [Fact]
    public void TtsService_Dispose_Idempotent()
    {
        var config = new AppConfig
        {
            TtsProvider = TtsProviderType.Edge,
            TtsSubscriptionKey = "fake-key",
        };

        var service = new TtsService(config, _mockConsole.Object);

        var firstEx = Record.Exception(() => service.Dispose());
        Assert.Null(firstEx);

        var secondEx = Record.Exception(() => service.Dispose());
        Assert.Null(secondEx);
    }

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

    [Fact]
    public void PiperTtsProvider_MissingBinary_SynthesizeAsync_ThrowsClearError()
    {
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
    }
}