# Configuration Pipeline Review

> Branch: `feat/config-pipeline-review`
> Inspected: Initial config load (`LoadOrSetupAsync`), runtime reconfiguration (`ReconfigureAsync`, `/reconfigure`), `ConfigSaved` event propagation, persistence layer (`FileConfigStorage`), reset/restart loop.

---

## Architecture Overview

### Load path (app startup)

```
Program.Main
  → AppBootstrapper.RunAsync
    → _shellHost.Run(cts)                  // Start StreamShell UI
    → _configService.LoadOrSetupAsync()     // Load or run first-time wizard
      → _storage.Load()                     // FileConfigStorage: read config.json
      → null? → RunInitialSetupAsync()      // Wizard creates AppConfig
      → Validate() has issues? → RunInitialSetupAsync()
      → Save + ConfigSaved?.Invoke()
    → _console.ApplyConsoleConfig(cfg)      // Apply display width, margins
    → AgentSettingsService.Load()           // agents.json
    → new AppRunner(cfg, factory)

AppRunner.RunAsync
  → RunAppLoopAsync() | restart loop
    → Connect PTT, TTS, Gateway, Direct LLM
    → Subscribe _configService.ConfigSaved  // After all services created
    → RunPttLoopAsync()
```

### Reconfigure path (at runtime)

```
User: /reconfigure
  → ReconfigureCommand.ExecuteAsync
    → _configService.Load()                    // Fresh from disk
    → _configService.ReconfigureAsync(host, existing, ct)
      → ModularConfigurationWizard.RunReconfigureAsync()
        → Clone(existing) via JSON round-trip
        → User picks section → RunAsync()
        → Returns cloned AppConfig or existing ref if no changes
      → _storage.Save(newCfg)
      → ConfigSaved?.Invoke(newCfg)            // Notify subscribers
```

### ConfigSaved subscribers

| Subscriber | Handler | What it does |
|-----------|---------|-------------|
| `AppRunner.RunPttLoopAsync` | `OnConfigSaved` (inline lambda) | Calls `audioService.RecreateTranscriber(newCfg, _console)` |
| `StreamShellInputHandler` | `OnConfigSaved(AppConfig)` | Detects DirectLlmUrl/Model changes → shows/hides `/llm` command |
| `DirectLlmProbeService` | `OnConfigSaved(AppConfig)` | Detects DirectLlmUrl/Model changes → re-probes endpoint |

---

## Issues Found

### 🔴 CRITICAL: Stale `_appConfig` reference after `/reconfigure`

**Location**: `StreamShellInputHandler.RegisterDirectLlmCommand()` (line 252)

**Problem**: After `/reconfigure` saves new config, `StreamShellInputHandler`'s `_appConfig` field still points to the **original** `AppConfig` instance from startup. `RegisterDirectLlmCommand()` reads `_appConfig.DirectLlmUrl` and `_appConfig.DirectLlmModelName` — these are the old values.

**Impact**: If Direct LLM is configured via `/reconfigure` (wasn't set at startup), `SetDirectLlmConfigured(true)` calls `RegisterDirectLlmCommand()` which reads **stale** `_appConfig` → returns early because old values are empty → `/llm` command never appears. The user must restart the app to use Direct LLM after adding it via `/reconfigure`.

**Root cause**: `_appConfig` is the raw `AppConfig` instance passed at construction — never updated. `OnConfigSaved` only updates the `_lastKnownLlm*` tracking fields, not the `_appConfig` reference itself.

**Fix**: Either (a) reassign `_appConfig = newCfg` inside `OnConfigSaved`, or (b) have `RegisterDirectLlmCommand` read from `_configService.Load()` (fresh from disk), or (c) pass `newCfg` directly to `SetDirectLlmConfigured`.

---

### 🟠 HIGH: Race / lost updates in `FileConfigStorage.Save()`

**Location**: `FileConfigStorage.Save()` — read-modify-write with no locking

**Problem**: The `Save()` method reads existing JSON, merges changes in memory, then writes the full file:
```csharp
var existingJson = File.ReadAllText(path);  // Read
node = JsonNode.Parse(existingJson);
// ... merge properties ...
File.WriteAllText(path, node.ToJsonString(JsonOpts));  // Write
```

**Impact**: Two concurrent `Save()` calls (e.g., rapid `/appconfig key1 val1` then `/appconfig key2 val2`) can race:
1. Save A reads file
2. Save B reads file (same state)
3. Save A writes (includes key1 change)
4. Save B writes (includes key2 change but NOT key1 — based on stale read)
5. → key1 change is lost

**Also**: No atomic write (write-to-temp + rename). If the process crashes mid-write, `config.json` is corrupted (incomplete JSON). Next startup returns `null` from `Load()`, triggering the first-time wizard — all settings are lost.

**Fix**: Use a write lock + atomic write pattern (`File.WriteAllText(temp)` → `File.Move(temp, target, overwrite: true)`).

---

### 🟠 HIGH: `DirectLlmProbeService.OnConfigSaved` is `async void` — unobserved exceptions

**Location**: `DirectLlmProbeService.cs` line 92

**Problem**: The `async void OnConfigSaved` handler is fire-and-forget. Exceptions thrown before or after `await` crash the process (on .NET, `async void` exceptions are rethrown on the SynchronizationContext — on a ThreadPool thread without a sync context, they exit the process).

```csharp
private async void OnConfigSaved(AppConfig newCfg)
{
    ...
    using var freshService = _factory.CreateDirectLlmService(newCfg);
    await ProbeAndUpdateAsync(freshService, newCfg, CancellationToken.None);
}
```

**Impact**: If `ProbeAndUpdateAsync` throws (connection refused, timeout, DNS failure, etc.), the exception escapes the `async void` → unhandled exception → **app crash**. The `try/catch` only wraps the body of `OnConfigSaved` up to the first `await` — after that, it's on a different async context.

Wait — actually in .NET, the `try/catch` in `async void` DOES catch exceptions from the entire async state machine. Let me recheck... Yes, the `try/catch` wraps everything properly. The exception WOULD be caught. Actually no, looking again:

```csharp
private async void OnConfigSaved(AppConfig newCfg)
{
    ...
    try
    {
        using var freshService = _factory.CreateDirectLlmService(newCfg);
        await ProbeAndUpdateAsync(freshService, newCfg, CancellationToken.None);
    }
    catch (Exception ex)
    {
        _console.LogError("llm", $"LLM re-probe failed: {ex.Message}");
    }
}
```

There IS a try/catch. So the crash risk is contained. But there's still a semantic concern: `async void` is fire-and-forget from the caller's perspective. If `ConfigSaved?.Invoke(cfg)` is called, the multicast delegate calls `OnConfigSaved`, which runs until the first `await`, then returns control to the next handler in the delegate chain. This is fine functionally but hard to reason about.

**Verdict**: Downgraded to 🟡 due to existing try/catch.

---

### 🟡 MEDIUM: No config re-validation after `/reconfigure`

**Location**: `ConfigurationService.ReconfigureAsync()` — calls wizard and saves, no validation

**Problem**: `LoadOrSetupAsync` calls `Validate(cfg)` after loading and re-runs setup if issues are found. `ReconfigureAsync` runs the wizard and immediately saves — no `Validate()` call.

**Impact**: A wizard section _could_ produce invalid config. For example:
- `HarnessConfigSection` validates AuthToken as non-empty, but what if a future code change makes a field optional in the wizard but required in `Validate()`?
- Or a user runs `/reconfigure`, enters the Harness section, and enters an invalid URL — the wizard's validator should catch it, but there's no defense-in-depth.

**Fix**: Call `Validate(newCfg)` after the wizard in `ReconfigureAsync` and warn the user if issues exist.

---

### 🟡 MEDIUM: `RecreateTranscriber` doesn't update `AudioRecorder` on sample rate/config changes

**Location**: `AudioService.cs` — `RecreateTranscriber()` only recreates the transcriber

**Problem**: `/appconfig SampleRate 48000` (or via `/reconfigure`) changes the config value. `ConfigSaved` fires → `AudioService.RecreateTranscriber(newCfg, _console)` runs. But this only recreates the `ITranscriber`, NOT the `IAudioRecorder`:

```csharp
public void RecreateTranscriber(AppConfig config, IColorConsole console)
{
    lock (_transcriberLock)
    {
        old = _transcriber;
        _transcriber = TranscriberFactory.Create(config, console);  // Only transcriber!
    }
    // _recorder is never touched
}
```

**Impact**: If `SampleRate`, `Channels`, `BitsPerSample`, or `MaxRecordSeconds` change, the `AudioRecorder` still records with the original settings. Audio captured after the config change may not match the transcriber's expectations. This is a silent correctness issue — the config appears to be updated but audio recording doesn't follow.

**Fix**: Either (a) ignore those fields in `/appconfig` changes (document them as startup-only), or (b) recreate the recorder too.

---

### 🟡 MEDIUM: AppRunner restart loop doesn't reload config

**Location**: `AppRunner.RunAsync()` — restart loop

```csharp
do {
    result = await RunAppLoopAsync(_cts.Token);  // Uses _cfg from startup
    if (result == (int)AppLoopExitCode.Restart) restartCount++;
} while (result == (int)AppLoopExitCode.Restart);
```

**Problem**: Each restart creates fresh services from the same `_cfg` object. If someone changed config on disk between restarts, the changes are never picked up. The restart loop is intended for internal error recovery (not config changes), but the disconnect between "restart the app loop" and "pick up new config" is surprising.

**Verdict**: Minor — restarts are for transient errors, not config changes. But worth documenting.

---

### 🟡 MEDIUM: `Clone()` in `ModularConfigurationWizard` has maintenance trap

**Location**: `ModularConfigurationWizard.cs` line 183

```csharp
private static AppConfig Clone(AppConfig source)
{
    var json = JsonSerializer.Serialize(source, options);
    var clone = JsonSerializer.Deserialize<AppConfig>(json, options);
    clone.CustomDataDir = source.CustomDataDir;       // Manual fixup
    clone.ReservedRightMargin = source.ReservedRightMargin;  // Manual fixup
    return clone;
}
```

**Problem**: `[JsonIgnore]` properties are lost in the JSON round-trip. Currently two are manually re-assigned. Any future `[JsonIgnore]` property added to `AppConfig` must also be added here — easily missed.

**Fix**: Use a copy-constructor or `MemberwiseClone()` + reflection-based shallow copy instead of JSON round-trip.

---

### 🟡 MEDIUM: Secret masking leaks partial value

**Location**: `ConfigSetupItem.cs` line 165

```csharp
public static string MaskSecret(string value)
{
    if (value.Length <= 4)
        return new string('*', value.Length);
    return value[..4] + new string('*', Math.Min(value.Length - 4, 12));
}
```

**Problem**: Shows the first 4 characters of any secret. This is displayed in the settings summary after each wizard section. Anyone looking over the user's shoulder (or in logs) can see the prefix of API keys.

**Also**: `FileConfigStorage.Save()` skips properties that match defaults by JSON comparison. But secret values (even when set) are written in plaintext to `config.json` on disk. There's no encryption or DPAPI protection.

**Verdict**: Secret-at-rest protection is a broader concern. The display mask is a minor UX leak.

---

### 🟡 MEDIUM: `Async void` handlers on multicast delegate are hard to reason about

**Location**: `ConfigurationService.cs` — `ConfigSaved?.Invoke(cfg)`

**Problem**: `ConfigSaved` is `Action<AppConfig>`. The three subscribers:
1. `AppRunner`'s lambda — synchronous
2. `StreamShellInputHandler.OnConfigSaved` — synchronous
3. `DirectLlmProbeService.OnConfigSaved` — `async void`

The multicast delegate calls them sequentially, but subscriber #3 returns control after the first `await`, running the actual probe work in the background. There's no sequencing guarantee — the lambda and handler #2 run synchronously, then handler #3 starts and runs partially, then returns. The probe runs in the background concurrently with whatever follows the `Invoke`.

**Impact**: If `RecreateTranscriber` (handler #1, which is actually the lambda in the order of subscription) depends on something the probe sets up... no, they're independent. But the `async void` nature means:
- Unobserved exception in probe crashes the process (contained by try/catch now)
- No way for the caller to know when probe completes
- Probe runs with `CancellationToken.None` — can't be cancelled

**Fix**: Consider `Func<AppConfig, Task>` instead of `Action<AppConfig>` and `await` all handlers, or use a channel/queue for async work.

---

### 🔵 LOW: `AppConfigCommand` uses shared `_appConfig` reference — side effects visible to all holders

**`/appconfig` mutates the shared `AppConfig` instance in-place before saving.** This is by design (all services injected with same reference see changes instantly), but creates coupling:

- Mutations are visible before `ConfigSaved` fires 
- Services that read config directly (not via `ConfigSaved`) see the new values and might act on them without the re-probe/rebuild that `ConfigSaved` triggers

---

### 🔵 LOW: `Console.CancelKeyPress` handler suppresses Ctrl+C but app can't be killed gracefully

**Location**: `AppBootstrapper.cs` line 96

```csharp
e.Cancel = true;  // Ctrl+C does nothing
```

**Problem**: Ctrl+C is completely suppressed. If `StreamShell` becomes unresponsive or the user needs to hard-kill, there's no escape except closing the terminal. The comment says Ctrl+C is "handled by StreamShell as Copy" — but if StreamShell fails, there's no alternative shutdown path.

---

### 🔵 LOW: `GetWindowHeight()` in wizard assumes interactive terminal

**Location**: `ModularConfigurationWizard.RunInitialSetupAsync()` — line 59

```csharp
for (int i = 0; i < ConsoleMetrics.GetWindowHeight(); i++)
    host.AddMessage("");
```

**Problem**: Pushes empty lines to "clear" between wizard sections. If running non-interactively (piped input, redirected output), `GetWindowHeight()` may return 0 or a default, making the "clear" unreliable. More importantly, this is a visual hack — pushing empty lines just scrolls content up.

---

### 🔵 LOW: No timeout for config file read

**`FileConfigStorage.Load()` calls `File.ReadAllText(path)` with no timeout.** If the file is on a network share or stuck behind anti-virus scanning, read blocks indefinitely. The entire startup hangs.

---

### 🔵 LOW: `ConfigSaved` fired during initial setup — before any subscribers exist

In `LoadOrSetupAsync`, `ConfigSaved?.Invoke(cfg)` fires when:
1. No config found → setup completed
2. Config had issues → re-setup completed

But at this point, no one subscribes to `ConfigSaved` yet. The subscribers are added LATER in `AppRunner.RunAppLoopAsync`. So the first `ConfigSaved` event is always a no-op. This is harmless (the event is a notification, not a command), but wastes an event fire on every setup/reconfig at startup.

---

## Resilience Assessment

| Dimension | Status | Notes |
|-----------|--------|-------|
| **File corruption** | ⚠️ Weak | No atomic writes. Crash during Save → corrupted file → full data loss (triggers first-time wizard) |
| **Concurrent saves** | ⚠️ Weak | No locking on read-modify-write. Rapid `/appconfig` calls lose updates |
| **Network timeout (LLM probe)** | ⚠️ Partial | Cancellation token available but no explicit timeout. Hanging probe blocks startup |
| **Network timeout (gateway connect)** | ✅ Good | `GatewayErrorClassifier` handles known errors gracefully. Guides user actions |
| **Config validation** | ✅ Good | `Validate()` checks URL format, auth tokens, sample rate range, enum values. But only called on load, not after `/reconfigure` |
| **Config change propagation** | ⚠️ Partial | TTS → needs app restart to pick up provider/voice changes (re-created once at startup in `InitializeTtsProviderAsync`). STT → re-creatable via `RecreateTranscriber`. Gateway → no re-pickup of URL/auth changes |
| **Thread safety (wizard state)** | ✅ Good | `WizardState` uses `Interlocked` for ref-counted enter/leave |
| **Concurrent access (transcriber)** | ✅ Good | `_transcriberLock` protects the reference swap in `RecreateTranscriber` |
| **Startup failure handling** | ✅ Good | Exception caught → `AppExitHandler` formats and logs |

---

## Summary

### Must Fix

| # | Severity | Issue | Fix |
|---|----------|-------|-----|
| 1 | 🔴 CRITICAL | `_appConfig` stale after `/reconfigure` — `/llm` won't appear if Direct LLM added post-startup | Reassign `_appConfig` in `OnConfigSaved` or read fresh from disk in `RegisterDirectLlmCommand` |
| 2 | 🟠 HIGH | `FileConfigStorage.Save()` has no write atomicity — corrupt file on crash | Atomic write (temp file + rename) + file lock for concurrent saves |
| 3 | 🟠 HIGH | No validation after `/reconfigure` — invalid config can be saved | Call `Validate()` after wizard in `ReconfigureAsync` and surface issues |

### Should Fix

| # | Severity | Issue | Fix |
|---|----------|-------|-----|
| 4 | 🟡 MEDIUM | `RecreateTranscriber` ignores `Recorder` — sample rate/channel changes silently ineffective | Either recreate recorder or document which fields are startup-only |
| 5 | 🟡 MEDIUM | `Clone()` JSON round-trip loses `[JsonIgnore]` properties — maintenance trap | Use copy-constructor or MemberwiseClone + reflection |
| 6 | 🟡 MEDIUM | Secret mask shows first 4 chars in settings summary | Mask all chars or don't display secrets in summaries |
| 7 | 🟡 MEDIUM | `async void` handlers on multicast delegate — fire-and-forget, CancellationToken.None | Convert to `Func<AppConfig, Task>` or use event channel |

### Could Fix

| # | Severity | Issue | Fix |
|---|----------|-------|-----|
| 8 | 🔵 LOW | Ctrl+C entirely suppressed — no escape hatch if StreamShell breaks | Check for double-Ctrl+C as kill signal |
| 9 | 🔵 LOW | No timeout on `File.ReadAllText` for config file | Use async read with timeout |
| 10 | 🔵 LOW | Wizard "clear screen" via empty lines is terminal-specific hack | Proper terminal clear or skip in non-interactive mode |
