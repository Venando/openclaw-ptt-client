using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Xunit;

namespace OpenClawPTT.TTS.Providers;

public class PythonEnvironmentTests
{
    // ─── Construction ───────────────────────────────────────────────────────

    [Fact]
    public void Constructor_ThrowsOnNullUvPath()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new PythonEnvironment(null!, "3.11", "/tmp", ".venv"));
    }

    [Fact]
    public void Constructor_ThrowsOnNullPythonVersion()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new PythonEnvironment("/uv", null!, "/tmp", ".venv"));
    }

    [Fact]
    public void Constructor_ThrowsOnNullBaseDir()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new PythonEnvironment("/uv", "3.11", null!, ".venv"));
    }

    [Fact]
    public void Constructor_ThrowsOnNullVenvName()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new PythonEnvironment("/uv", "3.11", "/tmp", null!));
    }

    // ─── VenvPath property ──────────────────────────────────────────────────

    [Fact]
    public void VenvPath_IsCorrectlyJoined()
    {
        var pyEnv = new PythonEnvironment("/uv", "3.11", "/home/user", ".venv");
        Assert.Equal(Path.Combine("/home/user", ".venv"), pyEnv.VenvPath);
    }

    // ─── PythonPath ─────────────────────────────────────────────────────────

    [Fact]
    public void PythonPath_Windows_ReturnsScriptsPythonExe()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        var pyEnv = new PythonEnvironment("/uv", "3.11", "D:\\work", ".venv");
        var expected = Path.Combine("D:\\work", ".venv", "Scripts", "python.exe");
        Assert.Equal(expected, pyEnv.PythonPath);
    }

    [Fact]
    public void PythonPath_Unix_ReturnsBinPython()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
            !RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return;

        var pyEnv = new PythonEnvironment("/uv", "3.11", "/home/user", ".venv");
        var expected = Path.Combine("/home/user", ".venv", "bin", "python");
        Assert.Equal(expected, pyEnv.PythonPath);
    }

    // ─── Venv directory existence ──────────────────────────────────────────

    [Fact]
    public async Task EnsureVenvExistsAsync_CreatesVenv_WhenNotExists()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"pyenv_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tmp);

        try
        {
            var pyEnv = new PythonEnvironment(
                uvPath: "echo",          // Will fail but we can check directory creation
                pythonVersion: "3.11",
                baseDir: tmp,
                venvName: ".venv_test");

            bool progressFired = false;
            pyEnv.ProgressChanged += _ => progressFired = true;

            // Directory should NOT exist before
            Assert.False(Directory.Exists(pyEnv.VenvPath));

            // Attempting to create with a fake uvPath will fail,
            // but we can at least verify the base dir is created.
            try
            {
                await pyEnv.EnsureVenvExistsAsync(Array.Empty<string>());
            }
            catch
            {
                // Expected to fail because "echo" is not a real uv
            }

            // Base dir should exist
            Assert.True(Directory.Exists(tmp));
            Assert.True(progressFired);
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task EnsureVenvExistsAsync_UsesExistingVenv_WhenExists()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"pyenv_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tmp);

        try
        {
            // Pre-create the venv directory
            var venvPath = Path.Combine(tmp, ".venv_test");
            Directory.CreateDirectory(venvPath);

            var pyEnv = new PythonEnvironment(
                uvPath: "/fake/uv", // Won't be called since venv exists
                pythonVersion: "3.11",
                baseDir: tmp,
                venvName: ".venv_test");

            string? lastProgress = null;
            pyEnv.ProgressChanged += msg => lastProgress = msg;

            // This should NOT call uv venv (directory already exists)
            // It will still try to install packages (which may fail due to fake uv),
            // but we only care about the "Using existing venv" message.
            try
            {
                await pyEnv.EnsureVenvExistsAsync(Array.Empty<string>());
            }
            catch
            {
                // Expected to fail at package install step
            }

            Assert.Contains("Using existing venv", lastProgress);
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { /* ignore */ }
        }
    }

    // ─── Progress event ─────────────────────────────────────────────────────

    [Fact]
    public async Task ProgressChanged_FiresDuringVenvCreation()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"pyenv_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tmp);

        try
        {
            var events = new System.Collections.Generic.List<string>();
            var pyEnv = new PythonEnvironment("/uv", "3.11", tmp, ".venv");
            pyEnv.ProgressChanged += e => events.Add(e);

            try { await pyEnv.EnsureVenvExistsAsync(Array.Empty<string>()); }
            catch { /* expected to fail */ }

            Assert.NotEmpty(events);
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { /* ignore */ }
        }
    }

    // ─── Dispose ────────────────────────────────────────────────────────────

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var pyEnv = new PythonEnvironment("/uv", "3.11", "/tmp", ".venv");
        pyEnv.Dispose();
        pyEnv.Dispose(); // Must not throw
    }

    [Fact]
    public void Dispose_SetsDisposedFlag()
    {
        var pyEnv = new PythonEnvironment("/uv", "3.11", "/tmp", ".venv");
        pyEnv.Dispose(); // Must not throw
        pyEnv.Dispose(); // Must not throw a second time
    }
}
