using OpenClawPTT;
using OpenClawPTT.Transcriber;
using System;
using Xunit;

namespace OpenClawPTT.Tests;

public class TranscriberFactoryTests
{
    #region Null API key scenarios

    [Fact]
    public void Create_OpenAiProvider_NullApiKey_ThrowsArgumentNullException()
    {
        var cfg = new AppConfig
        {
            SttProvider = "openai",
            OpenAiApiKey = null
        };

        var ex = Record.Exception(() => TranscriberFactory.Create(cfg));

        Assert.IsType<ArgumentNullException>(ex);
    }

    [Fact]
    public void Create_WhisperCppProvider_NullApiKey_DoesNotThrow()
    {
        // whisper-cpp doesn't use an API key, path/model are optional
        var cfg = new AppConfig
        {
            SttProvider = "whisper-cpp",
            WhisperCppPath = null,
            WhisperCppModelPath = null
        };

        var ex = Record.Exception(() => TranscriberFactory.Create(cfg));

        // Should not throw — whisper-cpp uses path-based config
        Assert.Null(ex);
    }

    [Fact]
    public void Create_OpenAiProvider_EmptyApiKey_ThrowsArgumentException()
    {
        var cfg = new AppConfig
        {
            SttProvider = "openai",
            OpenAiApiKey = ""
        };

        var ex = Record.Exception(() => TranscriberFactory.Create(cfg));

        // OpenAI adapter throws ArgumentNullException for empty string key
        Assert.True(ex is ArgumentNullException or ArgumentException);
    }

    [Fact]
    public void Create_OpenAiProvider_WhitespaceApiKey_ThrowsArgumentException()
    {
        var cfg = new AppConfig
        {
            SttProvider = "openai",
            OpenAiApiKey = "   "
        };

        var ex = Record.Exception(() => TranscriberFactory.Create(cfg));

        // OpenAI adapter throws ArgumentNullException for whitespace-only key
        Assert.True(ex is ArgumentNullException or ArgumentException);
    }

    #endregion

    #region Unknown / invalid provider scenarios

    [Fact]
    public void Create_UnknownProvider_ThrowsArgumentException()
    {
        var cfg = new AppConfig
        {
            SttProvider = "unknown-provider"
        };

        var ex = Record.Exception(() => TranscriberFactory.Create(cfg));

        Assert.IsType<ArgumentException>(ex);
        Assert.Contains("Unknown STT provider", ex.Message);
    }

    #endregion

    #region Default provider scenarios

    [Fact]
    public void Create_NullProvider_UsesGroq()
    {
        var cfg = new AppConfig
        {
            SttProvider = null,
            GroqApiKey = "gsk_testkey"
        };

        var result = TranscriberFactory.Create(cfg);

        Assert.IsType<GroqTranscriberAdapter>(result);
    }

    [Fact]
    public void Create_EmptyProvider_UsesGroq()
    {
        var cfg = new AppConfig
        {
            SttProvider = "",
            GroqApiKey = "gsk_testkey"
        };

        var result = TranscriberFactory.Create(cfg);

        Assert.IsType<GroqTranscriberAdapter>(result);
    }

    #endregion

    #region Null config scenario

    [Fact]
    public void Create_NullConfig_ThrowsNullReferenceException()
    {
        var ex = Record.Exception(() => TranscriberFactory.Create(null!));

        Assert.IsType<NullReferenceException>(ex);
    }

    #endregion

    #region Valid provider instantiations

    [Fact]
    public void Create_OpenAiProvider_ValidKey_ReturnsOpenAiTranscriberAdapter()
    {
        var cfg = new AppConfig
        {
            SttProvider = "openai",
            OpenAiApiKey = "sk-valid-key"
        };

        var result = TranscriberFactory.Create(cfg);

        Assert.IsType<OpenAiTranscriberAdapter>(result);
    }

    [Fact]
    public void Create_WhisperCppProvider_ValidPath_ReturnsWhisperCppTranscriberAdapter()
    {
        var cfg = new AppConfig
        {
            SttProvider = "whisper-cpp",
            WhisperCppPath = "/usr/local/bin/whisper",
            WhisperCppModelPath = "/models/ggml-base.bin"
        };

        var result = TranscriberFactory.Create(cfg);

        Assert.IsType<WhisperCppTranscriberAdapter>(result);
    }

    [Fact]
    public void Create_GroqProvider_ReturnsGroqTranscriberAdapter()
    {
        var cfg = new AppConfig
        {
            SttProvider = "groq",
            GroqApiKey = "gsk_testkey"
        };

        var result = TranscriberFactory.Create(cfg);

        Assert.IsType<GroqTranscriberAdapter>(result);
    }

    [Fact]
    public void Create_OpenAiProvider_CaseInsensitive()
    {
        var cfg = new AppConfig
        {
            SttProvider = "OPENAI",
            OpenAiApiKey = "sk-valid-key"
        };

        var result = TranscriberFactory.Create(cfg);

        Assert.IsType<OpenAiTranscriberAdapter>(result);
    }

    #endregion

    #region Disposal

    [Fact]
    public void Create_ReturnsDisposable_Adapter_CanBeDisposed()
    {
        var cfg = new AppConfig
        {
            SttProvider = "openai",
            OpenAiApiKey = "sk-valid-key"
        };

        var result = TranscriberFactory.Create(cfg);

        var disposable = Assert.IsAssignableFrom<IDisposable>(result);
        disposable.Dispose();
    }

    #endregion
}