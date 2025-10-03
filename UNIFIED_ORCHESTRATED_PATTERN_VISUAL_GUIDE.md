# 🎯 XStateNet Unified Orchestrated Pattern

## 🌟 Visual Guide to Location-Transparent State Machines

---

## 📋 Table of Contents

- [🎨 Overview](#-overview)
- [🏗️ Architecture](#️-architecture)
- [🚀 Quick Start](#-quick-start)
- [💡 Pattern Comparison](#-pattern-comparison)
- [🔧 API Reference](#-api-reference)
- [📊 Performance](#-performance)
- [✨ Examples](#-examples)

---

## 🎨 Overview

The **Unified Orchestrated Pattern** provides a **consistent, deadlock-free** way to create state machines that work seamlessly across different environments.

### 🎯 Key Benefits

| Benefit | Description | Icon |
|---------|-------------|------|
| **Consistency** | Same `ctx.RequestSend()` pattern everywhere | ✅ |
| **Safety** | No direct sends = No deadlocks | 🔒 |
| **Flexibility** | Deploy anywhere: InProcess, InterProcess, InterNode | 🌐 |
| **Testability** | Easy to test at all levels | 🧪 |
| **Transparency** | Write once, deploy anywhere | 🎭 |

---

## 🏗️ Architecture

### 🎪 Three-Layer Design

```
┌─────────────────────────────────────────────────────────────┐
│               🎨 Application Layer                           │
│         (Your State Machines & Business Logic)              │
└─────────────────────────────────────────────────────────────┘
                            ⬇️
┌─────────────────────────────────────────────────────────────┐
│            🌉 Abstraction Layer (IMessageBus)               │
│          (Location-Transparent Communication)               │
└─────────────────────────────────────────────────────────────┘
                            ⬇️
        ┌──────────────┬──────────────┬──────────────┐
        ⬇️              ⬇️              ⬇️
┌───────────────┐ ┌───────────────┐ ┌───────────────┐
│ 🏠 InProcess  │ │ 🔗 InterProc  │ │ 🌍 InterNode  │
│ (Fast Local)  │ │ (Same Machine)│ │ (Distributed) │
└───────────────┘ └───────────────┘ └───────────────┘
```

### 🎭 Before vs After

#### ❌ Before (Inconsistent & Dangerous)

```csharp
// 🏠 InProcess: Orchestrated ✅
var localActions = new Dictionary<string, Action<InProcessOrchestratedContext>>
{
    ["send"] = ctx => ctx.RequestSend("target", "EVENT")  // ✅ Safe
};

// 🌍 Distributed: Old Style ❌
var distActions = new ActionMap
{
    ["send"] = new NamedAction("send", async (sm) =>
    {
        await sm.SendAsync("EVENT");  // ❌ DANGEROUS - Can deadlock!
    })
};
```

#### ✅ After (Consistent & Safe)

```csharp
// 🏠 InProcess: Orchestrated ✅
var localActions = new Dictionary<string, Action<InProcessOrchestratedContext>>
{
    ["send"] = ctx => ctx.RequestSend("target", "EVENT")  // ✅ Safe
};

// 🌍 Distributed: Orchestrated ✅
var distActions = new Dictionary<string, Action<DistributedOrchestratedContext>>
{
    ["send"] = ctx => ctx.RequestSend("target", "EVENT")  // ✅ Safe & Consistent!
};
```

---

## 🚀 Quick Start

### 🏠 InProcess (Local Development)

Perfect for **unit tests** and **local development**.

```csharp
using XStateNet.Orchestration;

// 1️⃣ Create factory
var factory = new UnifiedStateMachineFactory(TransportType.InProcess);

// 2️⃣ Define actions with orchestrated context
var actions = new Dictionary<string, Action<InProcessOrchestratedContext>>
{
    ["sendHello"] = ctx =>
    {
        Console.WriteLine($"💬 Sending HELLO from {ctx.MachineId}");
        ctx.RequestSend("receiver", "HELLO");
    }
};

// 3️⃣ Create machine
var machine = await factory.CreateAsync(
    machineId: "sender",
    jsonScript: senderJson,
    actions: actions
);

// 4️⃣ Use it!
await machine.SendToAsync("sender", "START");
```

**⚡ Speed:** ~1-10 microseconds per message
**🎯 Use Case:** Development, Unit Tests

---

### 🌍 Distributed (Production)

Perfect for **production** and **distributed systems**.

```csharp
using XStateNet.Distributed;
using XStateNet.Orchestration;

// 1️⃣ Create distributed message bus
var messageBus = new DistributedMessageBusAdapter();
await messageBus.ConnectAsync();

// 2️⃣ Define actions with distributed orchestrated context
var actions = new Dictionary<string, Action<DistributedOrchestratedContext>>
{
    ["sendHello"] = ctx =>
    {
        Console.WriteLine($"💬 Sending HELLO from {ctx.MachineId}");
        ctx.RequestSend("receiver", "HELLO");
    }
};

// 3️⃣ Create distributed machine
var machine = await DistributedPureStateMachineFactory.CreateFromScriptAsync(
    machineId: "sender",
    json: senderJson,
    messageBus: messageBus,
    orchestratedActions: actions
);

// 4️⃣ Use it!
await messageBus.SendEventAsync("system", "sender", "START");
```

**⚡ Speed:** ~1-50 milliseconds per message
**🎯 Use Case:** Production, Microservices, Distributed Systems

---

## 💡 Pattern Comparison

### 🔴 Old Pattern (Dangerous)

```csharp
❌ PROBLEM: Direct sends can cause deadlocks!

var actions = new ActionMap
{
    ["sendToPing"] = new List<NamedAction>
    {
        new NamedAction("sendToPing", async (sm) =>
        {
            await sm.SendAsync("PING");  // 🚨 DEADLOCK RISK!
            // If the target machine is waiting for this machine,
            // this creates a circular wait = DEADLOCK
        })
    }
};

var machine = StateMachineFactory.CreateFromScript(json, false, false, actions);
```

**Problems:**
- 🚨 **Deadlock Risk:** Circular waits between machines
- 🔀 **Race Conditions:** Unpredictable message ordering
- 🐛 **Hard to Debug:** No central monitoring point
- 🎭 **Inconsistent:** Different patterns for local vs distributed

---

### 🟢 New Pattern (Safe)

```csharp
✅ SOLUTION: All sends go through orchestrator/message bus!

var actions = new Dictionary<string, Action<InProcessOrchestratedContext>>
{
    ["sendToPing"] = ctx =>
    {
        ctx.RequestSend("pong", "PING");  // ✅ DEADLOCK-FREE!
        // Request is queued and processed after current action completes
        // No circular wait possible
    }
};

var orchestrator = new EventBusOrchestrator();
var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
    id: "ping",
    json: json,
    orchestrator: orchestrator,
    orchestratedActions: actions
);
```

**Benefits:**
- ✅ **Deadlock-Free:** All communication through orchestrator
- 📋 **Deterministic:** Guaranteed message ordering
- 🔍 **Observable:** Central monitoring point
- 🎯 **Consistent:** Same pattern everywhere

---

## 🔧 API Reference

### 🎨 InProcessOrchestratedContext (InProcess)

```csharp
public class InProcessOrchestratedContext
{
    // 📤 Send to another machine
    void RequestSend(string targetMachineId, string eventName, object? payload = null);

    // 🔄 Send to self (deferred)
    void RequestSelfSend(string eventName, object? payload = null);

    // 📋 Access to machine
    IPureStateMachine Machine { get; }

    // 🏷️ Current machine ID
    string MachineId { get; }
}
```

### 🌍 DistributedOrchestratedContext (Distributed)

```csharp
public class DistributedOrchestratedContext
{
    // 📤 Send to another machine (location-transparent)
    void RequestSend(string targetMachineId, string eventName, object? payload = null);

    // 🔄 Send to self
    void RequestSelfSend(string eventName, object? payload = null);

    // 📡 Broadcast to all machines (pub/sub)
    void RequestBroadcast(string eventName, object? payload = null);

    // 📋 Access to machine
    IPureStateMachine Machine { get; }

    // 🏷️ Current machine ID
    string MachineId { get; }

    // 🎯 Current state
    string CurrentState { get; }
}
```

---

## 📊 Performance

### ⚡ Latency Comparison

```
🏠 InProcess
├─ Latency:    ~1-10 μs    ⚡⚡⚡⚡⚡
├─ Throughput: Very High    📈📈📈📈📈
└─ Use Case:   Development, Unit Tests

🔗 InterProcess
├─ Latency:    ~100 μs      ⚡⚡⚡⚡
├─ Throughput: High          📈📈📈📈
└─ Use Case:   Staging, Process Isolation

🌍 InterNode (TCP)
├─ Latency:    ~1-10 ms     ⚡⚡⚡
├─ Throughput: Medium        📈📈📈
└─ Use Case:   Distributed, Cloud

🌍 InterNode (MQ)
├─ Latency:    ~10-50 ms    ⚡⚡
├─ Throughput: High          📈📈📈📈
└─ Use Case:   Enterprise, Decoupled
```

### 📈 Scalability

| Transport | Max Machines | Max Throughput | Deployment |
|-----------|--------------|----------------|------------|
| 🏠 InProcess | 1000+ | 100K+ msg/sec | Single Process |
| 🔗 InterProcess | 100+ | 50K+ msg/sec | Same Machine |
| 🌍 InterNode | 10000+ | 20K+ msg/sec | Distributed |

---

## ✨ Examples

### 🏓 Example 1: PingPong

#### 🏠 InProcess Version

```csharp
using XStateNet.Orchestration;

public async Task PingPongInProcess()
{
    // 1️⃣ Create orchestrator
    var orchestrator = new EventBusOrchestrator(new OrchestratorConfig
    {
        PoolSize = 4,
        EnableLogging = true
    });

    // 2️⃣ Create Ping machine
    var pingActions = new Dictionary<string, Action<InProcessOrchestratedContext>>
    {
        ["onStart"] = ctx =>
        {
            Console.WriteLine("🏓 Ping: Starting!");
            ctx.RequestSend("pong", "PING");
        },
        ["onPong"] = ctx =>
        {
            Console.WriteLine("🏓 Ping: Got PONG!");
            ctx.RequestSend("pong", "PING");
        }
    };

    var ping = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
        id: "ping",
        json: PingMachineJson,
        orchestrator: orchestrator,
        orchestratedActions: pingActions
    );

    // 3️⃣ Create Pong machine
    var pongActions = new Dictionary<string, Action<InProcessOrchestratedContext>>
    {
        ["onPing"] = ctx =>
        {
            Console.WriteLine("🏐 Pong: Got PING, sending PONG!");
            ctx.RequestSend("ping", "PONG");
        }
    };

    var pong = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
        id: "pong",
        json: PongMachineJson,
        orchestrator: orchestrator,
        orchestratedActions: pongActions
    );

    // 4️⃣ Start ping-pong!
    await orchestrator.SendEventAsync("system", "ping", "START");

    await Task.Delay(1000);
}
```

**Output:**
```
🏓 Ping: Starting!
🏐 Pong: Got PING, sending PONG!
🏓 Ping: Got PONG!
🏐 Pong: Got PING, sending PONG!
...
```

---

#### 🌍 Distributed Version

```csharp
using XStateNet.Distributed;

public async Task PingPongDistributed()
{
    // 1️⃣ Create distributed message bus
    var messageBus = new DistributedMessageBusAdapter();
    await messageBus.ConnectAsync();

    // 2️⃣ Create Ping machine (exact same logic!)
    var pingActions = new Dictionary<string, Action<DistributedOrchestratedContext>>
    {
        ["onStart"] = ctx =>
        {
            Console.WriteLine("🏓 Ping: Starting!");
            ctx.RequestSend("pong", "PING");
        },
        ["onPong"] = ctx =>
        {
            Console.WriteLine("🏓 Ping: Got PONG!");
            ctx.RequestSend("pong", "PING");
        }
    };

    var ping = await DistributedPureStateMachineFactory.CreateFromScriptAsync(
        machineId: "ping",
        json: PingMachineJson,
        messageBus: messageBus,
        orchestratedActions: pingActions
    );

    // 3️⃣ Create Pong machine (exact same logic!)
    var pongActions = new Dictionary<string, Action<DistributedOrchestratedContext>>
    {
        ["onPing"] = ctx =>
        {
            Console.WriteLine("🏐 Pong: Got PING, sending PONG!");
            ctx.RequestSend("ping", "PONG");
        }
    };

    var pong = await DistributedPureStateMachineFactory.CreateFromScriptAsync(
        machineId: "pong",
        json: PongMachineJson,
        messageBus: messageBus,
        orchestratedActions: pongActions
    );

    // 4️⃣ Start ping-pong!
    await messageBus.SendEventAsync("system", "ping", "START");

    await Task.Delay(1000);
}
```

**✨ Notice:** Almost identical code, just different factory!

---

### 🎯 Example 2: Request-Reply Pattern

```csharp
// 🖥️ Server machine
var serverActions = new Dictionary<string, Action<DistributedOrchestratedContext>>
{
    ["processRequest"] = ctx =>
    {
        Console.WriteLine($"🖥️  Server: Processing request from {ctx.CurrentState}");

        // Do some processing...
        var result = ProcessData();

        // Send response back to client
        ctx.RequestSend("client", "RESPONSE", result);
        Console.WriteLine("✅ Server: Response sent!");
    }
};

// 📱 Client machine
var clientActions = new Dictionary<string, Action<DistributedOrchestratedContext>>
{
    ["sendRequest"] = ctx =>
    {
        Console.WriteLine("📱 Client: Sending request...");
        ctx.RequestSend("server", "REQUEST", requestData);
    },
    ["handleResponse"] = ctx =>
    {
        Console.WriteLine("📱 Client: Got response!");
        var response = ctx.Machine.CurrentState;
        ProcessResponse(response);
    }
};
```

**Output:**
```
📱 Client: Sending request...
🖥️  Server: Processing request from waiting
✅ Server: Response sent!
📱 Client: Got response!
```

---

### 🌐 Example 3: Fan-Out/Fan-In Pattern

```csharp
// 🎯 Coordinator machine
var coordinatorActions = new Dictionary<string, Action<DistributedOrchestratedContext>>
{
    ["distributeTasks"] = ctx =>
    {
        Console.WriteLine("🎯 Coordinator: Distributing tasks to workers...");

        // Fan-out: Send to multiple workers
        ctx.RequestSend("worker1", "PROCESS", data1);
        ctx.RequestSend("worker2", "PROCESS", data2);
        ctx.RequestSend("worker3", "PROCESS", data3);

        Console.WriteLine("📤 Coordinator: Tasks distributed!");
    },
    ["collectResults"] = ctx =>
    {
        Console.WriteLine("📥 Coordinator: Collecting results...");

        // Check if all results received
        if (AllResultsReceived())
        {
            Console.WriteLine("✅ Coordinator: All results collected!");
            ctx.RequestSelfSend("COMPLETE");
        }
    }
};

// 👷 Worker machines (all identical)
var workerActions = new Dictionary<string, Action<DistributedOrchestratedContext>>
{
    ["processTask"] = ctx =>
    {
        Console.WriteLine($"👷 Worker {ctx.MachineId}: Processing task...");

        var result = DoWork();

        // Send result back to coordinator
        ctx.RequestSend("coordinator", "RESULT", result);
        Console.WriteLine($"✅ Worker {ctx.MachineId}: Task complete!");
    }
};
```

**Output:**
```
🎯 Coordinator: Distributing tasks to workers...
📤 Coordinator: Tasks distributed!
👷 Worker worker1: Processing task...
👷 Worker worker2: Processing task...
👷 Worker worker3: Processing task...
✅ Worker worker1: Task complete!
✅ Worker worker2: Task complete!
✅ Worker worker3: Task complete!
📥 Coordinator: Collecting results...
✅ Coordinator: All results collected!
```

---

### 📡 Example 4: Pub/Sub Broadcast

```csharp
// 📡 Publisher machine
var publisherActions = new Dictionary<string, Action<DistributedOrchestratedContext>>
{
    ["publishUpdate"] = ctx =>
    {
        Console.WriteLine("📡 Publisher: Broadcasting update to all subscribers...");

        var data = GetLatestData();

        // Broadcast to all subscribers
        ctx.RequestBroadcast("DATA_UPDATED", data);

        Console.WriteLine("✅ Publisher: Update broadcasted!");
    }
};

// 📱 Subscriber machines (multiple)
var subscriberActions = new Dictionary<string, Action<DistributedOrchestratedContext>>
{
    ["onDataUpdated"] = ctx =>
    {
        Console.WriteLine($"📱 Subscriber {ctx.MachineId}: Received update!");

        // Update local state
        UpdateLocalData();

        Console.WriteLine($"✅ Subscriber {ctx.MachineId}: Data updated!");
    }
};
```

**Output:**
```
📡 Publisher: Broadcasting update to all subscribers...
✅ Publisher: Update broadcasted!
📱 Subscriber sub1: Received update!
📱 Subscriber sub2: Received update!
📱 Subscriber sub3: Received update!
✅ Subscriber sub1: Data updated!
✅ Subscriber sub2: Data updated!
✅ Subscriber sub3: Data updated!
```

---

## 🎓 Migration Guide

### 🔄 Step-by-Step Migration

#### 1️⃣ Identify Current Pattern

**❌ If you have this (OLD):**
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

#### 2️⃣ Convert to Orchestrated (NEW)

**✅ For InProcess:**
```csharp
var actions = new Dictionary<string, Action<InProcessOrchestratedContext>>
{
    ["sendEvent"] = ctx => ctx.RequestSend("target", "EVENT")  // ✅ Safe
};

var orchestrator = new EventBusOrchestrator();
var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
    id: "machine1",
    json: json,
    orchestrator: orchestrator,
    orchestratedActions: actions
);
```

**✅ For Distributed:**
```csharp
var actions = new Dictionary<string, Action<DistributedOrchestratedContext>>
{
    ["sendEvent"] = ctx => ctx.RequestSend("target", "EVENT")  // ✅ Safe
};

var messageBus = new DistributedMessageBusAdapter();
var machine = await DistributedPureStateMachineFactory.CreateFromScriptAsync(
    machineId: "machine1",
    json: json,
    messageBus: messageBus,
    orchestratedActions: actions
);
```

---

## 🎉 Summary

### ✅ What You Get

| Feature | Before | After |
|---------|--------|-------|
| **Pattern Consistency** | ❌ Different everywhere | ✅ Same everywhere |
| **Deadlock Safety** | ❌ Can deadlock | ✅ Deadlock-free |
| **Location Transparency** | ❌ Manual switching | ✅ Automatic |
| **Testability** | ❌ Hard to test | ✅ Easy to test |
| **Observability** | ❌ No central point | ✅ Centralized |
| **Code Reuse** | ❌ Rewrite for each env | ✅ Write once |

### 🎯 Next Steps

1. 📖 **Read** the full documentation
2. 🔧 **Try** the examples above
3. 🔄 **Migrate** your existing code
4. 🚀 **Deploy** with confidence!

---

## 📚 Additional Resources

- 📘 [Complete API Reference](./API_REFERENCE.md)
- 🎯 [Location Transparency Guide](./LOCATION_TRANSPARENCY_GUIDE.md)
- 🔧 [Distributed Pattern Guide](./DISTRIBUTED_ORCHESTRATED_PATTERN.md)
- 🏗️ [Architecture Deep Dive](./ARCHITECTURE.md)
- 💡 [Best Practices](./BEST_PRACTICES.md)

---

<div align="center">

## 🌟 Made with ❤️ using XStateNet

**Write Once, Deploy Anywhere!** 🚀

</div>
