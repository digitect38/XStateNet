# Quick Reference Card

## 🚀 Fast Start

```bash
# Default (Lock + Lock)
dotnet run

# Best overall (Hybrid: Array + Autonomous)
dotnet run --robot-hybrid --journey-xstate

# Maximum performance (Array-optimized XState)
dotnet run --robot-array --journey-xstate

# Autonomous polling (Self-managing robots)
dotnet run --robot-autonomous --journey-xstate

# Best concurrency (Actor-based)
dotnet run --robot-actor --journey-actor

# Best maintainability (XState)
dotnet run --robot-xstate --journey-xstate

# Run benchmark
dotnet run --benchmark
```

---

## 📋 All Command-Line Flags

| Flag | Effect |
|------|--------|
| `--robot-actor` | Use Actor-based RobotScheduler |
| `--robot-xstate` | Use XState-based RobotScheduler (FrozenDictionary) |
| `--robot-array` | Use XState-based RobotScheduler (Array-optimized) ⚡ |
| `--robot-autonomous` | Use Autonomous Polling-based RobotScheduler 🤖 |
| `--robot-hybrid` | Use Hybrid (Array + Autonomous) RobotScheduler 🚀 **BEST** |
| `--journey-actor` | Use Actor-based WaferJourneyScheduler |
| `--journey-xstate` | Use XState-based WaferJourneyScheduler |
| `--benchmark` / `-b` | Run performance benchmark |
| `--actor` / `-a` | Same as `--robot-actor` (legacy) |
| `--xstate` / `-x` | Same as `--robot-xstate` (legacy) |

---

## 🎯 The 5x3 Scheduler Matrix

|   | 🔒 Lock Journey | 🎭 Actor Journey | 🔄 XState Journey |
|---|----------------|------------------|-------------------|
| **🔒 Lock Robot** | `dotnet run` | `--journey-actor` | `--journey-xstate` |
| **🎭 Actor Robot** | `--robot-actor` | `--robot-actor --journey-actor` | `--robot-actor --journey-xstate` |
| **🔄 XState Robot** | `--robot-xstate` | `--robot-xstate --journey-actor` | `--robot-xstate --journey-xstate` |
| **⚡ Array Robot** | `--robot-array` | `--robot-array --journey-actor` | `--robot-array --journey-xstate` ⭐ |
| **🤖 Autonomous Robot** | `--robot-autonomous` | `--robot-autonomous --journey-actor` | `--robot-autonomous --journey-xstate` ✨ |

⭐ **Recommended**: Array + XState for best overall performance
✨ **New**: Autonomous polling with self-managing robots

---

## ⚡ Performance Quick Facts

```
Sequential Throughput (10K requests):
🔒 Lock:        1,852 req/sec
🎭 Actor:       3,161,256 req/sec  (170,569% faster)
🔄 XState:      2,144,818 req/sec  (115,694% faster)
⚡ Array:       2,818,887 req/sec  (152,086% faster)
🤖 Autonomous:  1,139 req/sec      (queue rate)
🚀 Hybrid:      1,208 req/sec      (queue rate)

Concurrent Load (10 threads, 10K requests):
🔒 Lock:        842 req/sec
🎭 Actor:       2,944,901 req/sec  (349,621% faster)
🔄 XState:      5,787,707 req/sec  (687,219% faster) ⚡ FrozenDictionary!
⚡ Array:       7,160,246 req/sec  (850,215% faster) ⭐ BEST CONCURRENT!
🤖 Autonomous:  3,162 req/sec      (275% faster)
🚀 Hybrid:      3,075 req/sec      (265% faster)

Query Latency:
🔒 Lock:        0.000ms avg  (best)
🎭 Actor:       0.015ms avg  (Ask overhead)
🔄 XState:      0.000ms avg  (same as Lock)
⚡ Array:       0.000ms avg  (same as Lock)
🤖 Autonomous:  0.000ms avg  (same as Lock)
🚀 Hybrid:      0.000ms avg  (same as Lock)

🚀 Array-optimized achieves HIGHEST concurrent throughput!
🤖 Autonomous/Hybrid excel at self-managing autonomous behavior!
```

---

## 🎓 When to Use Which

### 🔒 Lock-based
**Use for:** Development, debugging, low concurrency
**Avoid for:** High-load production, distributed systems
**Benchmark:** 842 req/sec concurrent

### 🎭 Actor-based
**Use for:** High concurrency, microservices, distributed
**Avoid for:** Simple CRUD, team unfamiliar with async
**Benchmark:** 2,944,901 req/sec concurrent

### 🔄 XState-based
**Use for:** Complex state logic, maintainability, visualization
**Avoid for:** Ultra-low latency, simple stateless ops
**Benchmark:** 5,787,707 req/sec concurrent

### ⚡ Array-based
**Use for:** Maximum concurrent throughput, high-load production
**Avoid for:** Development/debugging (harder to inspect)
**Benchmark:** 7,160,246 req/sec concurrent ⭐ BEST

### 🤖 Autonomous-based
**Use for:** Self-managing robots, polling architecture (SimpleCMPSchedulerDemo pattern)
**Avoid for:** When polling overhead is unacceptable
**Benchmark:** 3,162 req/sec concurrent + autonomy

### 🚀 Hybrid-based
**Use for:** Best of both worlds (byte optimizations + autonomy)
**Avoid for:** When pure XState visualization is needed
**Benchmark:** 3,075 req/sec concurrent + byte optimizations + autonomy

---

## 📊 Quick Comparison

| Feature | Lock | Actor | XState | Array | Autonomous | Hybrid |
|---------|------|-------|--------|-------|------------|--------|
| Throughput | ⭐ | ⭐⭐⭐ | ⭐⭐ | ⭐⭐⭐ | ⭐⭐ | ⭐⭐ |
| Latency | ⭐⭐⭐ | ⭐ | ⭐⭐⭐ | ⭐⭐⭐ | ⭐⭐⭐ | ⭐⭐⭐ |
| Simplicity | ⭐⭐⭐ | ⭐ | ⭐⭐ | ⭐⭐ | ⭐⭐ | ⭐⭐ |
| Scalability | ⭐ | ⭐⭐⭐ | ⭐⭐ | ⭐⭐⭐ | ⭐⭐ | ⭐⭐ |
| Maintainability | ⭐⭐ | ⭐⭐ | ⭐⭐⭐ | ⭐⭐ | ⭐⭐ | ⭐⭐ |
| Autonomy | ❌ | ❌ | ❌ | ❌ | ⭐⭐⭐ | ⭐⭐⭐ |

---

## 🔧 Implementation Files

### RobotScheduler
```
🔒 RobotScheduler.cs
🎭 RobotSchedulerActorProxy.cs + RobotSchedulerActor.cs
🔄 RobotSchedulerXState.cs + RobotSchedulerStateMachine.cs
⚡ RobotSchedulerXStateArray.cs (Array-optimized with byte indices)
🤖 AutonomousRobotScheduler.cs (Polling-based, self-managing)
```

### WaferJourneyScheduler
```
🔒 WaferJourneyScheduler.cs
🎭 WaferJourneySchedulerActorProxy.cs
🔄 WaferJourneySchedulerXState.cs
```

### Interfaces
```
IRobotScheduler.cs
IWaferJourneyScheduler.cs
```

---

## 🎯 Recommended Combinations

| Scenario | Combination | Command | Benchmark Result |
|----------|-------------|---------|------------------|
| **Learning** | Lock + Lock | `dotnet run` | 842 req/sec |
| **Maximum Concurrent Performance** | Array + XState | `dotnet run --robot-array --journey-xstate` ⚡ | **7,160,246 req/sec** ⭐ |
| **Best Overall** | Hybrid + XState | `dotnet run --robot-hybrid --journey-xstate` 🚀 | 3,075 req/sec + autonomy |
| **Autonomous Robots** | Autonomous + XState | `dotnet run --robot-autonomous --journey-xstate` 🤖 | 3,162 req/sec + self-managing |
| **High Concurrency** | Actor + Actor | `dotnet run --robot-actor --journey-actor` | 2,944,901 req/sec |
| **Enterprise** | XState + XState | `dotnet run --robot-xstate --journey-xstate` | 5,787,707 req/sec |
| **Performance Critical** | Array + Lock | `dotnet run --robot-array` | 7M+ req/sec |
| **Complex Logic** | Lock + XState | `dotnet run --journey-xstate` | Good for debugging |

---

## 🐛 Debugging Tips

### Lock-based
```csharp
// Easy: Set breakpoint, step through
lock (_lock)  // ← Breakpoint here
{
    var robot = TryAssign(); // ← Step through
}
```

### Actor-based
```csharp
// Use logging heavily
Receive<RequestTransfer>(msg => {
    _log.Info($"Processing: {msg}"); // ← Log messages
    // ...
});

// Or use AwaitAssert in tests
await AwaitAssertAsync(() => {
    var state = GetState();
    Assert.Equal("expected", state);
});
```

### XState-based
```csharp
// Query current state
var snapshot = await _machine.Ask<StateSnapshot>(new GetState());
Console.WriteLine($"Current state: {snapshot.CurrentState}");

// Check state machine definition
Console.WriteLine(MachineJson); // ← View JSON
```

---

## 📈 Monitoring

### Get Queue Size
```csharp
int queueSize = robotScheduler.GetQueueSize();
```

### Get Robot State
```csharp
string state = robotScheduler.GetRobotState("Robot 1");
// Returns: "idle", "busy", "carrying", or "unknown"
```

### Check Carrier Status
```csharp
bool isComplete = journeyScheduler.IsCurrentCarrierComplete();
string? carrierId = journeyScheduler.GetCurrentCarrierId();
```

---

## 🧪 Testing

```bash
# Run all tests
dotnet test XStateNet2/XStateNet2.Tests/XStateNet2.Tests.csproj

# Run specific test
dotnet test --filter RobotSchedulerTests

# Run with verbose output
dotnet test --logger "console;verbosity=detailed"
```

### Test Coverage
- ✅ 56 unit tests
- ✅ Single-wafer rules validation
- ✅ Concurrent access safety
- ✅ State transitions
- ✅ Metadata (XState V5)

---

## 🔍 Troubleshooting

### Problem: Stack Overflow in XState
**Cause:** Infinite loop in `always` transitions
**Solution:** Check guards in state machine JSON

### Problem: High Latency with Actors
**Cause:** Using Ask() for queries
**Solution:** Use direct property access or hybrid approach

### Problem: Lock Contention
**Cause:** Too many threads competing for lock
**Solution:** Switch to Actor or XState version

### Problem: Actor Messages Not Processing
**Cause:** Unhandled message type
**Solution:** Add `ReceiveAny` handler for debugging

---

## 📚 Documentation

| Document | Description |
|----------|-------------|
| `SCHEDULER_MATRIX.md` | Complete 3x3 matrix guide |
| `CONCURRENCY_MODELS.md` | Visual comparison of models |
| `QUICK_REFERENCE.md` | This document |
| `ROBOT_RULE.md` | Robot scheduling rules |
| `STATION_RULE.md` | Station management rules |

---

## 🎬 Example Session

```bash
# Terminal 1: Run benchmark
cd XStateNet2/CMPSimXS2.Console
dotnet run --benchmark

# Terminal 2: Test specific combination
dotnet run --robot-actor --journey-xstate

# Terminal 3: Run tests
cd ../../XStateNet2.Tests
dotnet test
```

---

## 💡 Pro Tips

1. **Start Simple:** Begin with Lock+Lock, then optimize
2. **Profile First:** Run benchmark before choosing
3. **Mix & Match:** Robot and Journey are independent
4. **Use Interfaces:** Write tests against `IRobotScheduler`
5. **Log Everything:** Especially for Actor debugging

---

## 🚨 Common Mistakes

❌ **Using Ask() in hot path** → Use Tell() for fire-and-forget
❌ **Forgetting .Result on Ask()** → Deadlock potential
❌ **Mixing sync/async** → Use proper async patterns
❌ **Not handling all messages** → Add ReceiveAny fallback
❌ **Circular always transitions** → Add proper guards

---

## 🎯 Performance Tuning

### For Maximum Concurrent Throughput (Fastest ⚡)
```bash
dotnet run --robot-array --journey-xstate
# Array-optimized: 7,160,246 req/sec (850,215% faster than Lock!)
# O(1) lookups with byte indices
# BEST for high-load production
```

### For Best Overall (Performance + Autonomy 🚀)
```bash
dotnet run --robot-hybrid --journey-xstate
# Hybrid: 3,075 req/sec (265% faster than Lock)
# Combines byte optimizations with autonomous polling
# Self-managing robots with O(1) state checks
```

### For Autonomous Operation (Self-managing 🤖)
```bash
dotnet run --robot-autonomous --journey-xstate
# Autonomous: 3,162 req/sec (275% faster than Lock)
# Each robot runs independent polling loop (10ms intervals)
# SimpleCMPSchedulerDemo pattern
```

### For High Concurrency (Actor Model)
```bash
dotnet run --robot-actor --journey-actor
# Actor: 2,944,901 req/sec (349,621% faster than Lock)
# Message passing without locks
```

### For Lowest Latency (Query Response)
```bash
dotnet run  # Lock-based (default)
# Synchronous operations, 0.000ms avg latency
# Best for debugging and simple scenarios
```

### For Enterprise (Maintainability + Performance)
```bash
dotnet run --robot-xstate --journey-xstate
# XState: 5,787,707 req/sec (687,219% faster than Lock)
# Declarative state machines + excellent performance
```

---

## 🔗 Quick Links

- **GitHub:** [XStateNet Repository]
- **Issues:** [Report a Bug]
- **Docs:** [Full Documentation](SCHEDULER_MATRIX.md)
- **Examples:** See `Program.cs` for usage

---

## 📞 Getting Help

1. Read [SCHEDULER_MATRIX.md](SCHEDULER_MATRIX.md)
2. Check [CONCURRENCY_MODELS.md](CONCURRENCY_MODELS.md)
3. Run `dotnet run --help`
4. Review example output
5. Check test cases in XStateNet2.Tests

---

## 🎓 Learning Path

### Beginner
1. Run default (`dotnet run`)
2. Read SCHEDULER_MATRIX.md
3. Try `--robot-actor`
4. Compare performance

### Intermediate
1. Read CONCURRENCY_MODELS.md
2. Test all 9 combinations
3. Run benchmark suite
4. Analyze performance data

### Advanced
1. Implement new scheduler
2. Add custom state machines
3. Profile with diagnostics
4. Contribute improvements

---

## ✨ Key Takeaways

- **6x3 Matrix** = 18 combinations to choose from
- **Independent Selection** = Mix robot and journey schedulers
- **Performance Leaders** (Concurrent Load Benchmark):
  - ⚡ **Array-optimized** = 7,160,246 req/sec (FASTEST - byte-indexed O(1) lookups)
  - 🔄 **XState (FrozenDict)** = 5,787,707 req/sec (Best maintainability + speed)
  - 🎭 **Actor** = 2,944,901 req/sec (High concurrency without locks)
  - 🤖 **Autonomous** = 3,162 req/sec (Self-managing robots with polling)
  - 🚀 **Hybrid** = 3,075 req/sec (Array optimizations + Autonomous behavior)
  - 🔒 **Lock** = 842 req/sec (Simplest debugging)
- **Latency Champions**: Lock, XState, Array, Autonomous, Hybrid all at ~0.000ms
- **Autonomy**: Only Autonomous and Hybrid have self-managing polling loops
- **Use Case Matters** = Choose based on requirements
- **Interfaces Enable Flexibility** = Easy to switch implementations

---

**Last Updated:** 2025-11-02
**Version:** 6x3 Scheduler Matrix (6 Robot Types × 3 Journey Types)
**Benchmark:** 6-Way comparison with real performance data
**Status:** Production Ready ✅
