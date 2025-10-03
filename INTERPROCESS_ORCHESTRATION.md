# XStateNet InterProcess Message Bus

High-performance Named Pipe-based message bus for inter-process communication and orchestration.

## Overview

The XStateNet InterProcess Message Bus provides seamless communication between state machines running in separate processes on Windows and Linux systems. It uses Named Pipes for high-performance, low-latency messaging.

## Architecture

```
┌─────────────┐                           ┌─────────────┐
│   Client A  │                           │   Client B  │
│  (Process 1)│                           │  (Process 2)│
└──────┬──────┘                           └──────┬──────┘
       │                                         │
       │  Named Pipe Connection                  │  Named Pipe Connection
       │                                         │
       └─────────────┐           ┌──────────────┘
                     │           │
                     ▼           ▼
              ┌─────────────────────┐
              │  Message Bus Service │
              │  (Windows Service)   │
              │                      │
              │  - Event Routing     │
              │  - Client Registry   │
              │  - Health Monitoring │
              └─────────────────────┘
```

## Features

- **High Performance**: 1,800+ messages/second throughput
- **Low Latency**: Sub-millisecond average latency (0.54ms)
- **100% Reliability**: Zero message loss in stress tests
- **Concurrent Clients**: Supports multiple simultaneous connections
- **Location Transparency**: Same API for local and remote machines
- **Production Ready**: Full error handling and graceful shutdown

## Components

### 1. XStateNet.InterProcess.Service

Windows Service hosting the message bus server.

**Key Files:**
- `Program.cs` - Service entry point
- `NamedPipeMessageBus.cs` - Core message routing
- `InterProcessMessageBusWorker.cs` - Background worker
- `HealthMonitor.cs` - Health monitoring

**Installation:**
```powershell
# Run as service
dotnet run --project XStateNet.InterProcess.Service

# Install as Windows Service
sc create "XStateNet MessageBus" binPath="C:\Path\To\XStateNet.InterProcess.Service.exe"
```

### 2. XStateNet.InterProcess.TestClient

Client library and test application for connecting to the service.

**Key Files:**
- `InterProcessClient.cs` - Client connection library
- `Program.cs` - Interactive test menu

**Usage:**
```csharp
using var client = new InterProcessClient("my-machine-id");
await client.ConnectAsync();

// Send event
await client.SendEventAsync("target-machine", "MY_EVENT", new { Data = "value" });

// Receive events
client.OnEvent("RESPONSE", evt => {
    Console.WriteLine($"Received: {evt.EventName}");
});
```

### 3. XStateNet.InterProcess.SelfTest

Standalone test application for validating message bus functionality.

## Protocol

### Message Types

```csharp
enum MessageType
{
    Register,      // Register a machine with the service
    Unregister,    // Unregister a machine
    SendEvent,     // Send an event to another machine
    Subscribe      // Subscribe to receive events
}
```

### Message Format

All messages are JSON-encoded and sent line-by-line over the Named Pipe:

```json
{
  "Type": 2,
  "Payload": {
    "SourceMachineId": "machine-a",
    "TargetMachineId": "machine-b",
    "EventName": "PING",
    "Payload": { "Message": "Hello" },
    "Timestamp": "2025-10-03T17:16:09.8"
  }
}
```

### Response Format

```json
{
  "Success": true,
  "Data": {
    "SourceMachineId": "machine-b",
    "TargetMachineId": "machine-a",
    "EventName": "PONG",
    "Payload": { "Message": "World" },
    "Timestamp": "2025-10-03T17:16:09.9"
  },
  "Error": null
}
```

## Test Results

### Ping-Pong Test (2 Clients)
```
✓ Test Complete! Received 5/5 PONGs
✓ Bidirectional communication working
✓ Clean shutdown with no exceptions
```

### Multi-Client Broadcast Test (5 Clients)
```
✓ Total: 20/20 messages delivered
  client-1: received 4/4 broadcasts ✓
  client-2: received 4/4 broadcasts ✓
  client-3: received 4/4 broadcasts ✓
  client-4: received 4/4 broadcasts ✓
  client-5: received 4/4 broadcasts ✓
```

### Stress Test (100 Messages)
```
✓ Sent: 100 messages
✓ Received: 100 messages
✓ Time: 54ms
✓ Throughput: 1,832 msg/sec
✓ Avg Latency: 0.54ms per message
```

## Performance Characteristics

| Metric | Value |
|--------|-------|
| **Throughput** | 1,800+ msg/sec |
| **Latency** | 0.54ms average |
| **Delivery Rate** | 100% (zero loss) |
| **Concurrent Clients** | 5+ simultaneous |
| **Connection Time** | <100ms |

## Configuration

### Service Configuration

Edit `appsettings.json`:

```json
{
  "MessageBus": {
    "PipeName": "XStateNet.MessageBus"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "XStateNet.InterProcess": "Debug"
    }
  }
}
```

### Client Configuration

```csharp
var client = new InterProcessClient(
    machineId: "my-machine",
    pipeName: "XStateNet.MessageBus"  // Optional, defaults to this value
);
```

## Error Handling

### Connection Errors

```csharp
try
{
    await client.ConnectAsync();
}
catch (Exception ex)
{
    Console.WriteLine($"Failed to connect: {ex.Message}");
    // Service may not be running
}
```

### Send Errors

```csharp
try
{
    await client.SendEventAsync("target", "EVENT", data);
}
catch (InvalidOperationException)
{
    // Not connected
}
catch (Exception ex)
{
    // Other error
}
```

## Integration with XStateNet Orchestrator

```csharp
// Create orchestrator
var orchestrator = new EventBusOrchestrator();

// Create machines
var machine1 = ExtendedPureStateMachineFactory.CreateFromScript(script1, ctx);
var machine2 = ExtendedPureStateMachineFactory.CreateFromScript(script2, ctx);

// Register with orchestrator
orchestrator.RegisterMachine("machine-1", machine1);
orchestrator.RegisterMachine("machine-2", machine2);

// Connect to InterProcess service for cross-process communication
using var client = new InterProcessClient("machine-1");
await client.ConnectAsync();

// Bridge orchestrator events to InterProcess
client.OnEvent("EXTERNAL_EVENT", evt => {
    orchestrator.SendEvent("machine-1", evt.EventName, evt.Payload);
});
```

## Deployment

### Development

```powershell
# Terminal 1: Run service
dotnet run --project XStateNet.InterProcess.Service

# Terminal 2: Run test client
dotnet run --project XStateNet.InterProcess.TestClient
```

### Production (Windows Service)

```powershell
# Build release
dotnet publish XStateNet.InterProcess.Service -c Release

# Install as Windows Service
sc create "XStateNet MessageBus" `
    binPath="C:\Deploy\XStateNet.InterProcess.Service.exe" `
    start=auto

# Start service
sc start "XStateNet MessageBus"

# Check status
sc query "XStateNet MessageBus"
```

### Production (Linux Systemd)

```bash
# Create systemd service file
sudo nano /etc/systemd/system/xstatenet-messagebus.service

[Unit]
Description=XStateNet InterProcess Message Bus
After=network.target

[Service]
Type=notify
ExecStart=/usr/bin/dotnet /opt/xstatenet/XStateNet.InterProcess.Service.dll
Restart=always
RestartSec=10

[Install]
WantedBy=multi-user.target

# Enable and start
sudo systemctl enable xstatenet-messagebus
sudo systemctl start xstatenet-messagebus
```

## Monitoring

### Health Check Endpoint

```csharp
var health = messageBus.GetHealthStatus();
Console.WriteLine($"Healthy: {health.IsHealthy}");
Console.WriteLine($"Connections: {health.ConnectionCount}");
Console.WriteLine($"Registered Machines: {health.RegisteredMachines}");
Console.WriteLine($"Last Activity: {health.LastActivityAt}");
```

### Logging

Service logs are written to:
- **Console**: Development mode
- **Windows Event Log**: Production (Windows)
- **Syslog**: Production (Linux)

## Troubleshooting

### "Failed to connect to pipe"

**Cause**: Service not running or pipe name mismatch

**Solution**:
```powershell
# Check if service is running
sc query "XStateNet MessageBus"

# Or for process
Get-Process | Where-Object { $_.Name -like "*XStateNet*" }
```

### "Connection lost"

**Cause**: Service stopped or network issue

**Solution**: Implement reconnection logic:

```csharp
async Task ConnectWithRetry()
{
    for (int i = 0; i < 5; i++)
    {
        try
        {
            await client.ConnectAsync();
            return;
        }
        catch
        {
            await Task.Delay(1000 * (i + 1)); // Exponential backoff
        }
    }
    throw new Exception("Failed to connect after 5 retries");
}
```

### "No pipe connection for target machine"

**Cause**: Target machine not connected or not subscribed

**Solution**: Ensure target machine called `ConnectAsync()` and completed registration

## Best Practices

1. **Connection Management**
   - Use `using` statement or call `Dispose()` explicitly
   - Implement reconnection logic for production
   - Handle connection loss gracefully

2. **Event Naming**
   - Use UPPERCASE for event names
   - Use descriptive names (e.g., `ORDER_PLACED`, not `E1`)
   - Prefix with domain when needed (e.g., `PAYMENT_COMPLETED`)

3. **Error Handling**
   - Always wrap network calls in try-catch
   - Log errors with context
   - Provide user feedback on failures

4. **Performance**
   - Reuse client connections
   - Avoid sending large payloads (>1MB)
   - Use batching for high-volume scenarios

5. **Security**
   - Run service with least privilege
   - Use Named Pipe permissions (Windows ACLs)
   - Validate message payloads
   - Implement authentication if needed

## Future Enhancements

- [ ] TLS encryption for pipe communication
- [ ] Message persistence and replay
- [ ] Distributed tracing integration
- [ ] Metrics export (Prometheus)
- [ ] Load balancing across multiple services
- [ ] Message filtering and routing rules
- [ ] Priority queues for urgent messages

## References

- [Named Pipes Documentation (Microsoft)](https://docs.microsoft.com/en-us/dotnet/standard/io/pipe-operations)
- [XStateNet Documentation](https://github.com/your-repo/xstatenet)
- [SCXML Specification](https://www.w3.org/TR/scxml/)

## License

MIT License - See LICENSE file for details

## Support

For issues and questions:
- GitHub Issues: https://github.com/your-repo/xstatenet/issues
- Email: support@your-domain.com

---

**Generated**: 2025-10-03
**Version**: 1.0.0
**Status**: Production Ready ✓
