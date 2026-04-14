# Connection Testability Plan

**Goal:** Enable 90%+ test coverage for all Connection folder classes via dependency injection, interface extraction, and comprehensive unit tests.

**Working Directory:** `.worktrees/testability-work`

---

## Issues Found

| File | Problem |
|---|---|
| `GatewayConnectionLifecycle.cs:74` | Hard-codes `new ClientWebSocketAdapter()` — socket not injectable |
| `GatewayMessager.cs:23` | Hard-codes `new MessageFraming(_ws, cfg)` — framing not injectable |
| `ConnectionResources.cs:12` | Uses `ClientWebSocket?` instead of `IClientWebSocket?` — leaks concrete type |
| `GatewayService.cs:30,32,57,58` | `new DeviceIdentity`, `new ConsoleUiOutput`, `new AgentOutputAdapter`, `new GatewayClient` — all tightly coupled |
| `GatewayClient.cs:30` | `new GatewayConnectionLifecycle(...)` — not injectable |
| `GatewayReconnector.cs:16` | Takes `IGatewayConnector` but lifecycle creates it internally |
| `MessageFraming.cs` | Reads `_ws.State` after `SendAsync` — could fail if mock returns wrong state |

---

## Changes

### 1. `GatewayConnectionLifecycle` — inject socket factory
- Add `Func<IClientWebSocket> _socketFactory`
- Default: `() => new ClientWebSocketAdapter()`
- Constructor param: `Func<IClientWebSocket>? socketFactory = null`
- Use `_socketFactory()` instead of `new ClientWebSocketAdapter()`

### 2. `GatewayMessager` — inject MessageFraming
- Add `MessageFraming? _framing` constructor param
- If null, create internally (backwards compat)
- Expose `GetFraming()` already exists ✅

### 3. `ConnectionResources` — use IClientWebSocket
- Change `ClientWebSocket? WebSocket` → `IClientWebSocket? WebSocket`
- This is a BREAKING change for `ConnectionResilience.HandleDisconnectionAsync` which accesses `.State` — `IClientWebSocket` has `.State` ✅ so it works

### 4. `GatewayClient` — inject GatewayConnectionLifecycle
- Add `IGatewayConnectionLifecycle` interface
- Extract interface: `IGatewayConnectionLifecycle` with `ConnectAsync`, `DisconnectAsync`, `SendRequestAsync`, `IsConnected`, `GetFraming()`
- `GatewayConnectionLifecycle` implements it
- `GatewayClient` takes `IGatewayConnectionLifecycle` in constructor

### 5. `GatewayService` — inject dependencies
- Extract `IConsoleOutput` already exists ✅
- Extract `IDeviceIdentity` interface
- Extract `IGatewayClientFactory` or pass ready-made `IGatewayClient`
- Add `AgentOutputAdapter` interface? Or injectable?

### 6. `GatewayAuthenticator` — minor
- Already injectable via constructor ✅
- `TickIntervalMs` property already exposed ✅

### 7. `GatewayReconnector`
- Already takes `IGatewayConnector` ✅
- Just needs `Dispose()` properly implemented

---

## New Test Files (12 tests per class minimum)

- `GatewayConnectionLifecycleTests` — mock socket factory, mock connector
- `GatewayMessagerTests` — mock socket, mock event source, mock framing
- `MessageFramingTests` — mock socket, test all request/response paths
- `ConnectionResilienceTests` — mock connect func, verify reconnect logic
- `GatewayReconnectorTests` — mock connector, test reconnect loop
- `GatewayAuthenticatorTests` — test auth handshake with mock config
- `GatewayServiceTests` — mock client + output, test connect flow
- `GatewayClientEventsTests` — already exists, extend it

---

## Commit Sequence

1. Add `IGatewayConnectionLifecycle` interface + wire into `GatewayClient`
2. Inject socket factory into `GatewayConnectionLifecycle` + tests
3. Inject `MessageFraming` into `GatewayMessager` + tests  
4. Change `ConnectionResources.WebSocket` to `IClientWebSocket` + fix all call sites
5. Add `IDeviceIdentity` interface + wire into `GatewayService`
6. Add `IGatewayClientFactory` + wire into `GatewayService`
7. Comprehensive test pass — fill gaps, aim for 90%+
8. Final build + coverage report

---

## Coverage Target

| Class | Target |
|---|---|
| `GatewayConnectionLifecycle` | 90%+ |
| `GatewayMessager` | 90%+ |
| `MessageFraming` | 90%+ |
| `ConnectionResilience` | 90%+ |
| `GatewayReconnector` | 90%+ |
| `GatewayAuthenticator` | 90%+ |
| `GatewayService` | 90%+ |
| `GatewayClient` | 90%+ |
