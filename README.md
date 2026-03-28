# OpenClaw Push-to-Talk Client

A desktop app for voice-controlled interaction with **OpenClaw** — an open-source AI assistant platform. Speak into your microphone using push-to-talk, get intelligent responses in real-time.

**Works with OpenClaw's AI agents** through your local gateway. Currently uses **Groq's Whisper API** for speech-to-text transcription.

## What It Does

1. **Push-to-talk toggle**: Press Alt+= to start recording, press again to stop — your voice is transcribed and sent to OpenClaw
2. **AI responses**: Get answers from OpenClaw agents displayed in your console
3. **Real-time streaming**: See responses word-by-word as they're generated
4. **Groq integration**: Uses Groq's Whisper API for accurate speech-to-text
5. **Cross-platform**: Windows, macOS, Linux — just needs a microphone

## Quick Start

```bash
# Clone and run
git clone https://github.com/Venando/openclaw-ptt-client
cd openclaw-ptt-client
dotnet run
```

First run asks for:
- **Gateway URL** (default: `ws://localhost:18789`) — your OpenClaw gateway
- **Groq API key** (for speech-to-text) — get from [groq.com](https://console.groq.com)
- **Audio settings** (usually just press Enter)

### Reconfiguration

If you need to update your configuration (e.g., change Gateway URL or Groq API key), you can:

- Run the application with `--reconfigure` flag: `dotnet run -- --reconfigure`
- Or press `R` when the application starts (within 3 seconds) to enter setup wizard.

The existing configuration will be used as default values; device identity and tokens are preserved unless explicitly changed.

## Controls

| Key | Action |
|-----|--------|
| **Alt+=** | Toggle recording (press to start, press again to stop) |
| **T** | Type a text message instead |
| **Q** | Quit the application |

## Features

- **OpenClaw integration**: Connects to your local OpenClaw gateway
- **Groq Whisper API**: High-quality speech-to-text transcription
- **Voice commands**: Natural conversation with AI agents
- **Clean console UI**: Color-coded responses with proper formatting
- **Cross-platform**: Windows, macOS, and Linux support
- **Guided setup**: Configuration wizard for easy onboarding
- **Persistent settings**: Saves preferences in `~/.openclaw-ptt/`

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
- Verify your OpenClaw gateway is running

## Planned Improvements

From the code comments (`Program.cs`):

1. **Shortcut settings**: Customizable hotkeys, add hold-to-talk option (currently toggle-only)
2. **Config reconfigure** (implemented): Option to re-run setup without deleting config
3. **Transcriber selection**: Choose between different speech-to-text services
4. **Long speech handling**: Chunked transcription for extended recordings
5. **Visual feedback**: Recording indicator outside terminal (e.g., red dot)
6. **System tray**: Minimize to tray when not in use
7. **Text-to-speech**: Voice responses from agent (optional)
8. **Raw audio streaming**: Send audio directly to OpenClaw for interpretation
9. **Session management**: Select different OpenClaw sessions (currently "main" only)
10. **Code cleanup**: Refactor Program.cs and other files
11. **Cross-platform testing**: Verify Linux/macOS compatibility
12. **Exit handling**: Fix Ctrl+C during config setup
13. **Connection resilience**: Better handling for gateway restarts (currently stops application)

## Contributing

Found a bug? Have a feature request? Open an issue or submit a pull request.

---

*Voice control for OpenClaw AI assistants. Press Alt+= to start recording, press again to send.*