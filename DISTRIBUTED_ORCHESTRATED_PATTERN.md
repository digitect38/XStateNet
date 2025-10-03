## Distributed Orchestrated Pattern

# Unified Orchestrated Pattern: Local + Distributed

## Overview

The distributed state machine pattern now follows the **same orchestrated style** as local EventBusOrchestrator, providing consistency across all deployment scenarios.

## Key Principles

1. **No Direct Sends**: Actions never call `machine.Send()` directly
2. **Context-Based Communication**: Use `ctx.RequestSend()` in actions
3. **Location Transparent**: Same pattern works InProcess, InterProcess, InterNode
4. **Deadlock-Free**: All communication goes through message bus infrastructure

## Architecture Comparison

### Before (Inconsistent)

```
┌─────────────────────────────────────────────────────────────┐
│  InProcess: EventBusOrchestrator (Orchestrated Pattern)     │
│  - Uses InProcessOrchestratedContext                                  │
│  - Actions: ctx.RequestSend()                                │
│  - Deadlock-free                                              │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│  Distributed: DistributedStateMachine (Old Pattern)          │
│  - Uses ActionMap with StateMachine parameter                │
│  - Actions: machine.Send() - DANGEROUS!                      │
│  - Potential deadlocks                                        │
└─────────────────────────────────────────────────────────────┘
```

### After (Consistent) ✅

```
┌─────────────────────────────────────────────────────────────┐
│  InProcess: EventBusOrchestrator                             │
│  - Uses InProcessOrchestratedContext                                  │
│  - Actions: ctx.RequestSend()                                │
│  - Deadlock-free                                              │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│  Distributed: DistributedPureStateMachineFactory             │
│  - Uses DistributedOrchestratedContext                       │
│  - Actions: ctx.RequestSend()                                │
│  - Deadlock-free + Location transparent                      │
└─────────────────────────────────────────────────────────────┘
```

## API Comparison

### InProcess (Local Orchestrated)

```csharp
// Create with EventBusOrchestrator
var orchestrator = new EventBusOrchestrator();

var actions = new Dictionary<string, Action<InProcessOrchestratedContext>>
{
    ["sendToPong"] = ctx => ctx.RequestSend("pong", "PING")
};

var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
    id: "ping",
    json: pingJson,
    orchestrator: orchestrator,
    orchestratedActions: actions
);
```

### Distributed (Distributed Orchestrated) - NEW! ✅

```csharp
// Create with IMessageBus (same pattern!)
var messageBus = new DistributedMessageBus();

var actions = new Dictionary<string, Action<DistributedOrchestratedContext>>
{
    ["sendToPong"] = ctx => ctx.RequestSend("pong", "PING")
};

var machine = await DistributedPureStateMachineFactory.CreateFromScriptAsync(
    machineId: "ping",
    json: pingJson,
    messageBus: messageBus,
    orchestratedActions: actions
);
```

**Notice**: Same pattern, same `ctx.RequestSend()` style!

## Complete Example: Distributed PingPong

### Old Pattern (Don't use) ❌

```csharp
// OLD: Direct sends in actions - can deadlock!
var pingActions = new ActionMap
{
    ["sendPong"] = new List<NamedAction>
    {
        new NamedAction("sendPong", async (sm) =>
        {
            // DANGEROUS: Direct send!
            await sm.SendAsync("PONG");
        })
    }
};

var pingMachine = StateMachineFactory.CreateFromScript(pingJson, false, false, pingActions);
```

### New Orchestrated Pattern (Use this) ✅

```csharp
using XStateNet.Distributed;
using XStateNet.Orchestration;

public async Task DistributedPingPongExample()
{
    // Step 1: Create message bus (choose transport)
    var messageBus = new DistributedMessageBus(workerCount: 4);
    await messageBus.ConnectAsync();

    // Step 2: Define Ping machine with orchestrated actions
    var pingJson = @"{
        id: 'ping',
        initial: 'idle',
        states: {
            idle: {
                on: { START: { target: 'active', actions: 'onStart' } }
            },
            active: {
                on: {
                    PONG: { target: 'active', actions: 'onPong' },
                    COMPLETE: 'done'
                }
            },
            done: { type: 'final' }
        }
    }";

    var pingCount = 0;
    var maxPings = 5;

    var pingActions = new Dictionary<string, Action<DistributedOrchestratedContext>>
    {
        ["onStart"] = ctx =>
        {
            pingCount++;
            Console.WriteLine($"[Ping] Sending PING #{pingCount}");
            ctx.RequestSend("pong", "PING", pingCount);
        },
        ["onPong"] = ctx =>
        {
            Console.WriteLine($"[Ping] Received PONG");
            if (pingCount < maxPings)
            {
                pingCount++;
                Console.WriteLine($"[Ping] Sending PING #{pingCount}");
                ctx.RequestSend("pong", "PING", pingCount);
            }
            else
            {
                ctx.RequestSelfSend("COMPLETE");
            }
        }
    };

    // Step 3: Create Ping machine
    var pingMachine = await DistributedPureStateMachineFactory.CreateFromScriptAsync(
        machineId: "ping",
        json: pingJson,
        messageBus: messageBus,
        orchestratedActions: pingActions
    );

    // Step 4: Define Pong machine with orchestrated actions
    var pongJson = @"{
        id: 'pong',
        initial: 'waiting',
        states: {
            waiting: {
                on: { PING: { target: 'waiting', actions: 'onPing' } }
            }
        }
    }";

    var pongActions = new Dictionary<string, Action<DistributedOrchestratedContext>>
    {
        ["onPing"] = ctx =>
        {
            Console.WriteLine($"[Pong] Received PING, sending PONG back");
            ctx.RequestSend("ping", "PONG");
        }
    };

    // Step 5: Create Pong machine
    var pongMachine = await DistributedPureStateMachineFactory.CreateFromScriptAsync(
        machineId: "pong",
        json: pongJson,
        messageBus: messageBus,
        orchestratedActions: pongActions
    );

    // Step 6: Start the ping-pong sequence
    await messageBus.SendEventAsync("external", "ping", "START");

    // Step 7: Wait for completion
    await pingMachine.WaitForStateWithActionsAsync("done", timeoutMs: 10000);

    Console.WriteLine($"Ping-Pong complete! Total pings: {pingCount}");

    // Cleanup
    pingMachine.Dispose();
    pongMachine.Dispose();
    messageBus.Dispose();
}
```

## Context API Comparison

### InProcessOrchestratedContext (InProcess)

```csharp
public class InProcessOrchestratedContext
{
    void RequestSend(string targetMachineId, string eventName, object? payload = null)
    void RequestSelfSend(string eventName, object? payload = null)
    IPureStateMachine Machine { get; }
    string MachineId { get; }
}
```

### DistributedOrchestratedContext (Distributed)

```csharp
public class DistributedOrchestratedContext
{
    void RequestSend(string targetMachineId, string eventName, object? payload = null)
    void RequestSelfSend(string eventName, object? payload = null)
    void RequestBroadcast(string eventName, object? payload = null)  // NEW!
    IPureStateMachine Machine { get; }
    string MachineId { get; }
    string CurrentState { get; }
}
```

**Key Addition**: `RequestBroadcast()` for distributed pub/sub scenarios.

## Unified Factory API

The `UnifiedStateMachineFactory` provides a consistent API across all transports:

```csharp
// For InProcess (automatic)
var factory = new UnifiedStateMachineFactory(TransportType.InProcess);
var machine = await factory.CreateAsync("machine1", json, localActions);

// For Distributed (explicit)
var factory = new UnifiedStateMachineFactory(TransportType.InterNode);
var machine = await factory.CreateDistributedAsync("machine1", json, distributedActions);
```

## Migration Guide

### Step 1: Identify Your Current Pattern

**If you have this (OLD):**
```csharp
var actions = new ActionMap
{
    ["sendEvent"] = new List<NamedAction>
    {
        new NamedAction("sendEvent", async (sm) =>
        {
            await sm.SendAsync("EVENT");  // ❌ Direct send
        })
    }
};

var machine = StateMachineFactory.CreateFromScript(json, false, false, actions);
```

### Step 2: Convert to Orchestrated Pattern (NEW)

**For InProcess deployment:**
```csharp
var actions = new Dictionary<string, Action<InProcessOrchestratedContext>>
{
    ["sendEvent"] = ctx => ctx.RequestSend("target", "EVENT")  // ✅ Orchestrated
};

var orchestrator = new EventBusOrchestrator();
var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
    id: "machine1",
    json: json,
    orchestrator: orchestrator,
    orchestratedActions: actions
);
```

**For Distributed deployment:**
```csharp
var actions = new Dictionary<string, Action<DistributedOrchestratedContext>>
{
    ["sendEvent"] = ctx => ctx.RequestSend("target", "EVENT")  // ✅ Orchestrated
};

var messageBus = new DistributedMessageBus();
var machine = await DistributedPureStateMachineFactory.CreateFromScriptAsync(
    machineId: "machine1",
    json: json,
    messageBus: messageBus,
    orchestratedActions: actions
);
```

### Step 3: Update Tests

**Before (OLD):**
```csharp
[Fact]
public async Task TestPingPong()
{
    var ping = StateMachineFactory.CreateFromScript(pingJson, false, false, pingActions);
    var pong = StateMachineFactory.CreateFromScript(pongJson, false, false, pongActions);

    await ping.SendAsync("START");  // Direct send
}
```

**After (NEW):**
```csharp
[Fact]
public async Task TestPingPong()
{
    var messageBus = new DistributedMessageBus();

    var ping = await DistributedPureStateMachineFactory.CreateFromScriptAsync(
        "ping", pingJson, messageBus, pingActions);
    var pong = await DistributedPureStateMachineFactory.CreateFromScriptAsync(
        "pong", pongJson, messageBus, pongActions);

    await messageBus.SendEventAsync("test", "ping", "START");  // Via message bus
}
```

## Benefits of Unified Pattern

### 1. **Consistency**
- Same `ctx.RequestSend()` pattern everywhere
- Easy to understand and maintain
- No cognitive overhead switching between patterns

### 2. **Safety**
- No direct sends = no deadlocks
- All communication goes through message bus
- Guaranteed ordering and delivery

### 3. **Flexibility**
- Start InProcess in development
- Move to Distributed in production
- Minimal code changes

### 4. **Testability**
- Easy to test with InProcess transport (fast)
- Integration tests with Distributed transport (realistic)
- No mocking needed - real message bus

## Common Patterns

### Pattern 1: Request-Reply

```csharp
var serverActions = new Dictionary<string, Action<DistributedOrchestratedContext>>
{
    ["handleRequest"] = ctx =>
    {
        var requestData = ctx.Machine.ContextMap["requestData"];
        var response = ProcessRequest(requestData);
        ctx.RequestSend("client", "RESPONSE", response);
    }
};

var clientActions = new Dictionary<string, Action<DistributedOrchestratedContext>>
{
    ["sendRequest"] = ctx =>
    {
        ctx.RequestSend("server", "REQUEST", myData);
    },
    ["handleResponse"] = ctx =>
    {
        var response = ctx.Machine.ContextMap["responseData"];
        ProcessResponse(response);
    }
};
```

### Pattern 2: Fan-Out/Fan-In

```csharp
var coordinatorActions = new Dictionary<string, Action<DistributedOrchestratedContext>>
{
    ["fanOut"] = ctx =>
    {
        // Send to multiple workers
        ctx.RequestSend("worker1", "PROCESS", data1);
        ctx.RequestSend("worker2", "PROCESS", data2);
        ctx.RequestSend("worker3", "PROCESS", data3);
    },
    ["collectResults"] = ctx =>
    {
        var results = ctx.Machine.ContextMap["results"];
        if (AllResultsReceived(results))
        {
            ctx.RequestSelfSend("COMPLETE");
        }
    }
};
```

### Pattern 3: Pub/Sub Broadcast

```csharp
var publisherActions = new Dictionary<string, Action<DistributedOrchestratedContext>>
{
    ["publishUpdate"] = ctx =>
    {
        // Broadcast to all subscribers
        ctx.RequestBroadcast("DATA_UPDATED", newData);
    }
};

var subscriberActions = new Dictionary<string, Action<DistributedOrchestratedContext>>
{
    ["onDataUpdated"] = ctx =>
    {
        var data = ctx.Machine.ContextMap["data"];
        UpdateLocalState(data);
    }
};
```

## Performance Considerations

| Pattern | Latency | Throughput | Use Case |
|---------|---------|------------|----------|
| InProcess Orchestrated | ~1-10μs | Very High | Local machines, unit tests |
| Distributed Orchestrated | ~1-50ms | Medium-High | Cross-process, distributed |

## Conclusion

The unified orchestrated pattern provides:
- ✅ **Consistency**: Same pattern everywhere
- ✅ **Safety**: No deadlocks
- ✅ **Flexibility**: Deploy anywhere
- ✅ **Testability**: Easy to test at all levels
- ✅ **Location Transparency**: Write once, deploy anywhere

**Recommendation**: Use the orchestrated pattern for ALL state machines, whether local or distributed!
