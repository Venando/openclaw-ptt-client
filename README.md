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
| **Customizable Hotkey** | Toggle or hold-to-talk recording (default: Alt+=) |
| **T** | Type a text message instead |
| **Q** | Quit the application |
| **Alt+R** | Reconfigure settings |

**Hotkey Configuration:** You can now customize your push-to-talk shortcut during setup. Choose any key combination (e.g., Ctrl+Shift+Space) and select between toggle mode or hold-to-talk mode.

## Features

- **OpenClaw integration**: Connects to your local OpenClaw gateway
- **Groq Whisper API**: High-quality speech-to-text transcription with error retry
- **Voice commands**: Natural conversation with AI agents
- **Clean console UI**: Color-coded responses with proper formatting
- **Cross-platform**: Windows, macOS, and Linux support
- **Guided setup**: Configuration wizard for easy onboarding
- **Persistent settings**: Saves preferences in `~/.openclaw-ptt/`
- **Customizable shortcuts**: Configure any hotkey with toggle or hold-to-talk mode
- **Connection resilience**: Automatic reconnection with configurable retry delay
- **Windows visual feedback**: Red dot overlay shows when recording is active

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

## Recent Improvements (Implemented)

1. ✅ **Shortcut settings**: Customizable hotkeys with hold-to-talk option
2. ✅ **Connection resilience**: Automatic reconnection with configurable retry delay (default: 1.5s)
3. ✅ **Visual feedback**: Windows-only red dot overlay when recording is active
4. ✅ **Groq error handling**: Retry logic for API failures with exponential backoff
5. ✅ **Code cleanup**: Refactored Program.cs into service classes for better architecture

## Planned Improvements

From the code comments (`Program.cs`):

1. **Transcriber selection**: Choose between different speech-to-text services
2. **Long speech handling**: Chunked transcription for extended recordings
3. **System tray**: Minimize to tray when not in use
4. **Text-to-speech**: Voice responses from agent (optional)
5. **Raw audio streaming**: Send audio directly to OpenClaw for interpretation
6. **Session management**: Select different OpenClaw sessions (currently "main" only)
7. **Cross-platform testing**: Verify Linux/macOS compatibility
8. **Exit handling**: Fix Ctrl+C during config setup

## Contributing

Found a bug? Have a feature request? Open an issue or submit a pull request.

---

*Voice control for OpenClaw AI assistants. Configure your preferred hotkey for push-to-talk.*