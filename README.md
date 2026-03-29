# OpenClaw Push-to-Talk Client

A desktop app for voice-controlled interaction with **OpenClaw** — an open-source AI assistant platform. Speak into your microphone using push-to-talk, get intelligent responses in real-time.

**Works with OpenClaw's AI agents** through your local gateway. Currently uses **Groq's Whisper API** for speech-to-text transcription.

## What It Does

1. **Push-to-talk**: Use your configured hotkey (default: Alt+=) to start/stop recording — your voice is transcribed and sent to OpenClaw
2. **AI responses**: Get answers from OpenClaw agents displayed in your console
3. **Real-time streaming**: See responses word-by-word as they're generated
4. **Groq integration**: Uses Groq's Whisper API for accurate speech-to-text with automatic retry on errors
5. **Cross-platform**: Windows, macOS, Linux — just needs a microphone
6. **Connection resilience**: Automatically reconnects if gateway server disconnects
7. **Visual feedback**: Windows shows red dot overlay when recording is active

## Quick Start

```bash
# Clone and run
git clone https://github.com/Venando/openclaw-ptt-client
cd openclaw-ptt-client
dotnet build openclaw-ptt.sln
dotnet run

# Or create standalone executable:
dotnet publish -c Release -r win-x64 --self-contained
```

First run asks for:
- **Gateway URL** (default: `ws://localhost:18789`) — your OpenClaw gateway
- **Groq API key** (for speech-to-text) — get from [groq.com](https://console.groq.com)
- **Audio settings** (usually just press Enter)
- **Hotkey configuration** — choose your push-to-talk shortcut and mode (toggle/hold-to-talk)

### Reconfiguration

To update your configuration (change Gateway URL, Groq API key, or hotkey settings):
- Press **Alt+R** while the application is running to enter reconfiguration mode
- The setup wizard will restart with your current settings as defaults

## Controls

**Important Window Focus Behavior:**
- **Push-to-talk hotkey**: Works globally from any window (system-wide)
- **Other shortcuts (T, Q, Alt+R)**: Only work when terminal console is focused

| Key | Action | Window Focus Required |
|-----|--------|----------------------|
| **Configured Hotkey** | Toggle or hold-to-talk recording (customizable, default: Alt+=) | **Any window** (global) |
| **T** | Type a text message instead | Terminal only |
| **Q** | Quit the application | Terminal only |
| **Alt+R** | Reconfigure settings | Terminal only |

**Hotkey Configuration:** During initial setup or reconfiguration (Alt+R), you can customize your push-to-talk shortcut. Choose any key combination (e.g., Ctrl+Shift+Space) and select between toggle mode or hold-to-talk mode.

**Design Note:** The push-to-talk hotkey works globally so you can use voice commands from any application. Other controls require terminal focus to prevent accidental interruptions of the application.

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
├── GroqTranscriber.cs      # Speech-to-text with retry logic
├── ConfigManager.cs        # Settings management
├── AppConfig.cs           # Configuration model
├── ConsoleUi.cs           # Console output formatting
├── Services/              # Service classes
│   ├── ConfigurationService.cs
│   ├── GatewayService.cs
│   ├── AudioService.cs
│   └── InputHandler.cs
├── VisualFeedback/        # Windows visual feedback
│   ├── IVisualFeedback.cs
│   ├── WindowsVisualFeedback.cs
│   ├── NoVisualFeedback.cs
│   └── VisualFeedbackFactory.cs
└── KeyboardListening/     # Platform-specific hotkey handling
    ├── IGlobalHotkeyHook.cs
    ├── GlobalHotkeyHookFactory.cs
    ├── WindowsHotkeyHook.cs
    ├── LinuxEvdevHotkeyHook.cs
    └── MacOsHotkeyHook.cs
```



## Need Help?

- Check that your microphone works in other apps
- Ensure you have .NET 8 SDK installed
- The setup wizard guides you through configuration
- Verify your OpenClaw gateway is running
- Check GitHub issues or pull requests for known solutions
- Connection issues? The app automatically reconnects with visible attempts in console

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