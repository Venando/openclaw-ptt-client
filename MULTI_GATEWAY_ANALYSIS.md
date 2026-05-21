# Multi-Gateway / Multi-Harness Support — Architectural Analysis

**Project**: openclaw-ptt-client  
**Branch**: `analysis/multi-gateway` (worktree)  
**Date**: 2026-05-21  
**Based on**: `main` @ `838c9b2` (post `feat/theme-builtins-palette` merge)

---

## 1. Current Architecture (Single Gateway)

### 1.1 Config Layer
- `AppConfig` has **one** `GatewayUrl : string` → `ws://localhost:18789`
- `AppConfig` has **one** `AuthToken`, **one** `DeviceToken`, **one** `TlsFingerprint`
- `FileConfigStorage` persists a single flat JSON object — no nested arrays for multiple connections
- `ConfigurationService` diffs a single `AppConfig` instance; `ConfigChangedEventArgs` is flat
- `HarnessConfigSection` wizard has a hardcoded dropdown: `["OpenClaw", "Nanobot (not supported)"]` — harness is a display string, not a runtime abstraction

### 1.2 Connection Layer
- `GatewayClient` → `GatewayConnectionLifecycle` → `ClientWebSocketAdapter`
- All three are **1:1 with a single WebSocket endpoint** (`_cfg.GatewayUrl`)
- `GatewayService` owns **exactly one** `IGatewayClient` (`_gatewayClient` field)
- `IGatewayService` exposes a single event surface (`Connected`, `Disconnected`, `AgentReplyDelta`, …)
- `GatewayReconnector` manages retries for **one** endpoint

### 1.3 Agent / Session Layer
- `AgentInfo` = `{ AgentId, Name, SessionKey, IsDefault }` — **no gateway/harness identifier**
- `AgentRegistry` (static singleton) holds a **flat `List<AgentInfo>`** and **one** `_activeSessionKey`
- `AgentRegistry.IsMessageForActiveSession(sessionKey)` — assumes all sessions live on the same gateway
- `AgentRegistry.SetActiveAgent(agentId)` — picks from the flat list; no gateway scoping
- `AgentRegistry.ActiveSessionKey` is a single global string used by `GatewayClient.SendTextAsync`

### 1.4 Application / DI Layer
- `ServiceFactory.CreateGatewayService(cfg, …)` creates **one** `GatewayClient` + **one** `GatewayService`
- `AppRunner.RunAppLoopAsync` creates **one** `gateway` instance and passes it into the entire PTT loop
- `TextMessageSender` wraps a **single** `IGatewayService`
- `CreateNamingPipeline` wires conversation naming to **one** gateway
- `HandleGatewayConfigChanged` recreates **one** gateway on URL/token change
- Status bar (`StatusService`, `ServiceKind.Gateway`) tracks **one** connection dot (Red/Yellow/Green)

### 1.5 UI / Commands Layer
- `/chat <agent>` switches `AgentRegistry.ActiveSessionKey` — no gateway context
- `/reconnect` (implied by config change) recreates the single gateway client
- `AgentStatusBottomPanel` renders a flat table of agents — no grouping by gateway
- `GatewayErrorClassifier` classifies errors for a single connection type
- No command exists to list, add, remove, or switch between gateways

---

## 2. What "Multi-Gateway" Actually Means

### Scenario A: Multiple OpenClaw Gateways
- Home gateway (`ws://192.168.1.50:18789`)
- Cloud gateway (`wss://claw.myserver.com`)
- Each has its own set of agents / sessions
- User wants to chat with agents on either gateway seamlessly

### Scenario B: Other Harnesses (ZeroClaw, Nanobot, etc.)
- Different wire protocol (not necessarily WebSocket + JSON-RPC)
- Different auth mechanism
- Different event shape
- May still expose "agents" and "sessions" conceptually

### Scenario C: Direct LLM as a Pseudo-Gateway
- Already partially exists (`DirectLlmService`)
- Could be unified under a "connection" abstraction so `/chat` works the same

---

## 3. Priority-Ordered Changes (Do in This Order)

### 🔴 P0 — Foundation: Config & Identity Model
> **Without these, nothing else can land.**

#### 3.1.1 Introduce `GatewayConnectionConfig` (new class)
```csharp
public sealed class GatewayConnectionConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");  // stable identity
    public string DisplayName { get; set; } = "Untitled";
    public string HarnessType { get; set; } = "openclaw";          // "openclaw", "zeroclaw", "direct-llm"
    public string GatewayUrl { get; set; } = "ws://localhost:18789";
    public string? AuthToken { get; set; }
    public string? DeviceToken { get; set; }
    public string? TlsFingerprint { get; set; }
    public bool IsDefault { get; set; } = false;
    public Dictionary<string, JsonElement> HarnessSpecific { get; set; } = new(); // extensibility
}
```

#### 3.1.2 Replace flat `GatewayUrl`/`AuthToken`/… in `AppConfig` with a collection
```csharp
public List<GatewayConnectionConfig> Gateways { get; set; } = new();

// Back-compat: single-gateway config migrates on first load
[Obsolete("Use Gateways collection")]
public string GatewayUrl { get; set; } = "ws://localhost:18789";
// … etc
```
- `FileConfigStorage` must preserve unrecognized fields → already does
- Add migration: if `Gateways` is empty but `GatewayUrl` is set, auto-create one `GatewayConnectionConfig`

#### 3.1.3 Add `GatewayConnectionId` to `AgentInfo`
```csharp
public sealed class AgentInfo
{
    public string AgentId { get; init; } = "";
    public string Name { get; init; } = "";
    public string SessionKey { get; init; } = "";
    public string GatewayConnectionId { get; init; } = "";   // NEW
    public bool IsDefault { get; init; }
}
```

#### 3.1.4 Update `ConfigurationService.Validate`
- At least one gateway must be configured
- Each gateway needs a valid URL (per harness rules)
- Duplicate `Id` values are an error

**Files to touch:**
- `Services/Config/AppConfig.cs`
- `Services/Config/ConfigurationService.cs`
- `Services/Config/IConfigStorage.cs` / `FileConfigStorage.cs` (migration logic)
- `Services/AgentInfo.cs`
- `Services/AgentSettings/AgentsConfig.cs` (maybe — if persisted agent settings need gateway scoping)

---

### 🟠 P1 — Abstraction: `IGatewayConnection` + `IGatewayHarness`
> **Introduce the harness plugin interface so new gateway types don't require core changes.**

#### 3.2.1 Introduce `IGatewayConnection` (connection handle)
```csharp
public interface IGatewayConnection : IDisposable
{
    string ConnectionId { get; }              // matches GatewayConnectionConfig.Id
    string DisplayName { get; }
    string HarnessType { get; }
    bool IsConnected { get; }

    // Same event surface as current IGatewayUIEvents, but scoped
    event Action? Connected;
    event Action? Disconnected;
    event Action? Reconnecting;
    event Action? ReconnectFailed;
    event Action<string>? AgentReplyFull;
    // … etc

    Task ConnectAsync(CancellationToken ct);
    Task DisconnectAsync(CancellationToken ct);
    Task<JsonElement> SendTextAsync(string body, CancellationToken ct);
    Task<JsonElement> SendAudioAsync(byte[] wavBytes, CancellationToken ct);
    Task<JsonElement> SendRpcAsync(string method, object? parameters, CancellationToken ct);
    Task<List<ChatHistoryEntry>?> FetchSessionHistoryAsync(string sessionKey, int limit = 5);
}
```

#### 3.2.2 Introduce `IGatewayHarness` (factory + protocol adapter)
```csharp
public interface IGatewayHarness
{
    string HarnessType { get; }
    IGatewayConnection CreateConnection(GatewayConnectionConfig config, IColorConsole console,
        IAgentActivityStore? activityStore = null);
    bool ValidateConfig(GatewayConnectionConfig config, out string error);
}
```

#### 3.2.3 Implement `OpenClawHarness : IGatewayHarness`
- This is basically a refactor of today's `GatewayClient` + `GatewayService` + `GatewayConnectionLifecycle`
- The existing `GatewayClient` already implements most of `IGatewayConnection` — rename/adapt
- `OpenClawHarness.CreateConnection` returns a `GatewayConnection` (rename of `GatewayClient`)

#### 3.2.4 Implement `DirectLlmHarness : IGatewayHarness` (optional but clean)
- Wraps `DirectLlmService` behind `IGatewayConnection`
- Makes `/chat` to a direct-LLM "agent" work the same as `/chat` to an OpenClaw agent

#### 3.2.5 Implement `ZeroClawHarness : IGatewayHarness` (stub)
- Exists to prove the abstraction works
- Throws `NotImplementedException` in methods until ZeroClaw protocol is defined

**Files to touch:**
- `Connection/IGatewayConnection.cs` (new)
- `Connection/IGatewayHarness.cs` (new)
- `Connection/Harnesses/OpenClawHarness.cs` (new, extracts from `GatewayService`)
- `Connection/Harnesses/DirectLlmHarness.cs` (new, optional)
- `Connection/GatewayClient.cs` → refactor to `Connection/OpenClawConnection.cs`
- `Connection/GatewayService.cs` → slim down or delete (logic moves to harness)

---

### 🟡 P2 — Registry: Multi-Connection Agent Registry
> **Enable the app to hold agents from multiple gateways simultaneously.**

#### 3.3.1 Replace static `AgentRegistry` with `IAgentRegistry`
```csharp
public interface IAgentRegistry
{
    IReadOnlyList<AgentInfo> AllAgents { get; }
    IReadOnlyList<AgentInfo> GetAgentsForGateway(string connectionId);

    string? ActiveSessionKey { get; }
    string? ActiveGatewayConnectionId { get; }   // NEW

    event Action<string?>? ActiveSessionChanged;
    event Action<string?>? ActiveGatewayChanged; // NEW

    bool SetActiveAgent(string agentId);        // can now cross gateways
    bool SetActiveSession(string sessionKey, string gatewayConnectionId); // NEW overload
    void RegisterAgents(string connectionId, IReadOnlyList<AgentInfo> agents);
    void ClearAgentsForGateway(string connectionId);
    bool IsMessageForActiveSession(string? sessionKey, string? gatewayConnectionId);
}
```

#### 3.3.2 Change `AgentRegistry` from `static` to instance
- Inject `IAgentRegistry` into `GatewayService` / `OpenClawConnection`
- `AppRunner` holds the single `IAgentRegistry` instance
- Update `AgentStatusBottomPanel` to receive `IAgentRegistry` instead of reading static state

**Files to touch:**
- `Services/AgentRegistry.cs` → major refactor
- `Services/IAgentRegistry.cs` (new)
- `Services/AgentStatus/AgentStatusBottomPanel.cs` (inject registry)
- `Connection/GatewayClient.cs` or `OpenClawConnection.cs` (inject registry)
- `Connection/GatewayConnectionLifecycle.cs` (snapshot processor needs registry)

---

### 🟢 P3 — Routing: Message Dispatch
> **Ensure text/audio messages route to the correct gateway.**

#### 3.4.1 Introduce `IGatewayRouter`
```csharp
public interface IGatewayRouter
{
    IGatewayConnection? GetConnectionForAgent(string agentId);
    IGatewayConnection? GetConnectionForSession(string sessionKey);
    IGatewayConnection? GetConnectionById(string connectionId);
    IReadOnlyList<IGatewayConnection> AllConnections { get; }
}
```

#### 3.4.2 Update `TextMessageSender`
```csharp
public class TextMessageSender : ITextMessageSender
{
    private readonly IGatewayRouter _router;
    private readonly IAgentRegistry _registry;

    public async Task SendAsync(string text, CancellationToken ct)
    {
        var activeAgent = _registry.GetActiveAgent();
        if (activeAgent == null) throw …;
        var conn = _router.GetConnectionForAgent(activeAgent.AgentId)
                  ?? _router.GetConnectionForSession(activeAgent.SessionKey);
        if (conn == null) throw …;
        await conn.SendTextAsync(text, ct);
    }
}
```

#### 3.4.3 Update `AppRunner.RunAppLoopAsync`
- Instead of `using var gateway = _factory.CreateGatewayService(cfg, …)`
- Do:
  ```csharp
  var harnessRegistry = _factory.CreateHarnessRegistry();
  harnessRegistry.Register(new OpenClawHarness());
  harnessRegistry.Register(new DirectLlmHarness());

  var connectionManager = _factory.CreateConnectionManager(cfg.Gateways, harnessRegistry);
  await connectionManager.ConnectAllAsync(ct);
  // connectionManager implements IGatewayRouter
  ```

**Files to touch:**
- `Services/IGatewayRouter.cs` (new)
- `Services/GatewayRouter.cs` or `Connection/ConnectionManager.cs` (new)
- `Services/TextMessageSender.cs`
- `AppRunner.cs` (loop setup)
- `AppRunner.ConfigSetup.cs` (config change handler: `HandleGatewayConfigChanged` now handles multiple)

---

### 🔵 P4 — UI: Multi-Connection Status & Commands
> **Surface multiple connections in the UI.**

#### 3.5.1 Status bar changes
- `ServiceKind.Gateway` dot becomes per-connection
- `StatusService` tracks `Dictionary<string, StatusColor>` keyed by `ConnectionId`
- Top separator shows active gateway name next to active agent name
- Bottom panel groups agents under gateway headers

#### 3.5.2 New / updated commands
| Command | Behavior |
|---------|----------|
| `/gateway list` | Show all configured gateways, their harness, URL, status |
| `/gateway add` | Wizard to add a new gateway (reuses config section) |
| `/gateway remove <id>` | Remove a gateway from config |
| `/gateway connect <id>` | Force-connect a disconnected gateway |
| `/gateway disconnect <id>` | Disconnect a gateway |
| `/gateway switch <id>` | Switch default gateway |
| `/chat <agent>` | Already exists — now implicitly routes to correct gateway |
| `/chat <agent> on <gateway>` | Explicit gateway targeting (optional) |

#### 3.5.3 `HarnessConfigSection` → `GatewayConfigWizard`
- Replaces the single harness step with a multi-gateway management flow
- Each gateway gets its own config section within the wizard
- Harness selection per-gateway, not global

**Files to touch:**
- `Services/Status/StatusService.cs`
- `Services/Status/ServiceKind.cs` (maybe add per-connection kinds)
- `Services/AgentStatus/AgentStatusBottomPanel.cs` (group by gateway)
- `Services/Commands/Native/ReconnectCommand.cs` (targeted reconnect)
- `Services/Commands/Native/AppStatusCommand.cs` (show all gateways)
- `Services/Config/Wizard/HarnessConfigSection.cs` → major refactor
- `Services/Commands/` (new gateway commands)

---

### 🟣 P5 — Polish: Backward Compatibility & Migration
> **Don't break existing single-gateway users.**

#### 3.6.1 Config migration on load
```csharp
// In FileConfigStorage.Load() or ConfigurationService.Load()
if (cfg.Gateways.Count == 0 && !string.IsNullOrWhiteSpace(cfg.GatewayUrl))
{
    cfg.Gateways.Add(new GatewayConnectionConfig
    {
        Id = "default",
        DisplayName = "Default",
        HarnessType = "openclaw",
        GatewayUrl = cfg.GatewayUrl,
        AuthToken = cfg.AuthToken,
        DeviceToken = cfg.DeviceToken,
        TlsFingerprint = cfg.TlsFingerprint,
        IsDefault = true,
    });
    cfg.GatewayUrl = ""; // clear legacy
}
```

#### 3.6.2 `AgentRegistry` static accessor deprecation
- Keep `AgentRegistry.ActiveSessionKey` as a forwarding property to the instance
- Mark static members `[Obsolete]` with messages pointing to `IAgentRegistry`
- Remove in a future major version

#### 3.6.3 Event source migration
- `IGatewayEventSource` already exists — ensure new `IGatewayConnection` can expose it
- `GatewayEventDispatcher` already handles events — ensure it works per-connection

**Files to touch:**
- `Services/Config/FileConfigStorage.cs` (migration)
- `Services/AgentRegistry.cs` (backward-compat forwarding)
- `Connection/GatewayEventDispatcher.cs` (ensure per-connection scoping)

---

## 4. Concrete File Checklist (in dependency order)

| # | File | Action | Priority |
|---|------|--------|----------|
| 1 | `Services/Config/AppConfig.cs` | Add `GatewayConnectionConfig`, `List<GatewayConnectionConfig> Gateways`, mark old props `[Obsolete]` | P0 |
| 2 | `Services/Config/ConfigurationService.cs` | Validate `Gateways` collection, update `ComputeChangedProperties` | P0 |
| 3 | `Services/Config/FileConfigStorage.cs` | Add migration from legacy single-gateway fields | P0 |
| 4 | `Services/AgentInfo.cs` | Add `GatewayConnectionId` | P0 |
| 5 | `Connection/IGatewayConnection.cs` | **New file** — extract connection surface from `IGatewayClient` | P1 |
| 6 | `Connection/IGatewayHarness.cs` | **New file** — harness plugin factory | P1 |
| 7 | `Connection/Harnesses/OpenClawHarness.cs` | **New file** — refactor existing `GatewayClient` + lifecycle into harness | P1 |
| 8 | `Connection/GatewayClient.cs` | Refactor to implement `IGatewayConnection` (or rename to `OpenClawConnection`) | P1 |
| 9 | `Connection/GatewayService.cs` | Slim down — most logic moves to harness/connection; or delete | P1 |
| 10 | `Services/IAgentRegistry.cs` | **New file** — replace static `AgentRegistry` | P2 |
| 11 | `Services/AgentRegistry.cs` | Refactor from static to instance, add gateway scoping | P2 |
| 12 | `Services/IGatewayRouter.cs` | **New file** — routing abstraction | P3 |
| 13 | `Services/GatewayRouter.cs` | **New file** — default router implementation | P3 |
| 14 | `Services/TextMessageSender.cs` | Use `IGatewayRouter` + `IAgentRegistry` instead of single `IGatewayService` | P3 |
| 15 | `AppRunner.cs` | Create `ConnectionManager`, connect all gateways, pass router into pipeline | P3 |
| 16 | `AppRunner.ConfigSetup.cs` | `HandleGatewayConfigChanged` now recreates the right connection | P3 |
| 17 | `Services/ServiceFactory.cs` / `IServiceFactory.cs` | Add `CreateConnectionManager`, `CreateHarnessRegistry`, etc. | P3 |
| 18 | `Services/Status/StatusService.cs` | Track per-connection status | P4 |
| 19 | `Services/AgentStatus/AgentStatusBottomPanel.cs` | Group agents by gateway | P4 |
| 20 | `Services/Commands/Native/ReconnectCommand.cs` | Target specific gateway | P4 |
| 21 | `Services/Config/Wizard/HarnessConfigSection.cs` | Multi-gateway wizard | P4 |
| 22 | `Services/Commands/` (new) | `/gateway *` commands | P4 |

---

## 5. What NOT to Change (Yet)

- **Audio recording / STT pipeline** — stays single-instance; user speaks once, audio routes to active agent's gateway
- **TTS pipeline** — stays single-instance; responses from any gateway are spoken the same way
- **Tool display system** — `ToolDisplayHandler`, renderers, `AgentOutputCoordinator` are gateway-agnostic
- **Theme system** — `ThemeConfig` is global; no per-gateway theming needed
- **StreamShell host** — single console output; multiple gateways multiplex into the same shell
- **Direct LLM service** — already separate; just wrap it in a harness later
- **Test mode / mock infrastructure** — update mocks after real interfaces stabilize

---

## 6. Open Questions for Ven

1. **Harness protocol diversity**: Is ZeroClaw actually WebSocket+JSON-RPC like OpenClaw, or a completely different protocol? This determines whether `IGatewayConnection` is generic enough or needs per-harness method extensions.

2. **Simultaneous connections**: Should all gateways be connected eagerly at startup, or connect-on-demand when an agent on that gateway is activated?

3. **Agent namespace collision**: If two gateways both have an agent named "coder", do we disambiguate by gateway prefix (`cloud/coder` vs `home/coder`), or require unique `AgentId` across all gateways?

4. **Cross-gateway operations**: Should `/crew` (agent settings) be per-gateway or global? Should conversation history span gateways?

5. **Session key uniqueness**: Are session keys globally unique across gateways? If not, `IGatewayRouter.GetConnectionForSession` needs gateway qualification.

6. **Auth strategy**: Is auth per-gateway (each has its own token), or is there a single identity (DeviceToken) shared across all gateways?

---

## 7. Suggested First Commit

> A minimal, reviewable PR that lays the foundation without breaking anything.

**Branch**: `feat/multi-gateway-config`
**Scope**: P0 only (config model + migration)
**Files**: `AppConfig.cs`, `ConfigurationService.cs`, `FileConfigStorage.cs`, `AgentInfo.cs`
**What it does**:
1. Introduces `GatewayConnectionConfig`
2. Adds `Gateways` list to `AppConfig`
3. Marks legacy `GatewayUrl`/`AuthToken`/… `[Obsolete]`
4. Adds config migration: legacy fields → first `Gateways` entry
5. Adds `GatewayConnectionId` to `AgentInfo`
6. All existing tests still pass (single-gateway behavior unchanged)
7. No runtime behavior change yet — purely data-model + migration

**Why this first**: It gives the codebase a stable data model that all subsequent PRs can build on. Reviewers can validate migration logic without being overwhelmed by connection-layer changes.

---

*End of analysis.*
