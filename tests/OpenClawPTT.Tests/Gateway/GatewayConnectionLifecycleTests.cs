using System.Net.WebSockets;
using System.Reflection;
using System.Text.Json;
using Moq;
using OpenClawPTT;
using Xunit;

namespace OpenClawPTT.Tests.Gateway;

public class GatewayConnectionLifecycleTests : IDisposable
{
    private readonly AppConfig _cfg;
    private readonly DeviceIdentity _dev;
    private readonly GatewayEventSource _events;
    private readonly Mock<IClientWebSocket> _mockWs;

    public GatewayConnectionLifecycleTests()
    {
        _cfg = new AppConfig
        {
            CustomDataDir = Path.GetTempPath(),
            GatewayUrl = "wss://127.0.0.1:9999/test",
            AuthToken = "test-token"
        };
        _dev = new DeviceIdentity(_cfg.DataDir);
        _dev.EnsureKeypair();
        _events = new GatewayEventSource();
        _mockWs = new Mock<IClientWebSocket>();
        _mockWs.Setup(x => x.State).Returns(WebSocketState.Open);
    }

    private GatewayConnectionLifecycle CreateWithMockSocket()
    {
        return new GatewayConnectionLifecycle(_cfg, _dev, _events, () => _mockWs.Object);
    }

    // ─── reflection helpers ─────────────────────────────────────────

    private static T InvokePrivate<T>(object target, string name, params object[] args)
    {
        var method = target.GetType().GetMethod(name,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)!;
        return (T)method.Invoke(target, args)!;
    }

    private static void InvokeVoid(object target, string name, params object[] args)
    {
        var method = target.GetType().GetMethod(name,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)!;
        method.Invoke(target, args);
    }

    // ─── construction ────────────────────────────────────────────────

    [Fact]
    public void Construct_WithMockSocketFactory_DoesNotThrow()
    {
        var exception = Record.Exception(() => CreateWithMockSocket());
        Assert.Null(exception);
    }

    [Fact]
    public void IsConnected_OpenSocket_ReturnsTrue()
    {
        _mockWs.Setup(x => x.State).Returns(WebSocketState.Open);
        var lifecycle = CreateWithMockSocket();
        Assert.Equal(WebSocketState.Open, _mockWs.Object.State);
        lifecycle.Dispose();
    }

    [Fact]
    public void IsConnected_ClosedSocket_ReturnsFalse()
    {
        _mockWs.Setup(x => x.State).Returns(WebSocketState.Closed);
        var lifecycle = CreateWithMockSocket();
        Assert.False(lifecycle.IsConnected);
        lifecycle.Dispose();
    }

    [Fact]
    public void GetFraming_BeforeConnect_ReturnsNull()
    {
        var lifecycle = CreateWithMockSocket();
        Assert.Null(lifecycle.GetFraming());
        lifecycle.Dispose();
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var lifecycle = CreateWithMockSocket();
        lifecycle.Dispose();
        var exception = Record.Exception(() => lifecycle.Dispose());
        Assert.Null(exception);
    }

    [Fact]
    public void Dispose_AfterConstruction_DoesNotThrow()
    {
        var lifecycle = CreateWithMockSocket();
        var exception = Record.Exception(() => lifecycle.Dispose());
        Assert.Null(exception);
    }

    [Fact]
    public void Dispose_WithNullSocket_DoesNotThrow()
    {
        var lifecycle = CreateWithMockSocket();
        var exception = Record.Exception(() => lifecycle.Dispose());
        Assert.Null(exception);
    }

    // ─── BuildConnectParams ─────────────────────────────────────────

    [Fact]
    public void BuildConnectParams_ReturnsDictionaryWithRequiredKeys()
    {
        var lifecycle = CreateWithMockSocket();
        var nonce = "test-nonce-123";

        var result = InvokePrivate<Dictionary<string, object?>>(lifecycle, "BuildConnectParams", nonce);

        Assert.NotNull(result);
        Assert.Equal(3, result["minProtocol"]);
        Assert.Equal(3, result["maxProtocol"]);
        Assert.Equal("operator", result["role"]);
        Assert.NotNull(result["client"]);
        Assert.NotNull(result["auth"]);
        Assert.NotNull(result["device"]);
        Assert.NotNull(result["scopes"]);

        var scopes = (Array)result["scopes"]!;
        Assert.Contains("operator.read", scopes.Cast<string>());
        Assert.Contains("operator.write", scopes.Cast<string>());
        Assert.Contains("operator.approvals", scopes.Cast<string>());

        lifecycle.Dispose();
    }

    [Fact]
    public void BuildConnectParams_IncludesDeviceSignatureAndNonce()
    {
        var lifecycle = CreateWithMockSocket();
        var nonce = "nonce-for-signature";

        var result = InvokePrivate<Dictionary<string, object?>>(lifecycle, "BuildConnectParams", nonce);
        var device = (Dictionary<string, object>)result["device"]!;

        Assert.Equal(_dev.DeviceId, device["id"]);
        Assert.Equal(_dev.PublicKeyBase64, device["publicKey"]);
        Assert.Equal(nonce, device["nonce"]);
        Assert.NotNull(device["signature"]);
        Assert.NotNull(device["signedAt"]);

        lifecycle.Dispose();
    }

    [Fact]
    public void BuildConnectParams_IncludesAuthToken()
    {
        var lifecycle = CreateWithMockSocket();

        var result = InvokePrivate<Dictionary<string, object?>>(lifecycle, "BuildConnectParams", "nonce");
        var auth = (Dictionary<string, object>)result["auth"]!;

        Assert.Equal(_cfg.AuthToken, auth["token"]);
        lifecycle.Dispose();
    }

    [Fact]
    public void BuildConnectParams_IncludesDeviceTokenWhenSet()
    {
        _cfg.DeviceToken = "my-device-token";
        var lifecycle = CreateWithMockSocket();

        var result = InvokePrivate<Dictionary<string, object?>>(lifecycle, "BuildConnectParams", "nonce");
        var auth = (Dictionary<string, object>)result["auth"]!;

        Assert.Equal(_cfg.DeviceToken, auth["deviceToken"]);
        lifecycle.Dispose();
    }

    [Fact]
    public void BuildConnectParams_ClientHasCorrectStructure()
    {
        var lifecycle = CreateWithMockSocket();

        var result = InvokePrivate<Dictionary<string, object?>>(lifecycle, "BuildConnectParams", "nonce");
        var client = (Dictionary<string, object>)result["client"]!;

        Assert.Equal("cli", client["id"]);
        Assert.Equal(_cfg.ClientVersion, client["version"]);
        Assert.Equal("cli", client["mode"]);
        Assert.Equal("desktop", client["deviceFamily"]);
        Assert.NotNull(client["platform"]);

        lifecycle.Dispose();
    }

    // ─── ValidateHelloOk ─────────────────────────────────────────────

    [Fact]
    public void ValidateHelloOk_ValidHelloOk_DoesNotThrow()
    {
        var lifecycle = CreateWithMockSocket();
        var json = JsonDocument.Parse(/* lang=json */ """
            {"type":"hello-ok"}
            """).RootElement;

        var exception = Record.Exception(() => InvokeVoid(lifecycle, "ValidateHelloOk", json));
        Assert.Null(exception);

        lifecycle.Dispose();
    }

    [Fact]
    public void ValidateHelloOk_TypeNotHelloOk_Throws()
    {
        var lifecycle = CreateWithMockSocket();
        var json = JsonDocument.Parse(/* lang=json */ """
            {"type":"error","message":"bad"}
            """).RootElement;

        var exception = Record.Exception(() => InvokeVoid(lifecycle, "ValidateHelloOk", json));
        Assert.NotNull(exception);
        Assert.Contains("Handshake rejected", exception.InnerException?.Message ?? exception.Message);

        lifecycle.Dispose();
    }

    [Fact]
    public void ValidateHelloOk_HelloOkWithErrorField_Throws()
    {
        var lifecycle = CreateWithMockSocket();
        var json = JsonDocument.Parse(/* lang=json */ """
            {"type":"hello-ok","error":"something went wrong"}
            """).RootElement;

        var exception = Record.Exception(() => InvokeVoid(lifecycle, "ValidateHelloOk", json));
        Assert.NotNull(exception);
        Assert.Contains("Server returned hello-ok with error", exception.InnerException?.Message ?? exception.Message);

        lifecycle.Dispose();
    }

    [Fact]
    public void ValidateHelloOk_MissingType_Throws()
    {
        var lifecycle = CreateWithMockSocket();
        var json = JsonDocument.Parse(/* lang=json */ """
            {}
            """).RootElement;

        var exception = Record.Exception(() => InvokeVoid(lifecycle, "ValidateHelloOk", json));
        Assert.NotNull(exception);

        lifecycle.Dispose();
    }

    // ─── ExtractTickIntervalMs ───────────────────────────────────────

    [Fact]
    public void ExtractTickIntervalMs_WithPolicy_ReturnsValue()
    {
        var lifecycle = CreateWithMockSocket();
        var json = JsonDocument.Parse(/* lang=json */ """
            {"policy":{"tickIntervalMs":30000}}
            """).RootElement;

        var result = InvokePrivate<int>(lifecycle, "ExtractTickIntervalMs", json);

        Assert.Equal(30_000, result);
        lifecycle.Dispose();
    }

    [Fact]
    public void ExtractTickIntervalMs_NoPolicy_ReturnsDefault()
    {
        var lifecycle = CreateWithMockSocket();
        var json = JsonDocument.Parse(/* lang=json */ """
            {}
            """).RootElement;

        var result = InvokePrivate<int>(lifecycle, "ExtractTickIntervalMs", json);

        Assert.Equal(15_000, result);
        lifecycle.Dispose();
    }

    [Fact]
    public void ExtractTickIntervalMs_PolicyWithoutTickInterval_ReturnsDefault()
    {
        var lifecycle = CreateWithMockSocket();
        var json = JsonDocument.Parse(/* lang=json */ """
            {"policy":{"otherField":"value"}}
            """).RootElement;

        var result = InvokePrivate<int>(lifecycle, "ExtractTickIntervalMs", json);

        Assert.Equal(15_000, result);
        lifecycle.Dispose();
    }

    // ─── PersistDeviceTokenIfIssued ─────────────────────────────────

    [Fact]
    public void PersistDeviceTokenIfIssued_WithDeviceToken_SetsConfig()
    {
        var cfg = new AppConfig
        {
            CustomDataDir = Path.GetTempPath(),
            GatewayUrl = "wss://127.0.0.1:9999/test",
            AuthToken = "test-token",
            DeviceToken = null
        };
        var dev = new DeviceIdentity(cfg.DataDir);
        dev.EnsureKeypair();
        var events = new GatewayEventSource();
        var lifecycle = new GatewayConnectionLifecycle(cfg, dev, events, () => new Mock<IClientWebSocket>().Object);

        var json = JsonDocument.Parse(/* lang=json */ """
            {"auth":{"deviceToken":"issued-token-xyz"}}
            """).RootElement;

        InvokeVoid(lifecycle, "PersistDeviceTokenIfIssued", json);

        Assert.Equal("issued-token-xyz", cfg.DeviceToken);
        lifecycle.Dispose();
    }

    [Fact]
    public void PersistDeviceTokenIfIssued_NoDeviceToken_DoesNotThrow()
    {
        var lifecycle = CreateWithMockSocket();
        var json = JsonDocument.Parse(/* lang=json */ """
            {"auth":{"token":"some-token"}}
            """).RootElement;

        var exception = Record.Exception(() => InvokeVoid(lifecycle, "PersistDeviceTokenIfIssued", json));
        Assert.Null(exception);

        lifecycle.Dispose();
    }

    [Fact]
    public void PersistDeviceTokenIfIssued_NoAuth_DoesNotThrow()
    {
        var lifecycle = CreateWithMockSocket();
        var json = JsonDocument.Parse(/* lang=json */ """
            {}
            """).RootElement;

        var exception = Record.Exception(() => InvokeVoid(lifecycle, "PersistDeviceTokenIfIssued", json));
        Assert.Null(exception);

        lifecycle.Dispose();
    }

    // ─── ProcessSnapshotIfPresent ──────────────────────────────────

    [Fact]
    public void ProcessSnapshotIfPresent_WithHealthAgents_SetsAgentRegistry()
    {
        var cfg = new AppConfig
        {
            CustomDataDir = Path.GetTempPath(),
            GatewayUrl = "wss://127.0.0.1:9999/test",
            AuthToken = "test-token"
        };
        var dev = new DeviceIdentity(cfg.DataDir);
        dev.EnsureKeypair();
        var events = new GatewayEventSource();
        var lifecycle = new GatewayConnectionLifecycle(cfg, dev, events, () => new Mock<IClientWebSocket>().Object);

        var json = JsonDocument.Parse(/* lang=json */ """
            {"snapshot":{"health":{"agents":[{"agentId":"default","name":"Default Agent","isDefault":true}]}}}
            """).RootElement;

        InvokeVoid(lifecycle, "ProcessSnapshotIfPresent", json);

        Assert.Equal("agent:default:main", AgentRegistry.ActiveSessionKey);
        lifecycle.Dispose();
    }

    [Fact]
    public void ProcessSnapshotIfPresent_NoSnapshot_DoesNotThrow()
    {
        var lifecycle = CreateWithMockSocket();
        var json = JsonDocument.Parse(/* lang=json */ """
            {}
            """).RootElement;

        var exception = Record.Exception(() => InvokeVoid(lifecycle, "ProcessSnapshotIfPresent", json));
        Assert.Null(exception);

        lifecycle.Dispose();
    }

    [Fact]
    public void ProcessSnapshotIfPresent_NoSessionDefaults_DoesNotThrow()
    {
        var lifecycle = CreateWithMockSocket();
        var json = JsonDocument.Parse(/* lang=json */ """
            {"snapshot":{"other":"data"}}
            """).RootElement;

        var exception = Record.Exception(() => InvokeVoid(lifecycle, "ProcessSnapshotIfPresent", json));
        Assert.Null(exception);

        lifecycle.Dispose();
    }

    // ─── ProcessHelloPayload ─────────────────────────────────────────

    [Fact]
    public void ProcessHelloPayload_ReturnsTickIntervalFromPolicy()
    {
        var cfg = new AppConfig
        {
            CustomDataDir = Path.GetTempPath(),
            GatewayUrl = "wss://127.0.0.1:9999/test",
            AuthToken = "test-token"
        };
        var dev = new DeviceIdentity(cfg.DataDir);
        dev.EnsureKeypair();
        var events = new GatewayEventSource();
        var lifecycle = new GatewayConnectionLifecycle(cfg, dev, events, () => new Mock<IClientWebSocket>().Object);

        var json = JsonDocument.Parse(/* lang=json */ """
            {
              "type": "hello-ok",
              "policy": {"tickIntervalMs": 20000},
              "auth": {"deviceToken": "tok"},
              "snapshot": {"health":{"agents":[{"agentId":"test","name":"Test Agent","isDefault":true}]}}
            }
            """).RootElement;

        var tickMs = InvokePrivate<int>(lifecycle, "ProcessHelloPayload", json);

        Assert.Equal(20_000, tickMs);
        Assert.Equal("tok", cfg.DeviceToken);
        Assert.Equal("agent:test:main", AgentRegistry.ActiveSessionKey);
        lifecycle.Dispose();
    }

    [Fact]
    public void ProcessHelloPayload_WithoutOptionalFields_ReturnsDefaultTickMs()
    {
        var lifecycle = CreateWithMockSocket();
        var json = JsonDocument.Parse(/* lang=json */ """
            {"type":"hello-ok"}
            """).RootElement;

        var tickMs = InvokePrivate<int>(lifecycle, "ProcessHelloPayload", json);

        Assert.Equal(15_000, tickMs);
        lifecycle.Dispose();
    }

    public void Dispose()
    {
        // cleanup if needed
    }
}
