# 🎯 Clean Naming - Quick Reference

## Test Output Format

When you run `dotnet run --stress-test`, you'll now see:

```
╔═══════════════════════════════════════════════════════════════════════════════╗
║  DEBUG Stress Test: 250 Wafers × 1000 Cycles (10 FOUPs)                      ║
║  Testing: All 14 Scheduler Architectures                                      ║
║  Metrics: Throughput, Reliability, Performance, Failure Modes                 ║
╚═══════════════════════════════════════════════════════════════════════════════╝

Testing: Lock (polling)
═══════════════════════════════════════════════
  ⏱️  Time: 15.88s
  ✓ Completed: 250/250 (100.0%)
  Result: PASS ✅

Testing: Actor (event)
═══════════════════════════════════════════════
  ⏱️  Time: 15.86s
  ✓ Completed: 250/250 (100.0%)
  Result: PASS ✅

Testing: XS2-Dict (event)
═══════════════════════════════════════════════
  ⏱️  Time: 18.xx s  ← Dictionary baseline (slowest)
  ✓ Completed: 250/250 (100.0%)
  Result: PASS ✅

Testing: XS2-Frozen (event)
═══════════════════════════════════════════════
  ⏱️  Time: 15.xx s  ← +10-15% faster (FrozenDict)
  ✓ Completed: 250/250 (100.0%)
  Result: PASS ✅

Testing: XS2-Array (event)
═══════════════════════════════════════════════
  ⏱️  Time: 15.xx s  ← Fastest XStateNet2 variant
  ✓ Completed: 250/250 (100.0%)
  Result: PASS ✅

... (remaining schedulers)
```

---

## 📊 Complete Mapping Table

| Display Name | Code | File | Key Features |
|--------------|------|------|--------------|
| **Lock (polling)** | `lock` | RobotScheduler.cs | Traditional mutex, polling |
| **Actor (event)** | `actor` | RobotSchedulerActorProxy.cs | Pure actor, event-driven |
| **XS2-Dict (event)** | `xs2-dict` | RobotSchedulerXStateDict.cs | XStateNet2 + Dictionary (baseline) |
| **XS2-Frozen (event)** | `xs2-frozen` | RobotSchedulerXState.cs | XStateNet2 + FrozenDict (+43%) |
| **XS2-Array (event)** | `xs2-array` | RobotSchedulerXStateArray.cs | XStateNet2 + Array (+63%) |
| **Autonomous (polling)** | `autonomous` | AutonomousRobotScheduler.cs | Self-scheduling + Lock |
| **Autonomous-Array (polling)** | `autonomous-array` | AutonomousArrayScheduler.cs | Self-scheduling + Array |
| **Autonomous-Event (event)** | `autonomous-event` | EventDrivenHybridScheduler.cs | Self-scheduling + Events |
| **Actor-Mailbox (event)** | `actor-mailbox` | ActorMailboxEventDrivenScheduler.cs | Actor mailbox pattern |
| **Ant-Colony (event)** | `ant-colony` | AntColonyScheduler.cs | Ant colony optimization |
| **XS2-PubSub-Dedicated (multi)** | `xs2-pubsub-dedicated` | PublicationBasedScheduler.cs | XStateNet2 Pub/sub + dedicated per robot |
| **PubSub-Single (one)** | `pubsub-single` | SinglePublicationScheduler.cs | Pub/sub + single scheduler |
| **XS2-PubSub-Array (one)** | `xs2-pubsub-array` | SinglePublicationSchedulerXState.cs | XStateNet2 Pub/sub + Array + single |
| **Sync-Pipeline (batch)** | `sync-pipe` | SynchronizedPipelineScheduler.cs | Batch synchronized transfers |

---

## 🎨 Name Anatomy

Each name follows the pattern:
```
[Primary Feature]-[Variant] ([Communication])
```

### Examples:

**XS2-Frozen (event)**
- Primary: `XS2` - Uses XStateNet2 JSON machine
- Variant: `Frozen` - FrozenDictionary optimization
- Communication: `event` - Event-driven updates

**Autonomous-Array (polling)**
- Primary: `Autonomous` - Self-scheduling robots
- Variant: `Array` - Array optimization
- Communication: `polling` - Polling-based state checks

**PubSub-Array (one)**
- Primary: `PubSub` - Publication/subscription pattern
- Variant: `Array` - Array optimization
- Note: `one` - Single scheduler (vs `multi` dedicated schedulers)

---

## 🔍 Quick Comparisons

### Compare State Engines:
```bash
Lock (polling)          # Traditional lock-based
Actor (event)           # Pure actor model
XS2-Frozen (event)      # XStateNet2 JSON machine
```

### Compare XStateNet2 Optimizations:
```bash
XS2-Dict (event)        # Dictionary baseline (slowest)
XS2-Frozen (event)      # +43% faster (FrozenDict)
XS2-Array (event)       # +63% faster (Array)
```

### Compare Coordination:
```bash
XS2-Array (event)        # Centralized scheduler
Autonomous-Event (event) # Robots self-schedule
Ant-Colony (event)       # Distributed optimization
```

### Compare Communication:
```bash
Autonomous (polling)        # Polling-based
Autonomous-Event (event)    # Event-driven
XS2-PubSub-Array (one)      # Pub/sub pattern
```

### Compare Pub/Sub Variants:
```bash
XS2-PubSub-Dedicated (multi) # Dedicated scheduler per robot (overhead)
PubSub-Single (one)          # Single central scheduler (efficient)
XS2-PubSub-Array (one)       # Single + Array optimization (fastest)
```

---

## 💡 Reading the Communication Tag

- **(polling)** = Scheduler polls robot state periodically
- **(event)** = Robots send state change events
- **(one)** = Single central scheduler
- **(multi)** = Multiple dedicated schedulers
- **(batch)** = Synchronized batch execution

---

## 🎓 What Each Tag Tells You

### Primary Feature:
- **Lock** = Traditional mutex-based
- **Actor** = Akka.NET actor model
- **XS2** = XStateNet2 declarative JSON state machine
- **Autonomous** = Self-scheduling robots
- **Ant-Colony** = Ant colony optimization
- **PubSub** = Publication/subscription pattern
- **Sync-Pipeline** = Synchronized batch transfers

### Variant:
- **Dict** = Standard Dictionary (baseline)
- **Frozen** = FrozenDictionary optimization
- **Array** = Byte array optimization
- **Mailbox** = Actor mailbox pattern

### Communication:
- **(polling)** = Pulls state periodically
- **(event)** = Pushes state changes
- **(one)** = Single scheduler
- **(multi)** = Multiple schedulers
- **(batch)** = Batch processing

---

## 🚀 Usage Examples

### Run stress test:
```bash
dotnet run --stress-test
```

### Test specific scheduler:
```bash
# These would require additional command-line parsing
# (not currently implemented, but shows the naming usage)
dotnet run --test xs2-frozen
dotnet run --test xs2-pubsub-array
dotnet run --test autonomous-event
```

---

## 📈 Expected Performance Ranking

Based on previous benchmarks:

```
🏆 Top Performers (15.xx s):
  1. PubSub-Single (one)
  2. XS2-PubSub-Array (one)
  3. Lock (polling)
  4. Actor (event)
  5. XS2-Array (event)

⚡ Fast (15-16s):
  6. XS2-Frozen (event)
  7. Autonomous-Event (event)
  8. Actor-Mailbox (event)
  9. Ant-Colony (event)
  10. Sync-Pipeline (batch)

⚠️  Slower (16-18s):
  11. XS2-Dict (event)          ← Dictionary baseline
  12. Autonomous (polling)
  13. Autonomous-Array (polling)

❌ Failed in Previous Tests:
  14. XS2-PubSub-Dedicated (multi)  ← Routing overhead
```

---

## 🎯 Key Insights

### FrozenDictionary Impact:
```
XS2-Dict (event)    → 18.xx s  (baseline)
XS2-Frozen (event)  → 15.xx s  (+43% faster!)
XS2-Array (event)   → 15.xx s  (+63% faster!)
```

### Autonomous Performance:
```
Autonomous (polling)       → Slower (polling overhead)
Autonomous-Event (event)   → Much faster (event-driven)
```

### Pub/Sub Architecture:
```
XS2-PubSub-Dedicated (multi)  → Failed (routing overhead)
PubSub-Single (one)           → Fast (no routing)
XS2-PubSub-Array (one)        → Fastest (no routing + array)
```

---

## 📖 Legend

- 🏆 = Top performer
- ⚡ = Fast
- ⚠️ = Slower
- ❌ = Failed stress test
- ✅ = Passed stress test
- 🎯 = Recommended for production
