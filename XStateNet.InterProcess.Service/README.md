# XStateNet InterProcess Service

A Windows Service that provides centralized message bus for InterProcess communication between XStateNet state machines.

## Quick Start

### 1. Build

```bash
dotnet build -c Release
```

### 2. Install as Windows Service

```powershell
# Run as Administrator
.\install-service.ps1
```

### 3. Verify

```powershell
Get-Service XStateNetMessageBus
```

## Features

- ✅ Named Pipe-based IPC (~50-200μs latency)
- ✅ Supports 100+ concurrent connections
- ✅ Built-in health monitoring
- ✅ Windows Event Log integration
- ✅ JSON configuration
- ✅ Production ready

## Documentation

See [INTERPROCESS_SERVICE_GUIDE.md](../INTERPROCESS_SERVICE_GUIDE.md) for complete documentation.

## Architecture

```
┌─────────────────────────────────────────┐
│  XStateNet InterProcess Service         │
│  (Windows Service)                      │
│                                         │
│  Named Pipe: XStateNet.MessageBus       │
└─────────────────┬───────────────────────┘
                  │
        ┌─────────┼─────────┐
        │         │         │
   ┌────▼───┐ ┌──▼────┐ ┌─▼─────┐
   │Process │ │Process│ │Process│
   │   1    │ │   2   │ │   3   │
   └────────┘ └───────┘ └───────┘
```

## License

MIT
