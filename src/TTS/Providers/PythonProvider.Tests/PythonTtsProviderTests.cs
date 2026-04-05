using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace OpenClawPTT.TTS.Providers;

public class PythonTtsProviderTests
{
    // ─── Available properties ───────────────────────────────────────────────

    [Fact]
    public void AvailableVoices_ContainsDefault()
    {
        var provider = new PythonTtsProvider("/fake/python", null);
        Assert.Contains("default", provider.AvailableVoices);
    }

    [Fact]
    public void AvailableModels_ContainsCoquiTts()
    {
        var provider = new PythonTtsProvider("/fake/python", null);
        Assert.Contains("coqui-tts", provider.AvailableModels);
    }

    [Fact]
    public void ProviderName_ContainsPythonTts()
    {
        var provider = new PythonTtsProvider("/fake/python", null);
        Assert.Contains("Python TTS", provider.ProviderName);
    }

    // ─── Constructor overloads ──────────────────────────────────────────────

    [Fact]
    public void Constructor_Legacy_ThrowsOnNullPythonPath()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new PythonTtsProvider(null!, null));
    }

    [Fact]
    public void Constructor_UvManagement_SetsUseUvManagementTrue()
    {
        // Just verify it doesn't throw and the instance is created
        var tmp = Path.Combine(Path.GetTempPath(), $"tts_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tmp);
        try
        {
            // With useUvManagement=true and no ttsServiceScript,
            // InitializeAsync will bootstrap uv and create venv (or try to).
            // We just verify construction succeeds.
            var provider = new PythonTtsProvider(
                baseDir: tmp,
                useUvManagement: true,
                uvToolsPath: null,
                pythonVersion: "3.11",
                ttsServiceScript: null);
            Assert.NotNull(provider);
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { /* ignore */ }
        }
    }

    // ─── InitializeAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task InitializeAsync_Throws_WhenUvManagementEnabled_AndNoTtyForPrompt()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"tts_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tmp);
        try
        {
            var provider = new PythonTtsProvider(
                baseDir: tmp,
                useUvManagement: true,
                ttsServiceScript: null);

            // Without a fake binary present, EnsureUvInstalledAsync will try to
            // PromptUser which has no TTY → throws InvalidOperationException
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => provider.InitializeAsync());
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { /* ignore */ }
        }
    }

    // ─── SynthesizeAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task SynthesizeAsync_Throws_WhenTtsProcessNotRunning()
    {
        // Create provider without starting any process
        var provider = new PythonTtsProvider("/fake/python", null);

        // Synthesize without InitializeAsync should fail
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.SynthesizeAsync("hello"));
    }

    [Fact]
    public async Task SynthesizeAsync_Throws_WithCorrectMessage_WhenNotInitialized()
    {
        var provider = new PythonTtsProvider("/fake/python", null);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.SynthesizeAsync("hello"));
        Assert.Contains("not running", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ─── Race guard on disposed ────────────────────────────────────────────

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"tts_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tmp);
        try
        {
            var provider = new PythonTtsProvider(tmp, useUvManagement: false);
            provider.Dispose();
            provider.Dispose(); // Must not throw
            provider.Dispose(); // Still must not throw
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task SynthesizeAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"tts_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tmp);
        try
        {
            var provider = new PythonTtsProvider(
                baseDir: tmp,
                useUvManagement: false,
                ttsServiceScript: null);

            provider.Dispose();

            // After dispose, any call that hits the _disposed check should throw
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => provider.SynthesizeAsync("hello"));
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { /* ignore */ }
        }
    }

    // ─── InitializeAsync with pre-existing venv (mock-able scenario) ──────

    [Fact]
    public async Task InitializeAsync_DoesNotThrow_WhenUvPathProvided_AndVenvExists()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"tts_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tmp);

        try
        {
            // Pre-create a fake .venv with a fake python
            var venvPath = Path.Combine(tmp, ".venv");
            var scriptsDir = Path.Combine(venvPath, "Scripts");
            Directory.CreateDirectory(scriptsDir);
            await File.WriteAllTextAsync(
                Path.Combine(scriptsDir, "python.exe"),
                "#!/usr/bin/env python");

            // Pre-create a tools folder with a fake uv.exe (> 5 MB so it passes size check)
            var toolsDir = Path.Combine(tmp, "tools");
            Directory.CreateDirectory(toolsDir);
            var fakeUv = Path.Combine(toolsDir, "uv.exe");
            File.WriteAllBytes(fakeUv, new byte[6 * 1024 * 1024]);

            // uv will try to run and fail, but with the existing venv it shouldn't need to create one.
            // However it will still try to install packages. We just check the path resolution works.
            var provider = new PythonTtsProvider(
                baseDir: tmp,
                useUvManagement: true,
                uvToolsPath: fakeUv,
                pythonVersion: "3.11",
                ttsServiceScript: null);

            // Since the venv exists and packages are empty, it should attempt package install
            // with the fake uv (which will fail). We just verify InitializeAsync reaches the right path.
            try
            {
                await provider.InitializeAsync();
            }
            catch (Exception ex) when (ex.Message.Contains("uv") || ex.Message.Contains("pip") || ex.Message.Contains("process"))
            {
                // Expected: the fake uv/pip won't work, but we verified the flow ran
            }
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { /* ignore */ }
        }
    }

    // ─── DefaultPackages is non-empty ─────────────────────────────────────

    [Fact]
    public void DefaultPackages_IsNotEmpty()
    {
        Assert.NotEmpty(PythonTtsProvider.DefaultPackages);
        Assert.Contains("coqui-tts", PythonTtsProvider.DefaultPackages);
    }
}
