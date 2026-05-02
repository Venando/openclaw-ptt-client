# OpenClaw Push-to-Talk Client

Desktop PTT client for [OpenClaw](https://github.com/openclaw/openclaw) AI gateway. Voice-controlled agent interaction via microphone, with multi-agent session management.

## Quick Start

```bash
dotnet run
```

First run prompts for gateway URL, Groq API key, audio settings, and hotkey.

## Controls

| Key | Action |
|-----|--------|
| **Hotkey** | Push-to-talk recording (global, configurable) |
| **Esc** | Cancel recording / clear text input |
| **/crew** | List agents and manage settings (hotkey, emoji) |
| **/chat `<name>`** | Switch active agent |
| **/quit** | Exit |

## Features

- **Multi-agent**: Per-agent hotkeys and emoji via `agents.json`
- **Per-agent settings**: `/crew hotkey <name> <combo>`, `/crew emoji <name> 🐱`
- **Confirm before send**: Optional — review transcription before sending
- **StreamShell UI**: Tab-completion, styled output, command palette
- **Escape cancel**: Cancel recording without transcribing (no API cost)
- **Persistent settings**: `~/.openclaw-ptt/config.json` + `agents.json`
- **Cross-platform**: Windows, macOS, Linux

## Tech Stack

.NET 10, WebSocket, Groq Whisper (STT), Spectre.Console, StreamShell
