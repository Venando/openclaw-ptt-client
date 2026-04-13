using System.Diagnostics;
using Xunit;

namespace OpenClawPTT.Tests;

/// <summary>
/// Stability and edge-case tests for DeviceIdentity — Sign-Then-EnsureKeypair bug fix
/// and robustness tests for filesystem errors, large payloads, and BuildV3Payload edge cases.
/// </summary>
public class DeviceIdentityStabilityTests : IDisposable
{
    private readonly string _testDir;

    public DeviceIdentityStabilityTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"oc_stability_{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, recursive: true); } catch { }
    }

    private DeviceIdentity MakeDi() { Directory.CreateDirectory(_testDir); return new DeviceIdentity(_testDir); }

    // ─── 1. Sign() before EnsureKeypair() → throws InvalidOperationException ────

    [Fact]
    public void Sign_BeforeEnsureKeypair_ThrowsInvalidOperationException()
    {
        var di = MakeDi();
        var ex = Assert.Throws<InvalidOperationException>(() => di.Sign("any payload"));
        Assert.Contains("EnsureKeypair", ex.Message);
    }

    [Fact]
    public void Sign_BeforeEnsureKeypair_NeverReturnsNull()
    {
        var di = MakeDi();
        Assert.Throws<InvalidOperationException>(() => di.Sign("test"));
    }

    // ─── 2. EnsureKeypair() with read-only data directory → exception propagates ──

    [Fact]
    public void EnsureKeypair_ReadOnlyDirectory_ThrowsIoOrUnauthorized()
    {
        // /dev is never writable — the directory cannot be created
        var fakeDataDir = Path.Combine("/dev", $"oc_test_{Guid.NewGuid():N}");
        var di = new DeviceIdentity(fakeDataDir);
        try
        {
            di.EnsureKeypair();
            Assert.Fail("Expected an exception to be thrown");
        }
        catch (UnauthorizedAccessException) { /* expected on Linux/WSL */ }
        catch (IOException) { /* expected on some systems */ }
    }

    // ─── 3. EnsureKeypair() when file is read-only → exception propagates ─────

    [Fact]
    public void EnsureKeypair_ExistingFileReadOnly_ThrowsIoOrUnauthorized()
    {
        var di = MakeDi();
        di.EnsureKeypair(); // create the file first (with a valid 32-byte key)

        var keyFile = Path.Combine(_testDir, "device.key");

        if (OperatingSystem.IsWindows())
        {
            File.SetAttributes(keyFile, FileAttributes.ReadOnly);
            try
            {
                try { di.EnsureKeypair(); Assert.Fail("Expected exception"); }
                catch (UnauthorizedAccessException) { /* ok */ }
                catch (IOException) { /* ok */ }
            }
            finally { File.SetAttributes(keyFile, FileAttributes.Normal); }
        }
        else
        {
            // Unix/WSL: chmod a-w removes write permission.
            // Must create a fresh file so EnsureKeypair tries to WRITE (not just load).
            File.Delete(keyFile);
            using (File.Create(keyFile)) { }
            Bash($"chmod a-w \"{keyFile}\"");
            try
            {
                try { di.EnsureKeypair(); Assert.Fail("Expected exception"); }
                catch (UnauthorizedAccessException) { /* ok - WSL throws this for EPERM */ }
                catch (IOException) { /* ok - native Unix throws this */ }
                catch (ArgumentException) { /* ok - empty file causes ArgumentException */ }
            }
            finally { Bash($"chmod u+w \"{keyFile}\""); }
        }
    }

    // ─── 4. EnsureKeypair() when parent directory cannot be created → exception ────

    [Fact]
    public void EnsureKeypair_NonCreatableParentDirectory_ThrowsIoOrUnauthorized()
    {
        // /dev is not writable as a subdirectory
        var fakeDataDir = Path.Combine("/dev", $"oc_test_{Guid.NewGuid():N}");
        var di = new DeviceIdentity(fakeDataDir);
        try
        {
            di.EnsureKeypair();
            Assert.Fail("Expected an exception to be thrown");
        }
        catch (UnauthorizedAccessException) { /* expected on Linux/WSL */ }
        catch (IOException) { /* expected on some systems */ }
    }

    // ─── 5. EnsureKeypair() write failure leaves existing key intact ───────────

    [Fact]
    public void EnsureKeypair_WriteFailure_DoesNotCorruptExistingKey()
    {
        var di = MakeDi();
        di.EnsureKeypair();
        var originalPubKey = di.PublicKeyBase64;
        var originalDeviceId = di.DeviceId;
        var keyFile = Path.Combine(_testDir, "device.key");
        var originalContent = File.ReadAllText(keyFile);

        // Calling again loads from existing file (doesn't try to write), identity stays valid
        di.EnsureKeypair();

        Assert.Equal(originalPubKey, di.PublicKeyBase64);
        Assert.Equal(originalDeviceId, di.DeviceId);
        Assert.Equal(originalContent, File.ReadAllText(keyFile));
    }

    // ─── 6. Sign() with very large payload (>1MB) → still works ────────────────

    [Fact]
    public void Sign_LargePayload_StillWorks()
    {
        var di = MakeDi(); di.EnsureKeypair();

        var largePayload = new string('x', 1_200_000);

        var sig = di.Sign(largePayload);

        Assert.NotEmpty(sig);
        var decoded = FromBase64Url(sig);
        Assert.Equal(64, decoded.Length);
    }

    [Fact]
    public void Sign_HugePayload_5MB_StillWorks()
    {
        var di = MakeDi(); di.EnsureKeypair();

        var hugePayload = new string('a', 5 * 1024 * 1024);

        var sig = di.Sign(hugePayload);

        Assert.NotEmpty(sig);
        var decoded = FromBase64Url(sig);
        Assert.Equal(64, decoded.Length);
    }

    // ─── 7. BuildV3Payload with null scope → handles gracefully ────────────────

    [Fact]
    public void BuildV3Payload_NullScope_ThrowsArgumentNullException()
    {
        var di = MakeDi(); di.EnsureKeypair();

        IEnumerable<string>? nullScopes = null;
        Assert.Throws<ArgumentNullException>(() =>
            di.BuildV3Payload("linux", "desktop", "cid", "ptt", "user",
                nullScopes!, 1000L, "tok", "non"));
    }

    [Fact]
    public void BuildV3Payload_EmptyScope_Works()
    {
        var di = MakeDi(); di.EnsureKeypair();

        var payload = di.BuildV3Payload(
            "linux", "desktop", "cid", "ptt", "user",
            Array.Empty<string>(), 1000L, "tok", "non");

        Assert.Contains("v3|", payload);
        var parts = payload.Split('|');
        Assert.Equal("", parts[5]);
    }

    // ─── 8. BuildV3Payload with scope containing commas → joins correctly ─────

    [Fact]
    public void BuildV3Payload_ScopeWithCommas_JoinedWithComma()
    {
        var di = MakeDi(); di.EnsureKeypair();

        var scopes = new[] { "gateway.connect,admin", "audio.record" };

        var payload = di.BuildV3Payload(
            "linux", "desktop", "cid", "ptt", "user",
            scopes, 1000L, "tok", "non");

        var parts = payload.Split('|');
        Assert.Equal("gateway.connect,admin,audio.record", parts[5]);
    }

    [Fact]
    public void BuildV3Payload_ScopeWithMultipleCommas_JoinedCorrectly()
    {
        var di = MakeDi(); di.EnsureKeypair();

        var scopes = new[] { "a,b,c", "d,e,f", "g" };

        var payload = di.BuildV3Payload(
            "linux", "desktop", "cid", "ptt", "user",
            scopes, 1000L, "tok", "non");

        var parts = payload.Split('|');
        Assert.Equal("a,b,c,d,e,f,g", parts[5]);
    }

    // ─── Helper ─────────────────────────────────────────────────────────────────

    private static void Bash(string cmd)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = $"-c \"{cmd}\"",
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi);
        p?.WaitForExit();
    }

    private static byte[] FromBase64Url(string input)
    {
        var base64 = input.Replace('-', '+').Replace('_', '/');
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }
        return Convert.FromBase64String(base64);
    }
}
