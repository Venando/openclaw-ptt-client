# GatewayClient Refactor — Split Plan

> **For agentic workers:** Use `superpowers:subagent-driven-development` to implement this plan task-by-task. Each task produces a self-contained, compilable change.

## Goal

Split `GatewayClient` (~700 lines) into focused, single-responsibility classes with constructor-injected dependencies and zero shared mutable state between components. Behavior is preserved verbatim.

## Architecture

```
GatewayClient (coordinator / thin facade)
├── ConnectionLifecycle    — WebSocket lifecycle, connect/disconnect, reconnect loop
├── AuthHandler            — handshake: nonce → sign → connect params → hello-ok
├── MessageFraming         — send/receive framing, request/response tracking, IDs
├── KeepaliveRunner        — tick loop (talks to MessageFraming only)
├── SessionMessageHandler — session.message parsing, block routing, events
└── GatewayEventSource    — holds all Action<> events (returned to callers)

Interfaces:
- IClientWebSocket    — already exists (PR #37), injected into ConnectionLifecycle + MessageFraming
- ISender             — SendRequestAsync only, provided by MessageFraming to KeepaliveRunner
- IGatewayEventSource — all events, implemented by GatewayEventSource
```

## Dependency Flow

```
GatewayClient (constructs all, wires all)
    ↓
    ├── ConnectionLifecycle takes IClientWebSocket
    ├── AuthHandler takes DeviceIdentity, AppConfig, ConsoleUi
    ├── MessageFraming takes IClientWebSocket
    ├── KeepaliveRunner takes ISender (from MessageFraming)
    ├── SessionMessageHandler takes AppConfig
    └── GatewayEventSource created externally, passed in via constructor
```

## Key Design Decisions

1. **Socket trapped in framing layer** — `IClientWebSocket` injected into `MessageFraming` only. `KeepaliveRunner` gets `ISender` (SendRequestAsync-only). `ConnectionLifecycle` gets its own `IClientWebSocket` for connect/disconnect.
2. **Events on separate class** — `GatewayEventSource` implements `IGatewayEventSource` with all `Action<...>` events. Injected into `GatewayClient` constructor. `SessionMessageHandler` gets `IGatewayEventSource` injected.
3. **Constructor injection throughout** — every component receives its dependencies via constructor.
4. **No shared mutable state** — each class owns its own fields, no cross-class mutating.
5. **ISender interface** — `MessageFraming` exposes `ISender` alongside its own interface (or as a sub-interface). `KeepaliveRunner` only sees `SendRequestAsync`.

---

## Tasks

### Task 1: Extract `GatewayEventSource` and `IGatewayEventSource`

**Files:**
- Create: `src/OpenClawPTT/code/Connection/GatewayEventSource.cs`
- Modify: `src/OpenClawPTT/code/Connection/IGatewayClient.cs` — remove event declarations from `IGatewayClient`
- Create: `src/OpenClawPTT/code/Connection/IGatewayEventSource.cs`

- [ ] **Step 1: Create `IGatewayEventSource.cs`**

```csharp
using System;
using System.Text.Json;

namespace OpenClawPTT;

public interface IGatewayEventSource
{
    event Action<string, JsonElement>? EventReceived;
    event Action<string>? AgentReplyFull;
    event Action<string>? AgentReplyDelta;
    event Action? AgentReplyDeltaStart;
    event Action? AgentReplyDeltaEnd;
    event Action<string>? AgentThinking;
    event Action<string, string>? AgentToolCall;
    event Action<string>? AgentReplyAudio;
}
```

- [ ] **Step 2: Create `GatewayEventSource.cs`**

```csharp
using System;
using System.Text.Json;

namespace OpenClawPTT;

public sealed class GatewayEventSource : IGatewayEventSource
{
    public event Action<string, JsonElement>? EventReceived;
    public event Action<string>? AgentReplyFull;
    public event Action<string>? AgentReplyDelta;
    public event Action? AgentReplyDeltaStart;
    public event Action? AgentReplyDeltaEnd;
    public event Action<string>? AgentThinking;
    public event Action<string, string>? AgentToolCall;
    public event Action<string>? AgentReplyAudio;
}
```

- [ ] **Step 3: Update `IGatewayClient.cs`**
Remove all `event Action<...>` declarations from `IGatewayClient`. Add `IGatewayEventSource? GetEventSource()` method.

```csharp
    // Remove all event declarations from IGatewayClient interface
    // Add:
    IGatewayEventSource? GetEventSource();
```

- [ ] **Step 4: Build to verify**
Run: `dotnet build --configuration Debug`
Expected: PASS (no new errors; removed events from interface will break any implementors that expect them — those are handled in later tasks)

- [ ] **Step 5: Commit**
```bash
git add src/OpenClawPTT/code/Connection/IGatewayEventSource.cs src/OpenClawPTT/code/Connection/GatewayEventSource.cs src/OpenClawPTT/code/Connection/IGatewayClient.cs
git commit -m "refactor(gateway): extract IGatewayEventSource and GatewayEventSource"
```

---

### Task 2: Extract `ISender` and `MessageFraming`

**Files:**
- Create: `src/OpenClawPTT/code/Connection/ISender.cs`
- Create: `src/OpenClawPTT/code/Connection/MessageFraming.cs`

- [ ] **Step 1: Create `ISender.cs`**

```csharp
using System;
using System.Text.Json;
using System.Threading;

namespace OpenClawPTT;

public interface ISender
{
    Task<JsonElement> SendRequestAsync(string method, object? parameters, CancellationToken ct, TimeSpan? timeout = null);
}
```

- [ ] **Step 2: Create `MessageFraming.cs`**

Extract from `GatewayClient`:
- All `_pending` and `_eventWaiters` fields
- `_idCounter`
- `NextId()`
- `SendRequestAsync()`
- `WaitForEventAsync()`

Constructor: `MessageFraming(IClientWebSocket ws, AppConfig cfg)`

Expose `ISender` implementation via `GetSender()` method.

- [ ] **Step 3: Build to verify**
Run: `dotnet build --configuration Debug`
Expected: PASS

- [ ] **Step 4: Commit**
```bash
git add src/OpenClawPTT/code/Connection/ISender.cs src/OpenClawPTT/code/Connection/MessageFraming.cs
git commit -m "refactor(gateway): extract MessageFraming and ISender"
```

---

### Task 3: Extract `AuthHandler`

**Files:**
- Create: `src/OpenClawPTT/code/Connection/AuthHandler.cs`

- [ ] **Step 1: Create `AuthHandler.cs`**

Extract from `ConnectAsync`:
- Handshake logic (wait for `connect.challenge`, build signed payload, send `connect`, validate `hello-ok`)
- `LogMessage()` helper
- `Redact()` helper
- All auth dict building (`scopes`, `client`, `device`, `auth`)

Constructor: `AuthHandler(DeviceIdentity dev, AppConfig cfg, ConsoleUi consoleUi)`

Methods:
- `Task<JsonElement> AuthenticateAsync(IClientWebSocket ws, CancellationToken ct)` — takes socket directly since it's the one connected in `ConnectAsync`
- `Task WaitForChallengeAsync(IClientWebSocket ws, CancellationToken ct)` — returns nonce string
- `Task<JsonElement> SendConnectAsync(IClientWebSocket ws, string nonce, CancellationToken ct)` — sends connect, returns hello

- [ ] **Step 2: Build to verify**
Run: `dotnet build --configuration Debug`
Expected: PASS

- [ ] **Step 3: Commit**
```bash
git add src/OpenClawPTT/code/Connection/AuthHandler.cs
git commit -m "refactor(gateway): extract AuthHandler"
```

---

### Task 4: Extract `KeepaliveRunner`

**Files:**
- Create: `src/OpenClawPTT/code/Connection/KeepaliveRunner.cs`

- [ ] **Step 1: Create `KeepaliveRunner.cs`**

Extract from `GatewayClient`:
- `_tickCts` field
- `StartKeepalive()` method logic

Constructor: `KeepaliveRunner(ISender sender, AppConfig cfg)`

Takes `ISender` (not `IClientWebSocket`) — can only call `SendRequestAsync`.

Methods:
- `void Start(int intervalMs, CancellationToken ct)` — starts the tick loop
- `void Stop()` — cancels the tick

- [ ] **Step 2: Build to verify**
Run: `dotnet build --configuration Debug`
Expected: PASS

- [ ] **Step 3: Commit**
```bash
git add src/OpenClawPTT/code/Connection/KeepaliveRunner.cs
git commit -m "refactor(gateway): extract KeepaliveRunner"
```

---

### Task 5: Extract `SessionMessageHandler`

**Files:**
- Create: `src/OpenClawPTT/code/Connection/SessionMessageHandler.cs`

- [ ] **Step 1: Create `SessionMessageHandler.cs`**

Extract from `GatewayClient`:
- `HandleSessionMessage()`
- `HandleAgentStream()`
- `HandleChatFinal()`
- `ExtractFullText()`
- `ExtractMarkedContent()`
- `StripAudioTags()`
- `TestStripAudioTags()` (static test hook)
- `TestExtractMarkedContent()` (instance test hook)

Constructor: `SessionMessageHandler(IGatewayEventSource events, AppConfig cfg)`

Fires events on `IGatewayEventSource`.

- [ ] **Step 2: Build to verify**
Run: `dotnet build --configuration Debug`
Expected: PASS

- [ ] **Step 3: Commit**
```bash
git add src/OpenClawPTT/code/Connection/SessionMessageHandler.cs
git commit -m "refactor(gateway): extract SessionMessageHandler"
```

---

### Task 6: Extract `ConnectionLifecycle`

**Files:**
- Create: `src/OpenClawPTT/code/Connection/ConnectionLifecycle.cs`

- [ ] **Step 1: Create `ConnectionLifecycle.cs`**

Extract from `GatewayClient`:
- `_ws` (IClientWebSocket)
- `_reconnectLock`, `_isReconnecting`, `_reconnectTask`
- `_disposeCts`
- `IsConnected`
- `ConnectAsync()` — the part that establishes the socket connection (up to before auth)
- `DisconnectInternalAsync()`
- `HandleDisconnectionAsync()`
- `ScheduleReconnectAsync()`
- `ReconnectLoopAsync()`
- `DisposeConnection()`
- `ClearPendingRequests()`
- `Dispose()` cleanup

Constructor: `ConnectionLifecycle(AppConfig cfg, DeviceIdentity dev)`

The socket is created here (`new ClientWebSocketAdapter()` or injected). This class owns the socket lifecycle.

- [ ] **Step 2: Build to verify**
Run: `dotnet build --configuration Debug`
Expected: PASS

- [ ] **Step 3: Commit**
```bash
git add src/OpenClawPTT/code/Connection/ConnectionLifecycle.cs
git commit -m "refactor(gateway): extract ConnectionLifecycle"
```

---

### Task 7: Slim `GatewayClient` to thin coordinator

**Files:**
- Modify: `src/OpenClawPTT/code/Connection/GatewayClient.cs`

- [ ] **Step 1: Reduce `GatewayClient` to constructor + field init**

`GatewayClient` becomes ~30 lines:
- Declares all child component fields
- Constructor takes `IGatewayEventSource eventSource` (injected)
- Constructs all child components
- Implements `IGatewayClient` by delegating to children
- Implements `GetEventSource()` returning the injected `eventSource`

Remove all extracted members. Remove all extracted event declarations.

- [ ] **Step 2: Wire up existing `ConnectAsync` flow**

Reassemble `ConnectAsync` as:
```
ConnectionLifecycle.ConnectAsync()
    → AuthHandler.WaitForChallengeAsync()
    → AuthHandler.SendConnectAsync()
    → MessageFraming.SendRequestAsync("sessions.subscribe", ...)
    → KeepaliveRunner.Start()
```

- [ ] **Step 3: Wire up `SendAudioAsync` / `SendTextAsync`**
Delegate to `MessageFraming`.

- [ ] **Step 4: Wire up `ProcessFrame`**
`ReceiveLoop` in `ConnectionLifecycle` calls back into `MessageFraming.ProcessFrame()`, which calls `SessionMessageHandler.Handle*()`.

- [ ] **Step 5: Wire up `HandleApprovalRequest`**
Move to `SessionMessageHandler` or keep in `GatewayClient` as a private helper.

- [ ] **Step 6: Wire up dispose**
`GatewayClient.Dispose()` disposes all children in order.

- [ ] **Step 7: Build to verify**
Run: `dotnet build --configuration Debug`
Expected: PASS

- [ ] **Step 8: Run tests**
Run: `dotnet test --configuration Debug`
Expected: All existing tests pass

- [ ] **Step 9: Commit**
```bash
git add src/OpenClawPTT/code/Connection/GatewayClient.cs
git commit -m "refactor(gateway): slim GatewayClient to thin coordinator"
```

---

### Task 8: Update all consumers

**Files:**
- Modify: `GatewayService.cs` — if it creates `GatewayClient`, needs to create and inject `GatewayEventSource`

- [ ] **Step 1: Find all `GatewayClient` instantiations**
```bash
grep -rn "new GatewayClient" --include="*.cs" .
```

- [ ] **Step 2: Update each to inject `GatewayEventSource`**
```csharp
var eventSource = new GatewayEventSource();
var client = new GatewayClient(cfg, dev, eventSource);
```

- [ ] **Step 3: Build and test**
Run: `dotnet build --configuration Debug`
Run: `dotnet test --configuration Debug`
Expected: PASS

- [ ] **Step 4: Commit**
```bash
git add [modified consumer files]
git commit -m "refactor(gateway): inject GatewayEventSource into GatewayClient consumers"
```

---

## End State

```
GatewayClient          ~30 lines  — coordinator, owns child lifecycle
ConnectionLifecycle    ~150 lines — socket + reconnect
AuthHandler            ~120 lines — auth handshake
MessageFraming         ~100 lines — send/recv framing
KeepaliveRunner         ~30 lines — tick loop
SessionMessageHandler ~200 lines — message parsing + events
GatewayEventSource      ~15 lines — pure event declarations
```

All child classes are independently testable (no `GatewayClient` dependency). All tests that exist today continue to pass. No behavior changes.
