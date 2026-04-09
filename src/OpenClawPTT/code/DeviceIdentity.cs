using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Security;

namespace OpenClawPTT;

public sealed class DeviceIdentity
{
    private readonly string _keyPath;
    private byte[] _privateKeySeed = null!; // 32-byte Ed25519 seed

    public string DeviceId { get; private set; } = "";
    public string PublicKeyBase64 { get; private set; } = ""; // base64url, no padding

    public DeviceIdentity(string dataDir)
    {
        _keyPath = Path.Combine(dataDir, "device.key");
    }

    public void EnsureKeypair()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_keyPath)!);

        if (File.Exists(_keyPath))
        {
            _privateKeySeed = Convert.FromBase64String(File.ReadAllText(_keyPath).Trim());
        }
        else
        {
            // Generate a fresh 32-byte Ed25519 seed
            var gen = new Ed25519KeyPairGenerator();
            gen.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
            var pair = gen.GenerateKeyPair();
            var priv = (Ed25519PrivateKeyParameters)pair.Private;
            _privateKeySeed = priv.GetEncoded(); // 32-byte seed
            File.WriteAllText(_keyPath, Convert.ToBase64String(_privateKeySeed));
            if (!OperatingSystem.IsWindows())
            {
                try { File.SetUnixFileMode(_keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
                catch { }
            }
        }

        var privKey = new Ed25519PrivateKeyParameters(_privateKeySeed);
        var pubKey = privKey.GeneratePublicKey();
        var rawPub = pubKey.GetEncoded(); // raw 32 bytes

        // SHA-256 of raw public key bytes → hex device ID
        DeviceId = Convert.ToHexString(SHA256.HashData(rawPub)).ToLowerInvariant();

        // base64url, no padding — matches server's normalizeDevicePublicKeyBase64Url()
        PublicKeyBase64 = ToBase64Url(rawPub);
    }

    public string BuildV3Payload(
        string platform,
        string deviceFamily,
        string clientId,
        string clientMode,
        string role,
        IEnumerable<string> scopes,  // caller must pass in connect-params order
        long signedAtMs,
        string token,
        string nonce)
    {
        var scopesJoined = string.Join(",", scopes);  // no sorting — must match connectParams.scopes order
        return $"v3|{DeviceId}|{clientId}|{clientMode}|{role}|{scopesJoined}|{signedAtMs}|{token}|{nonce}|{platform}|{deviceFamily}";
    }

    /// <summary>Ed25519 sign, return base64url-encoded raw 64-byte signature.</summary>
    public string Sign(string payload)
    {
        var data = Encoding.UTF8.GetBytes(payload);
        var privKey = new Ed25519PrivateKeyParameters(_privateKeySeed);
        var signer = new Ed25519Signer();
        signer.Init(true, privKey);
        signer.BlockUpdate(data, 0, data.Length);
        var sig = signer.GenerateSignature(); // raw 64 bytes
        return ToBase64Url(sig);
    }

    public static string GetPlatform()
    {
        if (OperatingSystem.IsWindows()) return "windows";
        if (OperatingSystem.IsMacOS()) return "macos";
        return "linux";
    }

    private static string ToBase64Url(byte[] data) =>
        Convert.ToBase64String(data).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}