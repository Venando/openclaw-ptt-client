# STT/TTS Configuration & Status Analysis

> Branch: `analysis/stt-tts-config-flow` (based on `origin/main` @ `07aec84`)
> Inspected: STT/TTS config flow (initial + reconfig), status reporting (status bar dots + `/appstatus` panel), resilience on failure, UI consistency.

---

## Architecture Overview

### Status Flow

```
Startup:
  AppRunner.InitializeTtsProviderAsync → Green (ok) / Red (fail)     [once, never re-probed]
  AudioService constructor → TranscriberFactory.Create → Green       [always Green on success]

Runtime:
  ConfigSaved → OnConfigSaved (STT watch list) → RecreateRecorder → RecreateTranscriber
                                             → SetServiceStatus(Stt, Yellow) → ... → Green/Red
  ConfigSaved → OnGatewayConfigSaved (GW props) → GatewayService.RecreateWithConfig
  ConfigSaved → OnDisplayConfigSaved (UI props) → ApplyConfigPositions, ApplyConsoleConfig
  
  TTS: NO ConfigSaved handler → never recreated → stale provider after /reconfigure
```

### Status Dot Display

```
TopSeparatorRight:  "GW:●" "TTS:●" "STT:●" "LLM:●"
                    (colored dot: Green=connected, Yellow=transitioning, Red=failed)
```

---

## Issues Found

### 🔴 CRITICAL: TTS cannot be reconfigured at runtime — requires app restart

**Location**: `AppRunner.cs` — no TTS handler in `ConfigSaved`

**Problem**: STT has a `ConfigSaved` handler (`OnConfigSaved` in `RunPttLoopAsync`) that watches STT-related properties and calls `RecreateRecorder` + `RecreateTranscriber`. TTS has **no equivalent handler**. The three `ConfigSaved` subscribers are:
1. `OnGatewayConfigSaved` — watches `GatewayUrl`, `AuthToken`, `DeviceToken`, `TlsFingerprint`
2. `OnDisplayConfigSaved` — watches display/UI props + position props
3. `OnConfigSaved` — watches STT + audio props only

**Impact**: When the user runs `/reconfigure` → "Text-To-Speech" → changes provider/voice/mode → saves, the new config is persisted to disk BUT the running TTS provider **never changes**. The app continues using the old provider. The user sees "TTS:●" (green, from startup) and has no indication their changes are silently ignored. A restart is required.

**Why this happens**:
- `InitializeTtsProviderAsync` is called once at startup on a background thread
- The `ITextToSpeech` provider is released via `ReleaseProvider()` and wired into `GatewayService` → `AudioResponseHandler`
- After initial wiring, there's no mechanism to swap the TTS provider
- `GatewayService.RecreateWithConfig` only recreates the `GatewayClient` (WebSocket), not the TTS wiring
- `AudioResponseHandler` holds a direct reference to `ITextToSpeech` with no replacement API

**Fix**: Add a TTS watch list to `ConfigSaved` (similar to STT's `OnConfigSaved`), call a `RecreateTtsProvider` method on `GatewayService`, which would:
1. Dispose old provider
2. Create new `TtsService` from updated config
3. Create new `AudioResponseHandler` with new provider
4. Wire into coordinator (replace via `SetAudioHandler`)

---

### 🔴 CRITICAL: STT status set to Green BEFORE transcriber is verified

**Location**: `AppRunner.cs` line 319

```csharp
using var audioService = _factory.CreateAudioService(_cfg);
// AudioService constructor creates a transcriber synchronously — mark STT as ready
_statusService.SetServiceStatus(ServiceKind.Stt, StatusColor.Green);
```

**Problem**: The status is set to Green as a side effect of `CreateAudioService` finishing without throwing. There's no actual verification that the transcriber works — no test transcription, no connectivity check. The `AudioService` constructor calls `TranscriberFactory.Create()` which:
- For Groq/OpenAI: creates a client object (no network call) — Green is reasonable
- For whisper-cpp: checks binary exists but doesn't test it — Green may be misleading
- For faster-whisper: checks uv + environment setup — Green may be misleading

If `TranscriberFactory.Create` throws (e.g., whisper-cpp binary not found, OpenAI key invalid format), the constructor throws → app crashes → status is never displayed.

**Impact**: Status dot shows Green but transcriber might fail on first use. No "verifying" step, no grace period, no delayed validation.

**Fix**: Defer status to first successful transcription, or add a verify-on-init step (e.g., send a short zero-silence audio clip through the pipeline).

---

### 🟠 HIGH: STT status briefly flashes Yellow→Green — user never sees Yellow

**Location**: `AppRunner.cs` lines 437-449 (OnConfigSaved STT handler)

```csharp
void OnConfigSaved(ConfigChangedEventArgs e)
{
    // ...
    _statusService.SetServiceStatus(ServiceKind.Stt, StatusColor.Yellow);
    try
    {
        audioService.RecreateRecorder(e.NewConfig, _console);
        audioService.RecreateTranscriber(e.NewConfig, _console);
        _statusService.SetServiceStatus(ServiceKind.Stt, StatusColor.Green);
    }
    catch (Exception ex)
    {
        _statusService.SetServiceStatus(ServiceKind.Stt, StatusColor.Red);
    }
}
```

**Problem**: The Yellow→Green transition happens synchronously within the same event handler, inside a lock (`Mutate()` in `StatusService`). The animation timer (`StatusAnimationManager`) runs on a separate thread and would need to render the Yellow state between the two `SetServiceStatus` calls. But both calls happen in the same `lock` block → the animation renderer can't observe the intermediate state.

Even if the renderer could observe Yellow, the transition is effectively instantaneous (no I/O between SetServiceStatus calls) — the user would never see the Yellow dot.

**Impact**: Misleading status transitions — Yellow is set but never visible. If `RecreateTranscriber` is slow (e.g., model download), the status would show Green before the transcriber is actually ready (but this is the same issue as #3 below).

**Fix**: Run the re-creation on a background thread with async status updates, or use a `Task.Run` + continuation pattern similar to how gateway reconnect is handled.

---

### 🟠 HIGH: `FasterWhisperModel` missing from STT reconfig watch list

**Location**: `AppRunner.cs` lines 419-431

```csharp
var sttProps = new[]
{
    nameof(AppConfig.SttProvider),
    nameof(AppConfig.GroqModel),
    nameof(AppConfig.GroqApiKey),
    nameof(AppConfig.OpenAiApiKey),
    nameof(AppConfig.OpenAiModel),
    nameof(AppConfig.WhisperCppModel),
    nameof(AppConfig.WhisperCppBinaryPath),
    nameof(AppConfig.SampleRate),
    nameof(AppConfig.Channels),
    nameof(AppConfig.BitsPerSample),
    nameof(AppConfig.MaxRecordSeconds),
};
```

**Problem**: `FasterWhisperModel` is NOT in the `sttProps` list. If the user changes the faster-whisper model via `/reconfigure`, the `ConfigSaved` handler doesn't fire the STT re-creation path. The transcriber keeps using the old model.

**Also missing**: `Locale` (affects Groq/OpenAI STT). If the user changes locale, the change is silently ignored until restart.

**Impact**: Changing faster-whisper model via `/reconfigure` silently does nothing. User must restart.

**Fix**: Add `nameof(AppConfig.FasterWhisperModel)` and `nameof(AppConfig.Locale)` to `sttProps`.

---

### 🟡 MEDIUM: `/appstatus` FormatStt() doesn't show Groq/OpenAI model

**Location**: `AppStatusCommand.cs` lines 158-163

```csharp
private string FormatStt()
{
    var provider = _config.SttProvider ?? "built-in (gateway)";
    var model = _config.FasterWhisperModel ?? _config.WhisperCppModel ?? "default";
    return $"{provider} | Model: {model}";
}
```

**Problem**: When STT provider is Groq or OpenAI, the model display falls through to "default". It doesn't read `GroqModel` or `OpenAiModel`.

Compare with `ModularConfigurationWizard.GetSttStatus()` which correctly handles all providers:
```csharp
private static string GetSttStatus(AppConfig config)
{
    var provider = config.SttProvider ?? "(not set)";
    var model = provider switch
    {
        "groq" => config.GroqModel ?? "whisper-large-v3-turbo",
        "openai" => config.OpenAiModel ?? "whisper-1",
        "whisper-cpp" => config.WhisperCppModel ?? "(default)",
        "faster-whisper" => config.FasterWhisperModel ?? "(default)",
        _ => "",
    };
    return string.IsNullOrEmpty(model) ? provider : $"{provider} ({model})";
}
```

**Impact**: `/appstatus` shows incorrect model info for Groq/OpenAI providers. User sees "groq | Model: default" instead of "groq | Model: whisper-large-v3-turbo".

**Fix**: Mirror the switch pattern from `GetSttStatus()` into `FormatStt()`.

---

### 🟡 MEDIUM: Status dot shows no detail — completely opaque to the user

**Location**: `ServiceStatusPart.cs`

The status bar shows: `GW:● TTS:● STT:● LLM:●`

**Problem**: The user can see a colored dot but has zero context about WHAT the status represents beyond the label prefix. For STT, there's no indication of which provider is active or which model. For TTS, no voice/mode info.

This is by design (separator bar is space-constrained), but the separation between "status dot" and "detailed status" means:
- User must explicitly run `/appstatus` to see provider/model info
- A Red STT dot could mean: Groq API key invalid, whisper-cpp binary not found, OpenAI connection failed, etc. — but user can't tell which
- A Green TTS dot gives false confidence if the TTS was configured at startup but reconfig silently changed to a broken provider

**Fix**: Consider showing a brief hover/tooltip on the status dot, or adding a one-line status message in the bottom panel, or echoing failed-initialization details to the error log that `/errors` can show.

---

### 🟡 MEDIUM: Edge TTS with null subscription key shows Red with no actionable message

**Location**: `TtsService.cs` constructor lines 67-71

```csharp
TtsProviderType.Edge => config.TtsSubscriptionKey != null
    ? new Providers.EdgeTtsProvider(config.TtsSubscriptionKey, config.TtsRegion ?? "eastus")
    : null,
```

**Problem**: If the user selects Edge TTS but doesn't provide a subscription key, `_provider` is set to `null`. Then:

```csharp
if (_provider == null && _providerType == TtsProviderType.Edge)
{
    // Edge with null key — warn but don't crash (TtsService still works, TTS just silent)
}
```

The comment says "warn" but nothing is logged! No `_console.Log()` call. The provider is silently null. Later in `InitializeTtsProviderAsync`:

```csharp
if (ttsService.Provider != null)
{
    _statusService.SetServiceStatus(ServiceKind.Tts, StatusColor.Green);
    // ...
    return ttsService.ReleaseProvider();
}

// Provider is null (Edge with no key, etc.) — warn but don't error
_statusService.SetServiceStatus(ServiceKind.Tts, StatusColor.Red);
_console.Log("tts", "TTS provider is null (not configured).");
```

The log says "TTS provider is null (not configured)" — but the user DID configure it. They just didn't provide a key. The message is misleading.

**Fix**: Log a specific warning for Edge null key case, and show `[yellow]` status (warning) rather than `[red]` (error).

---

### 🟡 MEDIUM: `AudioService.LogSttProvider` doesn't handle faster-whisper

**Location**: `AudioService.cs` lines 162-176

```csharp
var model = config.SttProvider switch
{
    AppConfig.ProviderGroq => config.GroqModel,
    AppConfig.ProviderOpenAi => config.OpenAiModel,
    AppConfig.ProviderWhisperCpp => config.WhisperCppModel,
    _ => null
};
```

**Problem**: Missing `AppConfig.ProviderFasterWhisper` case. When using faster-whisper, the model display falls to `null` → "default". The log shows "STT: faster-whisper (default)" regardless of actual model.

**Fix**: Add `AppConfig.ProviderFasterWhisper => config.FasterWhisperModel` case.

---

### 🔵 LOW: Duplicate status formatting logic between wizard and AppStatusBottomPanel

The `GetSttStatus()` in `ModularConfigurationWizard` and `FormatStt()` in `AppStatusBottomPanel` both format STT status but with different logic. The former handles all providers correctly; the latter only handles whisper-cpp/faster-whisper.

Similarly, `GetTtsStatus()` vs `FormatTts()` — the wizard shows voice+mode, the panel shows provider+mode.

**Fix**: Extract shared status formatting into a common helper (e.g., `StatusFormatter.FormatSttDetail(config)`) used by both.

---

### 🔵 LOW: `ConfigSaved` for TTS status position change doesn't update TTS

**Location**: `AppRunner.cs` — `OnDisplayConfigSaved` handler

When `TtsStatusPosition` changes, `ApplyConfigPositions` correctly repositions the TTS dot on the separator. But the TTS provider itself is never reconfigured — the dot position moves, the provider stays stale. This is a niche UX edge case but highlights the disconnect between "position config" and "provider config".

---

## Status State Machine Analysis

### Current States

| Service | States Used | States Missing |
|---------|------------|----------------|
| Gateway | Green, Yellow (reconnecting), Red (failed) | None — full coverage |
| TTS | Green, Red (startup only) | Yellow (reconfiguring), "Warning" (Edge-no-key) |
| STT | Green (startup), Yellow→Green (reconfig), Red (reconfig fail) | "Verifying", "Warning" (model not downloaded) |

### Status Transition Gaps

1. **TTS reconfig**: No transition at all. Dot stays at whatever color was set at startup.
2. **STT startup**: Jumps directly to Green without verification. No Yellow "initializing" state.
3. **STT reconfig Yellow**: Set but never visible due to synchronous execution in the same lock.
4. **TTS Edge-null**: Shows Red when Yellow (warning) would be more appropriate.

### What the user experiences

| Scenario | Expected | Actual |
|----------|----------|--------|
| `/reconfigure` → change TTS provider | TTS restarts with new provider | Silent no-op, old provider keeps running |
| `/reconfigure` → change faster-whisper model | STT recreated with new model | Silent no-op, `FasterWhisperModel` not in watch list |
| Edge TTS with no key | Yellow warning dot | Red error dot |
| STT at startup | Yellow (initializing) → Green | Instant Green |
| Whisper-cpp model download fails | Red dot + error message | Green dot (constructor succeeded), fails on first use |
| `/appstatus` with Groq STT | Shows "Groq (whisper-large-v3-turbo)" | Shows "groq \| Model: default" |

---

## Resilience Assessment

| Dimension | STT | TTS |
|-----------|-----|-----|
| **Live reconfig** | ✅ (partial — missing FasterWhisperModel, Locale) | ❌ None — requires restart |
| **Graceful degradation on init failure** | ❌ TranscriberFactory.Create throws → app crashes | ⚠️ Catches exception, shows Red, returns null provider |
| **Retry on transient failure** | ❌ No retry logic | ❌ No retry logic (only at startup) |
| **Status feedback on reconfig** | ⚠️ Yellow set but never visible | ❌ No status change at all |
| **Provider health check** | ❌ No verify step — Green set before first use | ❌ No verify step — Green set on construction success |
| **Config validation before use** | ❌ No pre-validation of STT config | ⚠️ Edge null key is silently accepted |
| **Thread safety (reconfig)** | ✅ _transcriberLock + _recorderLock | N/A (no reconfig handler) |
| **Error logging** | ⚠️ LogSttProvider missing faster-whisper case | ⚠️ Edge null case not logged |

---

## Related Branches (Unmerged)

| Branch | Status | Key Changes |
|--------|--------|-------------|
| `fix/configure-show-status` | Unmerged | Removes CoquiUv from TTS providers, fixes MainAgentsPart refresh, DRYs STT download UI, fixes `PromptSelectionHelper` active marker |
| `fix/stt-whisper-config-flow` | Unmerged | Removes faster-whisper as STT option(!), simplifies to Whisper.cpp only, removes `FasterWhisperConfigFlow` |
| `fix/tts-reconnect-status` | Unmerged | Major refactoring: Python TTS reconnection + status accuracy, simpler ReadLineAsync, Ctrl+D handling |

⚠️ **Warning**: `fix/stt-whisper-config-flow` removes the faster-whisper provider entirely. This conflicts with `feat/tts-uv-coqui` (just merged into main) which adds `CoquiUv`. The branch `fix/configure-show-status` also removes `CoquiUv`. These branches are in conflict — the project needs a decision on whether `CoquiUv` and `faster-whisper` stay.

---

## Summary of Fixes Needed

### Must Fix (correctness / data loss)

| # | Issue | Fix |
|---|-------|-----|
| 1 | TTS never reconfigures at runtime | Add TTS watch list to `ConfigSaved`, add `RecreateTtsProvider` to GatewayService |
| 2 | `FasterWhisperModel` missing from STT watch list | Add `nameof(AppConfig.FasterWhisperModel)` to `sttProps` |
| 3 | `/appstatus` FormatStt() ignores Groq/OpenAI models | Add switch cases for Groq/OpenAI model display |
| 4 | `AudioService.LogSttProvider` ignores faster-whisper | Add `ProviderFasterWhisper` case |

### Should Fix (UX / maintenance)

| # | Issue | Fix |
|---|-------|-----|
| 5 | STT status Green before verification | Add verify step or defer to first successful transcription |
| 6 | STT Yellow reconfig state never visible | Run recreation on background thread with async status |
| 7 | Edge TTS null key shows Red instead of Yellow warning | Add specific warning log + Yellow status for configurable-but-unavailable |
| 8 | Status dot has zero context — user can't diagnose failures | Add error detail to `/errors` log or add tooltip |
| 9 | Duplicate status formatting between wizard and panel | Extract shared `StatusFormatter` helper |
| 10 | TTS reconfig status never updates (even to "unchanged") | At minimum, show a message that TTS changes require restart |

### Could Fix (nice to have)

| # | Issue | Fix |
|---|-------|-----|
| 11 | STT provider health check on init | Send zero-audio test through pipeline |
| 12 | STT retry on transient failure | Add retry with backoff for cloud providers |
| 13 | `Locale` changes not picked up by STT reconfig | Add `nameof(AppConfig.Locale)` to `sttProps` |
| 14 | Status dot hover/tooltip with provider details | IStreamShellHost enhancement |
