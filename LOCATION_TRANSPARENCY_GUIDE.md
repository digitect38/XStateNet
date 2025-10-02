# Location Transparency in XStateNet

## Overview

Location Transparency allows the same application code to work across different deployment scenarios:
- **InProcess**: State machines in the same process (using EventBusOrchestrator)
- **InterProcess**: State machines across processes on the same machine (using IPC)
- **InterNode**: State machines distributed across network nodes (using TCP/RabbitMQ/Redis)

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                   Application Code                           │
│              (Same code, any environment)                    │
└─────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────┐
│                   IMessageBus Interface                      │
│           (Unified abstraction for all transports)           │
└─────────────────────────────────────────────────────────────┘
                            │
        ┌───────────────────┼───────────────────┐
        ▼                   ▼                   ▼
┌───────────────┐  ┌────────────────┐  ┌──────────────────┐
│ InProcessBus  │  │ InterProcessBus│  │  InterNodeBus    │
│ (Orchestrator)│  │ (Named Pipes)  │  │ (TCP/RabbitMQ)   │
└───────────────┘  └────────────────┘  └──────────────────┘
```

## Example: PingPong with Location Transparency

### 1. Same Code, Any Environment

```csharp
using XStateNet.Orchestration;
using System.Threading.Tasks;

public class LocationTransparentPingPong
{
    public static async Task Run(TransportType transport)
    {
        // Create factory with desired transport
        var factory = new UnifiedStateMachineFactory(transport);

        // Create Ping machine
        var pingActions = new Dictionary<string, Action<OrchestratedContext>>
        {
            ["sendPong"] = ctx => ctx.RequestSend("pong-machine", "PONG")
        };

        var pingMachine = await factory.CreateAsync(
            machineId: "ping-machine",
            jsonScript: @"{
                id: 'ping',
                initial: 'idle',
                states: {
                    idle: {
                        on: { START: { target: 'active', actions: 'sendPong' } }
                    },
                    active: {
                        on: { PING: { target: 'active', actions: 'sendPong' } }
                    }
                }
            }",
            actions: pingActions
        );

        // Create Pong machine
        var pongActions = new Dictionary<string, Action<OrchestratedContext>>
        {
            ["sendPing"] = ctx => ctx.RequestSend("ping-machine", "PING")
        };

        var pongMachine = await factory.CreateAsync(
            machineId: "pong-machine",
            jsonScript: @"{
                id: 'pong',
                initial: 'waiting',
                states: {
                    waiting: {
                        on: { PONG: { target: 'waiting', actions: 'sendPing' } }
                    }
                }
            }",
            actions: pongActions
        );

        // Start the sequence - same code works in all environments!
        await pingMachine.SendToAsync("ping-machine", "START");

        await Task.Delay(1000);
    }
}
```

### 2. Run In-Process (Development)

```csharp
await LocationTransparentPingPong.Run(TransportType.InProcess);
// Uses EventBusOrchestrator
// Fast, synchronous, perfect for unit tests and development
```

### 3. Run Inter-Process (Same Machine, Different Processes)

```csharp
await LocationTransparentPingPong.Run(TransportType.InterProcess);
// Uses named pipes or shared memory
// Good for process isolation on same machine
```

### 4. Run Inter-Node (Distributed)

```csharp
await LocationTransparentPingPong.Run(TransportType.InterNode);
// Uses TCP/RabbitMQ/Redis
// Full distributed deployment across network
```

## Configuration Examples

### Development: Fast In-Process

```csharp
var factory = new UnifiedStateMachineFactory(TransportType.InProcess);
// OR with custom config
var bus = new InProcessMessageBus(new OrchestratorConfig
{
    PoolSize = 4,
    EnableLogging = true
});
var factory = new UnifiedStateMachineFactory(bus, TransportType.InProcess);
```

### Testing: Isolated Inter-Process

```csharp
var factory = new UnifiedStateMachineFactory(TransportType.InterProcess);
// Tests actual IPC without network complexity
```

### Production: Distributed Inter-Node

```csharp
var factory = new UnifiedStateMachineFactory(TransportType.InterNode);
// OR with custom transport (RabbitMQ example)
var bus = new RabbitMQMessageBus("amqp://localhost");
var factory = new UnifiedStateMachineFactory(bus, TransportType.InterNode);
```

## Benefits

### 1. **Write Once, Deploy Anywhere**
```csharp
// Same application code
var machine = await factory.CreateAsync("my-machine", jsonScript, actions);

// Works in all environments:
// - Local development (InProcess)
// - Integration testing (InterProcess)
// - Production (InterNode)
```

### 2. **Easy Environment Switching**
```csharp
// Change via configuration, not code
var transport = config.GetValue<TransportType>("Transport");
var factory = new UnifiedStateMachineFactory(transport);
```

### 3. **Gradual Migration Path**
```csharp
// Start InProcess in development
TransportType.InProcess

// Move to InterProcess for staging
TransportType.InterProcess

// Deploy as InterNode in production
TransportType.InterNode
```

### 4. **Testability**
```csharp
// Unit tests use InProcess (fast, synchronous)
[Fact]
public async Task TestPingPong()
{
    var factory = new UnifiedStateMachineFactory(TransportType.InProcess);
    // ... test with real orchestrator, no mocks needed
}

// Integration tests use InterProcess (realistic IPC)
[Fact]
public async Task TestCrossProcess()
{
    var factory = new UnifiedStateMachineFactory(TransportType.InterProcess);
    // ... test actual inter-process communication
}
```

## Implementation Status

### ✅ Completed
- IMessageBus interface
- InProcessMessageBus (wraps EventBusOrchestrator)
- DistributedMessageBus (wraps OptimizedInMemoryEventBus)
- UnifiedStateMachineFactory
- UnifiedStateMachine wrapper

### 🚧 TODO
- InterProcess transport (named pipes / shared memory)
- RabbitMQ transport adapter
- Redis transport adapter
- TCP transport adapter
- Service discovery for distributed scenarios
- Connection pooling and retry logic
- Metrics and monitoring across transports

## Migration Guide

### From Old Pattern (Obsolete)
```csharp
// OLD: Direct CreateFromScript (can deadlock)
var machine = StateMachineFactory.CreateFromScript(json, false, false, actions);
machine.Send("EVENT"); // Direct send - risky!
```

### To Orchestrated Pattern (Recommended)
```csharp
// NEW: Orchestrated with location transparency
var factory = new UnifiedStateMachineFactory(TransportType.InProcess);
var machine = await factory.CreateAsync("my-machine", json, actions);
await machine.SendToAsync("target-machine", "EVENT"); // Orchestrated - safe!
```

### Benefits of New Pattern
1. **No deadlocks**: All communication goes through message bus
2. **Location transparent**: Change transport without changing code
3. **Observable**: Single point to monitor all communication
4. **Testable**: Easy to test with different transports
5. **Scalable**: Start local, scale to distributed

## Advanced Scenarios

### Custom Transport Implementation

```csharp
public class CustomMessageBus : IMessageBus
{
    // Implement your own transport (e.g., gRPC, SignalR, WebSockets)
    public Task SendEventAsync(string source, string target, string eventName, object? payload)
    {
        // Your custom implementation
    }
    // ... implement other methods
}

// Use it
var customBus = new CustomMessageBus();
var factory = new UnifiedStateMachineFactory(customBus, TransportType.InterNode);
```

### Hybrid Deployments

```csharp
// Some machines in-process, others distributed
var localFactory = new UnifiedStateMachineFactory(TransportType.InProcess);
var remoteFactory = new UnifiedStateMachineFactory(TransportType.InterNode);

var localMachine = await localFactory.CreateAsync("local-1", json, actions);
var remoteMachine = await remoteFactory.CreateAsync("remote-1", json, actions);

// They can still communicate through unified interface!
await localMachine.SendToAsync("remote-1", "EVENT");
```

## Performance Characteristics

| Transport      | Latency  | Throughput | Complexity | Use Case                    |
|----------------|----------|------------|------------|-----------------------------|
| InProcess      | ~1μs     | Very High  | Low        | Development, unit tests     |
| InterProcess   | ~100μs   | High       | Medium     | Process isolation, staging  |
| InterNode (TCP)| ~1-10ms  | Medium     | High       | Distributed, cloud          |
| InterNode (MQ) | ~10-50ms | High       | High       | Enterprise, decoupled       |

## Conclusion

Location Transparency enables:
- **Flexibility**: Deploy the same code in different environments
- **Simplicity**: One API, multiple transports
- **Testability**: Easy to test locally and in production-like environments
- **Scalability**: Start simple, scale when needed
- **Maintainability**: Change deployment without changing code

The unified factory pattern makes it easy to write state machines once and deploy them anywhere!
