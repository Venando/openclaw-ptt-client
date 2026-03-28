# Voice Control for AI Assistant

A simple desktop app that lets you talk to an AI assistant using push-to-talk. Speak into your microphone, get intelligent responses in real-time.

**For everyone** — no need to know what "OpenClaw" is. Just install, press a key, and talk.

## What It Does

1. **Push-to-talk**: Hold a key (like Space), speak, release — your voice is sent for processing
2. **AI responses**: Get answers from an AI assistant displayed in your console
3. **Real-time streaming**: See responses word-by-word as they're generated
4. **Works anywhere**: Windows, macOS, Linux — just needs a microphone

## Quick Start

```bash
# Clone and run
git clone https://github.com/Venando/openclaw-ptt-client
cd openclaw-ptt-client
dotnet run
```

First run asks for:
- **Gateway URL** (default: `ws://localhost:18789`)
- **API key** (for speech-to-text)
- **Audio settings** (usually just press Enter)

## Controls

| Key | Action |
|-----|--------|
| **Space** | Push-to-talk (hold to record, release to send) |
| **T** | Type a text message instead |
| **Q** | Quit the application |

## Features

- **Voice commands**: Natural conversation with AI assistant
- **Clean console UI**: Color-coded responses with proper formatting
- **Cross-platform**: Works on Windows, macOS, and Linux
- **No complex setup**: Guided configuration wizard
- **Persistent settings**: Saves your preferences automatically

## Technical Details

- **Language**: C# (.NET 8)
- **Audio**: System audio APIs + Groq Whisper for transcription
- **Communication**: WebSocket connection to AI gateway
- **Storage**: Config in `~/.openclaw-ptt/config.json`

## Project Structure

```
src/
├── Program.cs              # Main application loop
├── GatewayClient.cs        # AI communication
├── AudioRecorder.cs        # Microphone handling
├── GroqTranscriber.cs      # Speech-to-text
├── ConfigManager.cs        # Settings management
└── AppConfig.cs           # Configuration model
```

## Building

```bash
dotnet build
# Or create standalone executable:
dotnet publish -c Release -r win-x64 --self-contained
```

## Need Help?

- Check that your microphone works in other apps
- Ensure you have .NET 8 SDK installed
- The setup wizard guides you through configuration

---

*Simple voice control for AI assistants. Press a key, start talking.*