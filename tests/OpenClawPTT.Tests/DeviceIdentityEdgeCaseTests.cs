using Xunit;

namespace OpenClawPTT.Tests;

/// <summary>
/// QA tests for DeviceIdentity edge cases.
/// </summary>
public class DeviceIdentityEdgeCaseTests : IDisposable
{
    private readonly string _testDir;

    public DeviceIdentityEdgeCaseTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"oc_deveid_{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, recursive: true); } catch { }
    }

    private DeviceIdentity MakeDi() { Directory.CreateDirectory(_testDir); return new DeviceIdentity(_testDir); }

    // ─── EnsureKeypair idempotency ─────────────────────────────────────────────

    [Fact]
    public void EnsureKeypair_CreatesKeysIfMissing()
    {
        var di = MakeDi();
        di.EnsureKeypair();

        Assert.True(File.Exists(Path.Combine(_testDir, "device.key")));
        Assert.NotEmpty(di.DeviceId);
        Assert.NotEmpty(di.PublicKeyBase64);
    }

    [Fact]
    public void EnsureKeypair_ReusesExistingKey_NoNewKeyGenerated()
    {
        var di1 = MakeDi();
        di1.EnsureKeypair();
        var pubKey1 = di1.PublicKeyBase64;
        var id1 = di1.DeviceId;
        var fileContent = File.ReadAllText(Path.Combine(_testDir, "device.key"));

        // Call EnsureKeypair again on same or new instance
        var di2 = MakeDi();
        di2.EnsureKeypair();

        Assert.Equal(pubKey1, di2.PublicKeyBase64);
        Assert.Equal(id1, di2.DeviceId);
        Assert.Equal(fileContent, File.ReadAllText(Path.Combine(_testDir, "device.key")));
    }

    [Fact]
    public void EnsureKeypair_CanBeCalledMultipleTimes()
    {
        var di = MakeDi();
        di.EnsureKeypair();
        di.EnsureKeypair(); // should not throw, not regenerate
        di.EnsureKeypair();

        Assert.NotEmpty(di.DeviceId);
        Assert.NotEmpty(di.PublicKeyBase64);
    }

    [Fact]
    public void EnsureKeypair_DifferentInstances_SameDir_SameIdentity()
    {
        var di1 = MakeDi(); di1.EnsureKeypair();
        var di2 = MakeDi(); di2.EnsureKeypair();
        var di3 = MakeDi(); di3.EnsureKeypair();

        Assert.Equal(di1.DeviceId, di2.DeviceId);
        Assert.Equal(di2.DeviceId, di3.DeviceId);
        Assert.Equal(di1.PublicKeyBase64, di2.PublicKeyBase64);
        Assert.Equal(di2.PublicKeyBase64, di3.PublicKeyBase64);
    }

    // ─── Keys stored in correct directory ──────────────────────────────────────

    [Fact]
    public void Keys_StoredInDataDir_NotElsewhere()
    {
        var di = MakeDi();
        di.EnsureKeypair();

        var keyFile = Path.Combine(_testDir, "device.key");
        Assert.True(File.Exists(keyFile));

        // No key files outside _testDir
        var allFiles = Directory.GetFiles(_testDir, "*.key", SearchOption.AllDirectories);
        Assert.Single(allFiles);
        Assert.Equal(keyFile, allFiles[0]);
    }

    // ─── Sign consistency ──────────────────────────────────────────────────────

    [Fact]
    public void Sign_SamePayload_ProducesSameSignature()
    {
        var di = MakeDi(); di.EnsureKeypair();
        var payload = "v3|deviceid|clientid|clientmode|role|scope1,scope2|1234567890|token|nonce|platform|family";

        var sig1 = di.Sign(payload);
        var sig2 = di.Sign(payload);
        var sig3 = di.Sign(payload);

        Assert.Equal(sig1, sig2);
        Assert.Equal(sig2, sig3);
    }

    [Fact]
    public void Sign_DifferentPayloads_ProduceDifferentSignatures()
    {
        var di = MakeDi(); di.EnsureKeypair();

        var sig1 = di.Sign("payload one");
        var sig2 = di.Sign("payload two");
        var sig3 = di.Sign("");

        Assert.NotEqual(sig1, sig2);
        Assert.NotEqual(sig2, sig3);
        Assert.NotEqual(sig1, sig3);
    }

    [Fact]
    public void Sign_OutputIsBase64UrlEncoded()
    {
        var di = MakeDi(); di.EnsureKeypair();
        var sig = di.Sign("test");

        // base64url: no +, /, or = padding
        Assert.DoesNotContain("+", sig);
        Assert.DoesNotContain("/", sig);
        Assert.False(sig.EndsWith("="));
    }

    [Fact]
    public void Sign_SignatureIs64Bytes()
    {
        var di = MakeDi(); di.EnsureKeypair();
        var sig = di.Sign("test");

        var decoded = FromBase64Url(sig);
        Assert.Equal(64, decoded.Length);
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

    // ─── DeviceId format ───────────────────────────────────────────────────────

    [Fact]
    public void DeviceId_IsLowercaseHex()
    {
        var di = MakeDi(); di.EnsureKeypair();

        Assert.Matches("^[a-f0-9]{64}$", di.DeviceId);
    }

    [Fact]
    public void DeviceId_IsSHA256OfPublicKey()
    {
        var di = MakeDi(); di.EnsureKeypair();

        // Manually verify: decode public key, SHA256 it, compare with DeviceId
        var pubKeyBytes = FromBase64Url(di.PublicKeyBase64);
        var expectedId = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pubKeyBytes)).ToLowerInvariant();
        Assert.Equal(expectedId, di.DeviceId);
    }

    // ─── Corrupt/missing key file ──────────────────────────────────────────────

    [Fact]
    public void EnsureKeypair_CorruptKeyFile_ThrowsFormatException()
    {
        var di = MakeDi();
        File.WriteAllText(Path.Combine(_testDir, "device.key"), "not-valid-base64!!!");

        Assert.Throws<System.FormatException>(() => di.EnsureKeypair());
    }

    [Fact]
    public void EnsureKeypair_TruncatedKeyFile_Throws()
    {
        var di = MakeDi();
        // Write a key that's too short (not 32 bytes for Ed25519 seed)
        var shortKey = Convert.ToBase64String(new byte[16]); // 16 bytes, not 32
        File.WriteAllText(Path.Combine(_testDir, "device.key"), shortKey);

        // Should throw because Ed25519PrivateKeyParameters expects 32-byte seed
        Assert.ThrowsAny<Exception>(() => di.EnsureKeypair());
    }

    [Fact]
    public void EnsureKeypair_EmptyKeyFile_Throws()
    {
        var di = MakeDi();
        File.WriteAllText(Path.Combine(_testDir, "device.key"), "");

        Assert.ThrowsAny<Exception>(() => di.EnsureKeypair());
    }

    // ─── BuildV3Payload ─────────────────────────────────────────────────────────

    [Fact]
    public void BuildV3Payload_FormatsCorrectly()
    {
        var di = MakeDi(); di.EnsureKeypair();

        var payload = di.BuildV3Payload(
            platform: "linux",
            deviceFamily: "desktop",
            clientId: "client-abc",
            clientMode: "ptt",
            role: "user",
            scopes: new[] { "gateway.connect", "audio.record" },
            signedAtMs: 1234567890000L,
            token: "tok",
            nonce: "non");

        Assert.Equal($"v3|{di.DeviceId}|client-abc|ptt|user|gateway.connect,audio.record|1234567890000|tok|non|linux|desktop", payload);
    }

    [Fact]
    public void BuildV3Payload_SingleScope_Works()
    {
        var di = MakeDi(); di.EnsureKeypair();

        var payload = di.BuildV3Payload(
            "linux", "desktop", "c", "ptt", "user",
            new[] { "only-one" },
            1000L, "t", "n");

        Assert.Contains("only-one", payload);
        Assert.DoesNotContain(",", payload.Split('|')[5]);
    }
}
