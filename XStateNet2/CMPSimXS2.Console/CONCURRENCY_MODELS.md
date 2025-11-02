# Concurrency Models Comparison

## Visual Comparison

### 🔒 Lock-based Model

```
Thread 1                Thread 2                Thread 3
   |                       |                       |
   | RequestTransfer()     | RequestTransfer()     | RequestTransfer()
   ▼                       ▼                       ▼
┌──────────────────────────────────────────────────────┐
│              LOCK (mutual exclusion)                 │
│  ┌────────────────────────────────────────────┐     │
│  │                                             │     │
│  │  Thread 1: Execute request                 │     │ ← Only ONE thread
│  │  Threads 2,3: BLOCKED, waiting for lock    │     │   executes
│  │                                             │     │
│  └────────────────────────────────────────────┘     │
└──────────────────────────────────────────────────────┘
   |                       |                       |
   ▼                       ▼                       ▼
 Return                 Return                 Return
```

**Characteristics:**
- ⏱️ Threads wait for lock (contention)
- 🔄 Sequential execution within lock
- ✅ Simple to understand
- ⚠️ Lower throughput under load

---

### 🎭 Actor-based Model

```
Thread 1                Thread 2                Thread 3
   |                       |                       |
   | Tell(RequestTransfer) | Tell(RequestTransfer) | Tell(RequestTransfer)
   ▼                       ▼                       ▼
   |                       |                       |
   ├───────────────────────┴───────────────────────┤
   │         All threads return IMMEDIATELY        │
   └───────────────────────┬───────────────────────┘
                           ▼
              ┌──────────────────────────┐
              │    Actor Mailbox         │
              │  ┌────────────────────┐  │
              │  │ Message 1 (Thread1)│  │
              │  ├────────────────────┤  │
              │  │ Message 2 (Thread2)│  │
              │  ├────────────────────┤  │
              │  │ Message 3 (Thread3)│  │
              │  └────────────────────┘  │
              └──────────────────────────┘
                           |
                           | Single-threaded
                           | message processing
                           ▼
              ┌──────────────────────────┐
              │   Process one message    │
              │   at a time (serialized) │
              └──────────────────────────┘
```

**Characteristics:**
- 🚀 No thread blocking (fire-and-forget)
- 📬 Mailbox serializes messages
- 🔀 Massive concurrency support
- ✅ No explicit locks needed

---

### 🔄 XState-based Model

```
Thread 1                Thread 2                Thread 3
   |                       |                       |
   | Tell(REQUEST_TRANSFER)| Tell(REQUEST_TRANSFER)| Tell(REQUEST_TRANSFER)
   ▼                       ▼                       ▼
              ┌──────────────────────────┐
              │    XState Actor          │
              │  ┌────────────────────┐  │
              │  │   State Machine    │  │
              │  │                    │  │
              │  │   ┌──────────┐    │  │
              │  │   │   idle   │◄───┼──┼── Initial state
              │  │   └────┬─────┘    │  │
              │  │        │ event    │  │
              │  │        ▼          │  │
              │  │   ┌──────────┐    │  │
              │  │   │processing│    │  │
              │  │   └────┬─────┘    │  │
              │  │        │ always   │  │
              │  │        │ (guard)  │  │
              │  │        └──────────┘  │
              │  └────────────────────┘  │
              └──────────────────────────┘
                           |
                           | Declarative
                           | state transitions
                           ▼
              ┌──────────────────────────┐
              │   Execute actions        │
              │   based on state         │
              └──────────────────────────┘
```

**Characteristics:**
- 📋 Declarative JSON definition
- 🎯 Clear state transitions
- 🔍 Visual and debuggable
- ⚡ High performance (actor under hood)

---

## Message Flow Comparison

### 🔒 Lock-based: Synchronous Flow

```
Client Thread
    │
    │ RequestTransfer(request)
    ▼
┌────────────────────┐
│   Acquire Lock     │  ← Block if locked
└────────┬───────────┘
         │
         ▼
┌────────────────────┐
│  Try Assign Robot  │
└────────┬───────────┘
         │
         ▼
┌────────────────────┐
│  Queue if needed   │
└────────┬───────────┘
         │
         ▼
┌────────────────────┐
│   Release Lock     │
└────────┬───────────┘
         │
         ▼
    Return to Client

⏱️ Total Time: Execution + Lock Wait
```

---

### 🎭 Actor-based: Asynchronous Flow

```
Client Thread                    Actor Thread
    │                                │
    │ Tell(RequestTransfer)          │
    ├───────────────────────────────►│
    │                                │ Mailbox queues message
    ▼                                │
Return IMMEDIATELY                   │
(~1-2 microseconds)                  │
                                     ▼
                         ┌────────────────────┐
                         │ Process Message    │
                         └─────────┬──────────┘
                                   │
                                   ▼
                         ┌────────────────────┐
                         │ Try Assign Robot   │
                         └─────────┬──────────┘
                                   │
                                   ▼
                         ┌────────────────────┐
                         │ Queue if needed    │
                         └────────────────────┘

⏱️ Client Time: ~1-2 microseconds (Tell)
⏱️ Processing: Happens asynchronously
```

---

### 🔄 XState-based: State-driven Flow

```
Client Thread                    XState Actor
    │                                │
    │ Tell(REQUEST_TRANSFER)         │
    ├───────────────────────────────►│
    │                                │
    ▼                                │
Return IMMEDIATELY                   │
                                     ▼
                         ┌────────────────────┐
                         │  Current State?    │
                         └─────────┬──────────┘
                                   │
                         ┌─────────▼──────────┐
                         │ idle state         │
                         │   on: REQUEST_→    │
                         │     target: proc   │
                         │     action: queue  │
                         └─────────┬──────────┘
                                   │
                                   ▼
                         ┌────────────────────┐
                         │ Transition to      │
                         │ processing state   │
                         └─────────┬──────────┘
                                   │
                                   ▼
                         ┌────────────────────┐
                         │ Execute entry      │
                         │ actions            │
                         └─────────┬──────────┘
                                   │
                                   ▼
                         ┌────────────────────┐
                         │ Check always       │
                         │ transitions        │
                         └────────────────────┘

⏱️ Client Time: ~1-2 microseconds (Tell)
📊 State Machine: Guides execution flow
```

---

## Concurrency Patterns

### Lock-based: Pessimistic Locking

```csharp
public void RequestTransfer(TransferRequest request)
{
    lock (_lock)  // ← Single point of synchronization
    {
        // Only ONE thread can be here at a time
        var robot = TryAssignTransfer(request);
        if (robot == null)
            _pendingRequests.Enqueue(request);
    }
    // All other threads wait here
}
```

**Pattern:** Mutual Exclusion
**Pros:** Simple, safe
**Cons:** Contention under load

---

### Actor-based: Message Passing

```csharp
public class RobotSchedulerActor : ReceiveActor
{
    // NO LOCKS - Single-threaded by design
    public RobotSchedulerActor()
    {
        Receive<RequestTransfer>(msg =>
        {
            // Guaranteed: Only one message processed at a time
            // Mailbox serializes all incoming messages
            var robot = TryAssignTransfer(msg.Request);
            if (robot == null)
                _pendingRequests.Enqueue(msg.Request);
        });
    }
}

// Client code
public void RequestTransfer(TransferRequest request)
{
    _schedulerActor.Tell(new RequestTransfer(request));
    // Returns immediately - no waiting!
}
```

**Pattern:** Actor Model (Hewitt, 1973)
**Pros:** No blocking, high throughput
**Cons:** Async complexity

---

### XState-based: State Machine

```json
{
  "id": "robotScheduler",
  "initial": "idle",
  "states": {
    "idle": {
      "on": {
        "REQUEST_TRANSFER": {
          "target": "processing",
          "actions": ["queueOrAssignTransfer"]
        }
      }
    },
    "processing": {
      "entry": ["processTransfers"],
      "always": {
        "target": "idle",
        "cond": "hasNoPendingWork"
      }
    }
  }
}
```

```csharp
// Actions registered programmatically
_machine = factory.FromJson(MachineJson)
    .WithAction("queueOrAssignTransfer", (ctx, data) => { /* ... */ })
    .WithGuard("hasNoPendingWork", (ctx, _) => _context.PendingRequests.Count == 0)
    .BuildAndStart();
```

**Pattern:** Finite State Machine (Mealy/Moore)
**Pros:** Declarative, visual, maintainable
**Cons:** Learning curve

---

## Performance Characteristics Graph

```
Throughput (requests/sec)
    │
5M  │         🎭 Actor (5.3M)
    │         ██████████████████████████████████████████
    │
4M  │
    │
3M  │
    │
2M  │
    │
1.3M│    🔄 XState (1.3M)
    │    ██████████████
    │
1M  │
    │
    │
    │ 🔒 Lock (988)
    │ █
    └─────────────────────────────────────────────────► Concurrency
     Low                                            High
```

---

## Memory Usage Comparison

```
┌──────────────────────────────────────────────────────┐
│                Memory Footprint                       │
├──────────────────────────────────────────────────────┤
│                                                       │
│  🔒 Lock-based                                       │
│  ████ ~4KB (Dictionary + Lock)                       │
│                                                       │
│  🎭 Actor-based                                      │
│  ████████ ~12KB (Actor + Mailbox + Dictionary)      │
│                                                       │
│  🔄 XState-based                                     │
│  ████████████ ~18KB (Actor + State Machine + Dict)  │
│                                                       │
└──────────────────────────────────────────────────────┘
```

**Note:** Memory overhead is negligible compared to performance gains.

---

## Thread Safety Mechanisms

### 🔒 Lock-based
```
Thread Safety = Explicit Mutual Exclusion
                    │
    ┌───────────────┼───────────────┐
    │               │               │
Acquire Lock    Critical       Release Lock
                Section
```

### 🎭 Actor-based
```
Thread Safety = Mailbox Serialization
                    │
    ┌───────────────┼───────────────┐
    │               │               │
Send Message    Mailbox         Process One
to Mailbox      Queues          at a Time
```

### 🔄 XState-based
```
Thread Safety = State Machine + Actor Mailbox
                    │
        ┌───────────┼───────────┐
        │           │           │
    JSON State   Mailbox    Single-threaded
    Definition   Queue      State Processing
```

---

## Code Complexity Comparison

### Lines of Code (approximate)

```
┌────────────────────────────────────────────┐
│  Implementation   │  LOC  │  Complexity    │
├────────────────────────────────────────────┤
│  🔒 Lock          │  300  │  ⭐ Simple     │
│  🎭 Actor         │  450  │  ⭐⭐ Medium   │
│  🔄 XState        │  400  │  ⭐⭐⭐ Higher │
└────────────────────────────────────────────┘
```

### Cyclomatic Complexity

```
🔒 Lock:   10-15 per method (branching logic)
🎭 Actor:  5-8 per handler (message handlers are simple)
🔄 XState: 3-5 per action (state machine handles flow)
```

---

## Error Handling Patterns

### 🔒 Lock-based: Try-Catch

```csharp
lock (_lock)
{
    try
    {
        request.Validate();
        var robot = TryAssignTransfer(request);
    }
    catch (Exception ex)
    {
        Logger.Log($"ERROR: {ex.Message}");
        return;
    }
}
```

### 🎭 Actor-based: Supervisor Strategy

```csharp
// Actor supervision handles failures
protected override SupervisorStrategy SupervisorStrategy()
{
    return new OneForOneStrategy(ex =>
    {
        if (ex is InvalidRequestException)
            return Directive.Resume;  // Continue

        return Directive.Restart;  // Restart actor
    });
}
```

### 🔄 XState-based: Error States

```json
{
  "states": {
    "processing": {
      "on": {
        "error": {
          "target": "error",
          "actions": ["logError"]
        }
      }
    },
    "error": {
      "entry": ["notifyError"],
      "on": {
        "RETRY": "processing"
      }
    }
  }
}
```

---

## Testing Complexity

| Aspect | 🔒 Lock | 🎭 Actor | 🔄 XState |
|--------|---------|----------|-----------|
| **Unit Tests** | ⭐ Easy | ⭐⭐ Medium | ⭐⭐ Medium |
| **Mocking** | ⭐ Easy | ⭐⭐⭐ Hard | ⭐⭐ Medium |
| **Race Conditions** | ⭐⭐⭐ Hard | ⭐ Easy | ⭐ Easy |
| **Debugging** | ⭐ Easy | ⭐⭐⭐ Hard | ⭐⭐ Medium |
| **Integration** | ⭐ Easy | ⭐⭐ Medium | ⭐⭐ Medium |

---

## When to Use Which Model

### 🔒 Lock-based: Use When...

✅ **Team is not familiar with actors**
✅ **Low concurrency (< 100 req/sec)**
✅ **Simple state management**
✅ **Debugging is priority**
✅ **Embedded systems (low memory)**

❌ **Don't use for:**
- High-throughput systems
- Microservices
- Distributed systems

---

### 🎭 Actor-based: Use When...

✅ **High concurrency (> 10,000 req/sec)**
✅ **Distributed systems**
✅ **Microservices architecture**
✅ **Event-driven design**
✅ **Team knows Akka.NET**

❌ **Don't use for:**
- Synchronous request/response
- Simple CRUD apps
- Teams unfamiliar with async

---

### 🔄 XState-based: Use When...

✅ **Complex state logic**
✅ **Need visual diagrams**
✅ **Long-term maintainability**
✅ **State machine workflows**
✅ **Good balance needed**

❌ **Don't use for:**
- Simple stateless operations
- Real-time ultra-low latency
- Teams unfamiliar with state machines

---

## Migration Path

### From Lock to Actor

```
Step 1: Extract interface (IRobotScheduler)
Step 2: Create actor implementation
Step 3: Run both in parallel (shadow mode)
Step 4: Compare behavior and performance
Step 5: Switch to actor in production
Step 6: Remove lock-based code (optional)
```

### From Lock to XState

```
Step 1: Identify states in lock-based code
Step 2: Draw state diagram
Step 3: Convert to JSON state machine
Step 4: Register actions and guards
Step 5: Test state transitions
Step 6: Deploy XState version
```

### From Actor to XState

```
Step 1: Map messages to events
Step 2: Define states from actor behavior
Step 3: Convert receive handlers to actions
Step 4: Add state machine orchestration
Step 5: Test equivalent behavior
```

---

## Real-world Analogies

### 🔒 Lock-based = Single Cashier
```
Customers (threads) line up
Only ONE customer served at a time
Others wait in line (blocked)
Simple, but slow when busy
```

### 🎭 Actor-based = Restaurant
```
Customers give order (message) and sit down
Kitchen (actor) processes orders one by one
Customers don't wait at counter (non-blocking)
High throughput, many customers served
```

### 🔄 XState-based = Assembly Line
```
Clear steps: order → prepare → cook → serve
Each step has specific rules (state machine)
Visual workflow everyone understands
Efficient and organized
```

---

## Summary Table

| Feature | 🔒 Lock | 🎭 Actor | 🔄 XState |
|---------|---------|----------|-----------|
| **Throughput** | Low | Very High | High |
| **Latency** | Low | Medium | Low |
| **Simplicity** | ⭐⭐⭐ | ⭐ | ⭐⭐ |
| **Scalability** | ⭐ | ⭐⭐⭐ | ⭐⭐ |
| **Maintainability** | ⭐⭐ | ⭐⭐ | ⭐⭐⭐ |
| **Learning Curve** | ⭐ Easy | ⭐⭐⭐ Hard | ⭐⭐ Medium |
| **Memory Usage** | Low | Medium | Medium |
| **Debugging** | Easy | Hard | Medium |
| **Visual Tools** | No | No | Yes |
| **Distributed** | No | Yes | Possible |

---

## Conclusion

Each concurrency model has its place:

- **🔒 Lock** = Simple, synchronous, low concurrency
- **🎭 Actor** = High performance, async, distributed
- **🔄 XState** = Declarative, maintainable, balanced

**The best choice depends on your specific requirements!**

---

**See Also:**
- [SCHEDULER_MATRIX.md](SCHEDULER_MATRIX.md) - Complete 3x3 matrix documentation
- [ROBOT_RULE.md](ROBOT_RULE.md) - Robot scheduling rules
- [STATION_RULE.md](STATION_RULE.md) - Station management rules
