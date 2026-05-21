# Delta Printing Pipeline Trace

## Overview

The pipeline goes: **Gateway WebSocket → SessionMessageHandler → GatewayService → AgentOutputCoordinator → ReplyStreamCoordinator → AgentReplyFormatter → IFormattedOutput → StreamShellHost → Console**

## 1. Gateway Events (SessionMessageHandler)

Two event types from the Gateway:

### `agent` events (real-time streaming)
```
{ "data": { "phase": "start" } }     → RaiseAgentReplyDeltaStart()
{ "data": { "delta": "Hello" } }     → RaiseAgentReplyDelta("Hello")
{ "data": { "phase": "end" } }       → RaiseAgentReplyDeltaEnd()
```

### `session.message` events (complete replies)
```
{ "message": { "role": "assistant", "content": [...] } }
  → text blocks → RaiseAgentReplyFull(text)
```

**Config gate**: `RealTimeReplyOutput = (ReplyDisplayMode == Delta || Both)`
- If `true`: `agent` events fire delta lifecycle. `session.message` skips (streaming handles it).
- If `false`: `session.message` simulates delta by firing `Start → Full → End` for each text block.

**Default**: `ReplyDisplayMode = Both` → `RealTimeReplyOutput = true` ✅

## 2. GatewayService → AgentOutputCoordinator

Events are wired directly:
```csharp
AgentReplyDeltaStart → OnAgentReplyDeltaStart() → ReplyStreamCoordinator.OnDeltaStart()
AgentReplyDelta      → OnAgentReplyDelta(delta)  → ReplyStreamCoordinator.OnDelta(delta)
AgentReplyDeltaEnd   → OnAgentReplyDeltaEnd()     → ReplyStreamCoordinator.OnDeltaEnd()
AgentReplyFull       → OnAgentReplyFull(body)     → ReplyStreamCoordinator.OnFullReply(body)
```

## 3. ReplyStreamCoordinator

### OnDeltaStart()
- Resets `_accumulatedText = ""`, `_isDeltaStarted = true`, `_formatter = null`

### OnDelta(delta)
1. `_accumulatedText += delta` (for TTS playback at end)
2. `EnsurePrefixPrinted()` — lazy-creates the formatter on first delta
3. If `_formatter != null`: `_formatter.ProcessDelta(delta)`
4. If `_formatter == null`: `_console.PrintAgentReplyDelta(prefix, delta, suffix)` — **bypasses formatter entirely**

### OnDeltaEnd()
1. `_isDeltaStarted = false`
2. If `_formatter != null`: `_formatter.Finish()` then clear it
3. `_prefixPrinted = false`

### OnFullReply(body)
1. Converts markdown → Spectre markup via `MarkdownToSpectreConverter`
2. `EnsurePrefixPrinted()`
3. If `_formatter != null`: `_formatter.ProcessMarkupDelta(markup)` → `_formatter.Finish()` → flush capturing console
4. If `_formatter == null`: `_console.PrintAgentReplyWithMarkdown(prefix, markup)`

### EnsurePrefixPrinted() — CRITICAL
Builds the agent prefix line:
```csharp
_currentPrefix = "  🤖 [cyan]Assistant[/]: ";
```

Then if `EnableWordWrap`:
```csharp
if (shellHost != null) {
    _capturingConsole = new StreamShellCapturingConsole(shellHost);
    _formatter = new AgentReplyFormatter(prefix, margin, prefixAlreadyPrinted: true, output: _capturingConsole);
} else {
    _capturingConsole = null;
    _formatter = new AgentReplyFormatter(prefix, margin, prefixAlreadyPrinted: true, output: null!);  // ← BUG
}
```

If no StreamShell host, `output: null!` — will crash on any write/flush.

## 4. AgentReplyFormatter (Line-Buffered)

### What I Changed
- **Before**: Each character/word immediately called `_output.Write()` — printed word-by-word as deltas arrived.
- **After**: All output accumulates in `_lineBuffer`. Only flushed on:
  - `WriteNewLine()` — line is complete, flush + newline
  - `Finish()` — stream ended, flush remaining + newline
  - Table deferral exit — flush deferred buffer

### Table Deferral (ProcessDelta only)
When a delta contains `|...|` lines (markdown table):
- `_deferred = true` — completed lines go to `_deferredBuffer` instead of output
- When a non-table, non-empty line appears: `_deferred = false`, flush `_deferredBuffer`
- On `Finish()`: always flush deferred buffer

This prevents a half-built table from rendering line-by-line.

### ProcessMarkupDelta
Does NOT have table deferral — tables are already converted to Spectre markup by `MarkdownToSpectreConverter` before this method is called. Same line-buffering applies though.

## 5. IFormattedOutput Destinations

### StreamShellCapturingConsole (when StreamShell host exists)
- `Write()` → appends to internal `StringBuilder`
- `WriteLine()` → appends `\n`
- `FlushToStreamShell()` → splits on `\n`, sends each line to `_shellHost.AddMessage()`

### null! (when NO StreamShell host) — BUG
- Any write/flush → **NullReferenceException**

## 6. ColorConsole (the actual IColorConsole)

### PrintAgentReplyDelta (when formatter is null / word wrap disabled)
```csharp
void PrintAgentReplyDelta(prefix, delta, suffix) {
    ShellMsg(Markup.Escape(delta));  // Sends EACH delta chunk directly to StreamShell
}
```
This is the "word by word" output — each delta chunk (which may be a single character or word) is sent immediately.

### PrintAgentReplyWithMarkdown (full reply, no formatter)
```csharp
void PrintAgentReplyWithMarkdown(prefix, body) {
    ShellMsg($"{prefix}{body}");  // Sends entire pre-formatted markup at once
}
```

## Required Config Settings

| Setting | Default | Required For Line-by-Line |
|---------|---------|---------------------------|
| `ReplyDisplayMode` | `Both` | `Delta` or `Both` (enables `RealTimeReplyOutput`) |
| `EnableWordWrap` | `true` | `true` (creates `AgentReplyFormatter`) |
| StreamShell host | — | Must be non-null (otherwise `null!` crash) |

### What happens with different configs:

**`ReplyDisplayMode = Both` + `EnableWordWrap = true` + StreamShell present** ← IDEAL
- Delta events fire → formatter buffers line-by-line → capturing console → StreamShell

**`ReplyDisplayMode = Both` + `EnableWordWrap = false`**
- Delta events fire → `PrintAgentReplyDelta()` → each chunk prints immediately (word-by-word)

**`ReplyDisplayMode = Full`**
- No delta events → only `AgentReplyFull` at end → full text through `ProcessMarkupDelta`

**`ReplyDisplayMode = Both` + `EnableWordWrap = true` + NO StreamShell** ← CRASH
- `null!` passed as `_output` → NRE on first flush

## Issues Found

### Issue 1: null! Output Crash (PRE-EXISTING)
`ReplyStreamCoordinator.EnsurePrefixPrinted()` passes `output: null!` when `GetStreamShellHost()` returns null. My line-buffering delays the crash but doesn't prevent it.

**Fix**: Don't create a formatter when there's no valid output destination.

### Issue 2: PrintAgentReplyDelta Bypasses Formatter
When `EnableWordWrap = false` or no StreamShell host, `ColorConsole.PrintAgentReplyDelta()` sends each chunk directly. This is word-by-word output.

**Not a bug** — this is the intended fallback path. Line-by-line requires word wrap enabled.

### Issue 3: ProcessMarkupDelta Has No Table Deferral
`ProcessMarkupDelta` (used for full replies) doesn't check for table lines because tables are already converted to Spectre box-drawing markup by `MarkdownToSpectreConverter` before it arrives. This is correct.

## Fix Applied

`ReplyStreamCoordinator.EnsurePrefixPrinted()` — don't create formatter when no StreamShell host. Let it fall through to `PrintAgentReplyDelta` / `PrintAgentReplyWithMarkdown`.
