# OpenClaw Push-to-Talk Console Client

A cross-platform C# console application for voice interaction with OpenClaw agents via push-to-talk.

(READ me needs rework)

## Features

- **Push-to-talk voice recording** (Space/R key)
- **Text message fallback** (T key)
- **Cross-platform audio capture**:
  - Windows: NAudio
  - macOS/Linux: sox or arecord fallback
- **Full Gateway v3 protocol** with device identity (ECDSA P-256)
- **Auto-approval** for exec requests
- **Persistent configuration** with setup wizard

## Project Structure

```
openclaw-ptt/
├── OpenClawPTT.csproj          # .NET 8 project file
├── README.md                   # This file
└── src/
    ├── AppConfig.cs            # Configuration model
    ├── ConfigManager.cs        # Load/save/validate config
    ├── DeviceIdentity.cs       # ECDSA keypair & signing
    ├── GatewayClient.cs        # WebSocket + protocol
    ├── AudioRecorder.cs        # NAudio + CLI recording
    └── Program.cs              # Entry point + PTT loop
```

## Requirements

- **.NET 8 SDK**
- **Audio recording**:
  - Windows: NAudio (auto-installed via NuGet)
  - macOS: `brew install sox`
  - Linux: `apt install sox` or `arecord` (ALSA)

## Building

```bash
cd openclaw-ptt
dotnet build
dotnet publish -c Release -r win-x64 --self-contained false
```

## Running

```bash
cd openclaw-ptt
dotnet run
```

First run will prompt for:
- Gateway URL (default: `ws://localhost:4440`)
- Auth token (or device token)
- Audio settings (sample rate, max recording seconds)

## Controls

```
╔══════════════════════════════════════════╗
║  Push-to-Talk ready                      ║
╠══════════════════════════════════════════╣
║  [Alt + "="]  Toggle recording           ║
║  [T]        Type a text message          ║
║  [Q]        Quit                         ║
╚══════════════════════════════════════════╝
```

## Configuration

Stored in `~/.openclaw-ptt/config.json`:

```json
{
  "gatewayUrl": "ws://localhost:4440",
  "authToken": "your_token_here",
  "deviceToken": "auto-populated",
  "locale": "en-US",
  "sampleRate": 16000,
  "channels": 1,
  "bitsPerSample": 16,
  "maxRecordSeconds": 120
}
```

## Device Identity

Generates a persistent ECDSA P-256 keypair at `~/.openclaw-ptt/device.key`.  
Used for Gateway v3 authentication — device token is saved after first connection.

## License

OpenClaw project — see OpenClaw repository for license details.