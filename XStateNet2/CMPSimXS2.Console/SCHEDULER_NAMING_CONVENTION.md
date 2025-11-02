# 📋 Scheduler Naming Convention

## Naming Pattern

```
[Coordination] + [State Engine] + [Optimization] + [Sync Strategy] + [Communication]
```

## Feature Dimensions

### 1. **Coordination Model**
- `centralized` - Single central scheduler
- `autonomous` - Robots self-schedule
- `ant-colony` - Distributed ant colony optimization

### 2. **State Engine**
- `lock` - Lock-based (mutex)
- `actor` - Pure Akka.NET actor
- `xstate` - XState JSON machine

### 3. **Optimization Level**
- `dict` - Standard Dictionary
- `frozen-dict` - FrozenDictionary optimization
- `array-opti` - Byte array optimization

### 4. **Synchronization Strategy**
- `no-sync` - No synchronization (opportunistic)
- `sync-pipe` - Synchronized pipeline (batch transfers)

### 5. **Communication Pattern**
- `polling` - Polling-based state checks
- `event-driven` - Event-driven updates
- `pub-sub` - Publication/subscription pattern

---

## 🎯 Complete Scheduler Matrix

| Old Name | New Name | File | Features |
|----------|----------|------|----------|
| **Lock-based** | `centralized-lock-polling` | `RobotScheduler.cs` | Lock-based, polling |
| **Actor-based** | `centralized-actor-event-driven` | `RobotSchedulerActorProxy.cs` | Pure actor, event-driven |
| **XState (Dict)** | `centralized-xstate-dict-event-driven` | `RobotSchedulerXStateDict.cs` | XState + Dictionary baseline |
| **XState (FrozenDict)** | `centralized-xstate-frozen-dict-event-driven` | `RobotSchedulerXState.cs` | XState + FrozenDict |
| **XState (Array)** | `centralized-xstate-array-opti-event-driven` | `RobotSchedulerXStateArray.cs` | XState + Array |
| **Autonomous** | `autonomous-lock-polling` | `AutonomousRobotScheduler.cs` | Autonomous + Lock |
| **Hybrid** | `autonomous-array-opti-polling` | `AutonomousArrayScheduler.cs` | Autonomous + Array |
| **Event-Driven** | `autonomous-array-opti-event-driven` | `EventDrivenHybridScheduler.cs` | Autonomous + Events |
| **Actor Mailbox** | `centralized-actor-mailbox-event-driven` | `ActorMailboxEventDrivenScheduler.cs` | Actor mailbox pattern |
| **Ant Colony** | `ant-colony-actor-event-driven` | `AntColonyScheduler.cs` | Ant colony optimization |
| **Publication-Based** | `centralized-xstate-frozen-dict-pub-sub-dedicated` | `PublicationBasedScheduler.cs` | Pub/sub with dedicated schedulers |
| **Single Publication** | `centralized-actor-pub-sub-single` | `SinglePublicationScheduler.cs` | Pub/sub with single scheduler |
| **Array Single Publication** | `centralized-xstate-array-opti-pub-sub-single` | `SinglePublicationSchedulerXState.cs` | Array + Pub/sub single |
| **Synchronized Pipeline** | `centralized-actor-pub-sub-sync-pipe` | `SynchronizedPipelineScheduler.cs` | Synchronized batch transfers |

---

## 📊 Simplified Display Names (for Stress Test Output)

For readability, we'll use abbreviated names in test output:

| Code | Display Name | Features |
|------|--------------|----------|
| `lock` | `Lock (polling)` | Baseline lock-based |
| `actor` | `Actor (event)` | Pure actor model |
| `xstate-dict` | `XState-Dict (event)` | XState + Dictionary |
| `xstate-frozen` | `XState-Frozen (event)` | XState + FrozenDict |
| `xstate-array` | `XState-Array (event)` | XState + Array optimization |
| `autonomous` | `Autonomous (polling)` | Self-scheduling |
| `autonomous-array` | `Autonomous-Array (polling)` | Self-scheduling + Array |
| `autonomous-event` | `Autonomous-Event (event)` | Self-scheduling + Events |
| `actor-mailbox` | `Actor-Mailbox (event)` | Actor mailbox pattern |
| `ant-colony` | `Ant-Colony (event)` | Ant colony optimization |
| `pubsub-dedicated` | `PubSub-Dedicated (multi)` | Pub/sub + dedicated schedulers |
| `pubsub-single` | `PubSub-Single (one)` | Pub/sub + single scheduler |
| `pubsub-array` | `PubSub-Array (one)` | Pub/sub + Array + single |
| `sync-pipe` | `Sync-Pipeline (batch)` | Synchronized batch transfers |

---

## 🎨 Color-Coded Categories (for documentation)

### By Coordination Model:
- 🏢 **Centralized**: `lock`, `actor`, `xstate-*`, `pubsub-*`, `sync-pipe`
- 🤖 **Autonomous**: `autonomous`, `autonomous-array`, `autonomous-event`
- 🐜 **Ant Colony**: `ant-colony`

### By State Engine:
- 🔒 **Lock**: `lock`, `autonomous`
- 🎭 **Actor**: `actor`, `actor-mailbox`, `ant-colony`, `pubsub-single`, `sync-pipe`
- 🔄 **XState**: `xstate-dict`, `xstate-frozen`, `xstate-array`, `pubsub-dedicated`, `pubsub-array`

### By Optimization:
- 📚 **Dictionary**: `xstate-dict`
- ❄️ **FrozenDict**: `xstate-frozen`, `pubsub-dedicated`
- ⚡ **Array**: `xstate-array`, `autonomous-array`, `autonomous-event`, `pubsub-array`

### By Communication:
- 📞 **Polling**: `lock`, `autonomous`, `autonomous-array`
- ⚡ **Event-Driven**: `actor`, `xstate-*`, `autonomous-event`, `actor-mailbox`
- 📡 **Pub/Sub**: `pubsub-*`, `sync-pipe`

---

## 🔍 Quick Feature Lookup

### Want the fastest?
→ `xstate-array` or `pubsub-array` (Array optimization)

### Want the simplest?
→ `lock` (Traditional lock-based)

### Want actor model?
→ `actor` or `actor-mailbox`

### Want declarative state machine?
→ `xstate-frozen` (Standard XState)

### Want autonomous robots?
→ `autonomous-event` (Best autonomous variant)

### Want synchronized batch transfers?
→ `sync-pipe` (Pipeline synchronization)

### Want to measure FrozenDict benefit?
→ Compare `xstate-dict` vs `xstate-frozen` vs `xstate-array`

---

## 📝 Implementation Guide

### StressTest.cs Mapping

```csharp
private static IRobotScheduler CreateScheduler(ActorSystem actorSystem, string code)
{
    return code switch
    {
        // Lock-based
        "lock" => new RobotScheduler(),

        // Pure Actor
        "actor" => new RobotSchedulerActorProxy(actorSystem, $"stress-{code}"),

        // XState Variants (Dict → FrozenDict → Array)
        "xstate-dict" => new RobotSchedulerXStateDict(actorSystem, $"stress-{code}"),
        "xstate-frozen" => new RobotSchedulerXState(actorSystem, $"stress-{code}"),
        "xstate-array" => new RobotSchedulerXStateArray(actorSystem, $"stress-{code}"),

        // Autonomous Variants
        "autonomous" => new AutonomousRobotScheduler(),
        "autonomous-array" => new AutonomousArrayScheduler(),
        "autonomous-event" => new EventDrivenHybridScheduler(),

        // Actor Mailbox
        "actor-mailbox" => new ActorMailboxEventDrivenScheduler(actorSystem, $"stress-{code}"),

        // Ant Colony
        "ant-colony" => new AntColonyScheduler(actorSystem, $"stress-{code}"),

        // Pub/Sub Variants
        "pubsub-dedicated" => new PublicationBasedScheduler(actorSystem, $"stress-{code}"),
        "pubsub-single" => new SinglePublicationScheduler(actorSystem, $"stress-{code}"),
        "pubsub-array" => new SinglePublicationSchedulerXState(actorSystem, $"stress-{code}"),

        // Synchronized Pipeline
        "sync-pipe" => new SynchronizedPipelineScheduler(actorSystem, $"stress-{code}"),

        _ => new RobotScheduler()
    };
}
```

---

## 🎯 Benefits of This Naming Convention

1. **Self-Documenting**: Name tells you exactly what features it has
2. **Systematic**: Easy to create new variants following the pattern
3. **Comparable**: Easy to see what differs between variants
4. **Searchable**: Can grep by feature (e.g., all "event-driven")
5. **Educational**: Clear for learning architecture patterns

---

## 📚 Example Comparisons

### Compare State Engines:
```bash
# Lock-based vs Actor vs XState
lock              # Lock + polling
actor             # Actor + event-driven
xstate-frozen     # XState + FrozenDict + event-driven
```

### Compare Optimizations:
```bash
# Dictionary → FrozenDict → Array
xstate-dict       # Baseline Dictionary
xstate-frozen     # +43% (FrozenDict)
xstate-array      # +63% (Array)
```

### Compare Coordination:
```bash
# Centralized vs Autonomous
xstate-array      # Centralized
autonomous-event  # Autonomous (robots self-schedule)
```

### Compare Communication:
```bash
# Polling vs Events vs Pub/Sub
autonomous        # Polling
autonomous-event  # Event-driven
pubsub-array      # Pub/Sub
```

---

## 🚀 Migration Path

**From old naming to new:**

1. Update `StressTest.cs` test names
2. Update `CreateScheduler()` switch cases
3. Keep file names unchanged (to avoid breaking imports)
4. Update documentation references

**Backward compatibility:**

Old codes can map to new codes via alias:
```csharp
"array" => "xstate-array",           // Alias
"eventdriven" => "autonomous-event",  // Alias
"singlepub" => "pubsub-single",       // Alias
// etc.
```

This allows gradual migration without breaking existing scripts.
