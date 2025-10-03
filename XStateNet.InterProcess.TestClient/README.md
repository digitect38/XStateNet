# XStateNet InterProcess Test Client

Interactive test application for the XStateNet InterProcess Service.

## Quick Start

### 1. Build

```bash
dotnet build -c Release
```

### 2. Start Service

In a separate terminal:

```powershell
cd ..\XStateNet.InterProcess.Service
.\run-console.bat
```

### 3. Run Tests

```powershell
.\run-tests.bat
```

Or use command line:

```powershell
cd bin\Release\net9.0

# Interactive menu
.\XStateNet.InterProcess.TestClient.exe

# Specific tests
.\XStateNet.InterProcess.TestClient.exe ping    # Ping-pong test
.\XStateNet.InterProcess.TestClient.exe multi   # Multi-client test
.\XStateNet.InterProcess.TestClient.exe stress  # Stress test
```

## Test Scenarios

### 1. Ping-Pong Test

Two clients exchange messages back and forth.

- **Purpose**: Verify bi-directional communication
- **Duration**: ~5 seconds
- **Expected**: 5/5 messages delivered

### 2. Multi-Client Test

Five clients broadcast messages to each other.

- **Purpose**: Test message routing with multiple clients
- **Duration**: ~10 seconds
- **Expected**: 20/20 messages delivered (each client receives 4)

### 3. Stress Test

Single sender sends 100 messages to receiver as fast as possible.

- **Purpose**: Measure throughput and latency
- **Duration**: ~2 seconds
- **Expected**: > 1000 msg/sec throughput, < 5ms latency

### 4. Custom Test

Interactive shell for manual testing.

- **Purpose**: Custom scenarios and debugging
- **Commands**: `send <target> <event> [message]`, `quit`

## Architecture

```
┌─────────────────────┐
│  Test Client        │
│  (This App)         │
└──────────┬──────────┘
           │
           │ Named Pipe
           ↓
┌─────────────────────┐
│  InterProcess       │
│  Service            │
│  (Message Bus)      │
└─────────────────────┘
```

## Documentation

See [TESTING_INTERPROCESS_SERVICE.md](../TESTING_INTERPROCESS_SERVICE.md) for complete testing guide.

## Troubleshooting

**"Failed to connect to pipe"**
- Make sure the InterProcess Service is running
- Check the pipe name matches (default: `XStateNet.MessageBus`)

**Messages not being received**
- Check service logs for errors
- Verify both clients are registered
- Add delay between registration and sending

## License

MIT
