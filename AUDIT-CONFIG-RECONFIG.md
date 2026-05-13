# Config/Reconfig Flow Audit

Date: 2026-05-13
Branch: audit/config-reconfig

## Flow Overview

The config/reconfig pipeline has three entry points:

1. **Startup** (`AppBootstrapper.RunAsync`) → `ConfigurationService.LoadOrSetupAsync`
2. **/reconfigure command** → `ReconfigureCommand.ExecuteAsync` → `ConfigurationService.ReconfigureAsync`
3. **/appconfig command** → `AppConfigCommand.ExecuteAsync` → `ConfigurationService.Save`

All paths converge on `ConfigurationService.Save()` which persists to `FileConfigStorage` and fires `ConfigSaved`.

## ConfigSaved Event Subscribers (4 current)

| Subscriber | File | Listens For | What It Does |
|---|---|---|---|
| AppRunner (RunPttLoopAsync) | `AppRunner.cs:336` | All saves | Recreates transcriber + recorder |
| StreamShellInputHandler | `StreamShellInputHandler.cs:257` | LLM URL/Model changes | Toggles /llm command visibility |
| DirectLlmProbeService | `DirectLlmProbeService.cs:92` | LLM URL/Model changes | Re-probes Direct LLM endpoint |
| ConfigurationService | `ConfigurationService.cs:19` | (publisher) | Fires the event itself |

## Issues Found

### 🔴 1. Resilience: GatewayService holds stale config reference
**File**: `AppRunner.cs` → GatewayService created once with initial `_cfg`
**File**: `StreamShellInputHandler.cs` → also holds `_appConfig` passed at construction

GatewayService does NOT subscribe to `ConfigSaved`. Changing `GatewayUrl` or `AuthToken` via `/appconfig` saves to disk but the active connection holds the old values. The user must `/reconnect` or restart.

The AppConfigCommand directly mutates `_appConfig` in-place, then calls Save. If save fails (disk full, permissions), the in-memory config is already dirty.

**Recommendation**: `ConfigSaved` should be a proper message/command for GatewayService to hot-reload gateway URIs. Or at minimum, document that `GatewayUrl`/`AuthToken` changes require `/reconnect`.

---

### 🔴 2. Resilience: /appconfig doesn't validate before save
**File**: `AppConfigCommand.cs:89-96`

The command sets a property and calls `Save()` immediately. No `Validate()` call. You can set `GatewayUrl` to `"abc"` (invalid URL), `SampleRate` to `999999` (outside 8000-48000), or `ReconnectDelaySeconds` to `-5`. The invalid config is persisted and will only be caught on next startup.

**Recommendation**: Call `ConfigurationService.Validate(newCfg)` before Save. Warn/show validation errors to user but let them override.

---

### 🔴 3. DRY: ConfigSaved fires from 4 call sites, all subscribers run on every save
**File**: `ConfigurationService.cs` — lines 44, 68, 99, 108

Every `ConfigSaved` fires all subscribers:
- `AppRunner.OnConfigSaved` recreates transcriber + recorder (try/catch wrapped, safe but wasteful)
- `StreamShellInputHandler.OnConfigSaved` only reacts to LLM URL/Model changes (field-comparison guard)
- `DirectLlmProbeService.OnConfigSaved` only reacts to LLM URL/Model changes (same guard)

When changing `UserMessagePrefix`, all three subscribers fire, only StreamShellInputHandler filters (and finds nothing changed), but the AppRunner subscriber still recreates both transcriber and recorder — doing I/O and thread-safety work for no reason.

**Recommendation**: Add a `ConfigChangedEventArgs` with a `HashSet<string>` of changed property names to the event, so subscribers can filter efficiently. Or split into specific events: `SttConfigChanged`, `DirectLlmConfigChanged`, `DisplayConfigChanged`.

---

### 🔴 4. SRP: ConfigurationService has too many responsibilities
**File**: `ConfigurationService.cs`

Current responsibilities:
- Data access: Load, Save (delegates to `_storage`)
- Validation: `Validate(AppConfig)`
- Interactive wizard orchestration: calls `_wizard.RunInitialSetupAsync` / `_wizard.RunReconfigureAsync`
- Event publishing: manages `ConfigSaved` event

The wizard is a hardcoded field (`new ModularConfigurationWizard()`) — not injectable in the default constructor path.

**Recommendation**: Extract wizard orchestration to its own service (`IConfigWizardOrchestrator`). `ConfigurationService` becomes a pure data service + validation + event bus.

---

### 🟡 5. SRP: AppRunner has god-method composition
**File**: `AppRunner.cs`

`RunAppLoopAsync` (line ~66) does:
- Sets debug level
- Creates state machine, direct LLM service, TTS summarizer
- Launches TTS + LLM probe background tasks
- Creates gateway service + wires lifecycle events
- Connects gateway with guided error handling
- Delegates to `RunPttLoopAsync`

`RunPttLoopAsync` (line ~310) does:
- Creates audio service + subscribes to ConfigSaved
- Creates naming pipeline (naming sender, naming service, input handler)
- Creates shell/hotkey services
- Runs PTT loop + cleanup

**Recommendation**: Consider extracting a `PttSession` class that owns the per-session lifecycle (services created for one run), keeping `AppRunner` focused on the outer restart loop and TTS/LLM init coordination.

---

### 🟡 6. Thread safety: ConfigSaved events fire outside atomic save lock
**File**: `FileConfigStorage.cs:75-85` — atomic write with lock
**File**: `ConfigurationService.cs:99-108` — Save then fire event outside lock

`FileConfigStorage.Save()` holds a lock for disk write. But `ConfigurationService.Save()` fires `ConfigSaved` AFTER `_storage.Save(cfg)` returns — outside the lock. Two concurrent saves race on the event: second subscriber thread disposes a resource the first thread just created.

**Recommendation**: Either (a) make `ConfigurationService.Save` async with a semaphore, or (b) use a lock in `ConfigurationService` that covers both storage write and event dispatch. The lock contention is negligible — config saves are manual, not automatic.

---

### 🟡 7. DRY: No unified validation pipeline for subscribers
**File**: `ConfigurationService.cs:80-99` — Validate in the service
**File**: `AppRunner.cs:320-335` — Subscriber has its own inline try/catch for STT

Two validation patterns coexist:
- `ConfigurationService.Validate()` returns a list of issue strings
- AppRunner's `OnConfigSaved` wraps `RecreateTranscriber`/`RecreateRecorder` in try/catch (duck typing failure)

If a new property is added to Validate(), subscribers that do field-level work won't know about it unless they're updated.

**Recommendation**: Add a `ConfigValidated` event or make Validate() part of the ConfigSaved pipeline so subscribers can check validity before acting.

---

### 🟡 8. Design: /reconfigure only hot-reloads STT, not display/UI/config
**File**: `AppRunner.cs:320-335` — only STT changes are hot-reloaded

Things that DON'T hot-reload on /reconfigure or /appconfig:
- `VisualMode`, `VisualFeedbackEnabled`, `VisualFeedbackPosition`, etc.
- `UserMessagePrefix`, `RightMarginIndent`, `EnableWordWrap`
- `Status positions` (set once in `AppRunner` constructor via `_statusService.ApplyConfigPositions(_cfg)`)
- `BottomPanelLineCount`
- `HotkeyCombination`, `HoldToTalk` — PTT hotkey

These all take effect on restart only. Not critical, but surprising for users who expect runtime settings to apply immediately.

**Recommendation**: Subscribe relevant display services to `ConfigSaved` and reapply display/UI config on changes that affect visual output.

---

### 🟢 9. Minor: Post-reconfigure validation doesn't loop back
**File**: `ConfigurationService.cs:86-93`

After `ReconfigureAsync` runs the wizard, it validates the result. If issues remain, it shows them but doesn't offer to fix them. User sees "[yellow]" warnings and is told to run `/reconfigure again`. Not a critical UX issue since the wizard's section-level validation catches most problems.

---

### 🟢 10. Minor: FileConfigStorage.Load returns null on corrupted JSON
**File**: `FileConfigStorage.cs:37-55`

If `config.json` becomes corrupted (manual edit, truncation, disk error), JSON parsing fails → `Load()` returns null → `LoadOrSetupAsync` triggers first-time setup. User loses all settings. A backup-recovery pattern (save `config.json.corrupted.12345678` and create fresh config from defaults) would be safer.

---

## Summary

| Severity | Count |
|----------|-------|
| 🔴 High | 4 |
| 🟡 Medium | 4 |
| 🟢 Low | 2 |

**Top 3 priorities**:
1. GatewayService stale config — no way to hot-reload `GatewayUrl`/`AuthToken` changes (🔴)
2. /appconfig lacks validation before save — corrupt values can be persisted (🔴)
3. ConfigSaved event has no change metadata — subscribers waste work on unrelated saves (🔴)
