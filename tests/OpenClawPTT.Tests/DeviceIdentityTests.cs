using Xunit;

namespace OpenClawPTT.Tests;

public class DeviceIdentityTests
{
    private readonly string _testDir;

    public DeviceIdentityTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"oc_devid_{Guid.NewGuid():N}");
    }

    [Fact]
    public void EnsureKeypair_CreatesKeyFiles()
    {
        Directory.CreateDirectory(_testDir);
        var di = new DeviceIdentity(_testDir);

        di.EnsureKeypair();

        var keyPath = Path.Combine(_testDir, "device.key");
        Assert.True(File.Exists(keyPath));
        Assert.False(string.IsNullOrEmpty(di.DeviceId));
        Assert.False(string.IsNullOrEmpty(di.PublicKeyBase64));
    }

    [Fact]
    public void DeviceId_IsConsistentAcrossCalls()
    {
        Directory.CreateDirectory(_testDir);
        var di = new DeviceIdentity(_testDir);

        di.EnsureKeypair();
        var id1 = di.DeviceId;

        // Create another instance pointing to same dir — should read same key
        var di2 = new DeviceIdentity(_testDir);
        di2.EnsureKeypair();

        Assert.Equal(id1, di2.DeviceId);
    }

    [Fact]
    public void Sign_ProducesDeterministicOutput()
    {
        Directory.CreateDirectory(_testDir);
        var di = new DeviceIdentity(_testDir);
        di.EnsureKeypair();

        var payload = "test payload for signing";
        var sig1 = di.Sign(payload);
        var sig2 = di.Sign(payload);

        Assert.Equal(sig1, sig2);
    }

    [Fact]
    public void Sign_ProducesDifferentOutputForDifferentPayloads()
    {
        Directory.CreateDirectory(_testDir);
        var di = new DeviceIdentity(_testDir);
        di.EnsureKeypair();

        var sig1 = di.Sign("payload one");
        var sig2 = di.Sign("payload two");

        Assert.NotEqual(sig1, sig2);
    }
}
