# Publication-Based Robot Scheduler

## Overview

The **Publication-Based Scheduler** implements a **dedicated scheduler per robot** architecture using a **publish/subscribe pattern** for state coordination.

This is the **10th scheduler implementation** in the CMPSimXS2 suite, representing a fundamentally different architectural approach.

## Key Concept

Instead of:
- ❌ Central scheduler dispatching work to robots
- ❌ Robots polling for available work
- ❌ Global coordination logic

We have:
- ✅ **Each robot has its own dedicated scheduler**
- ✅ **Robots and stations publish state changes**
- ✅ **Schedulers subscribe to relevant state publications**
- ✅ **Reactive, event-driven coordination**

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│              PublicationBasedScheduler (Orchestrator)           │
│                                                                 │
│  ┌──────────────────┐  ┌──────────────────┐  ┌──────────────┐ │
│  │ Robot 1 Dedicated│  │ Robot 2 Dedicated│  │ Robot 3 Ded. │ │
│  │    Scheduler     │  │    Scheduler     │  │  Scheduler   │ │
│  └────────┬─────────┘  └────────┬─────────┘  └──────┬───────┘ │
│           │                     │                    │         │
│           │  Subscribes to:     │                    │         │
│           │  - Robot 1 states   │                    │         │
│           │  - Carrier states   │                    │         │
│           │  - Polisher states  │                    │         │
│           │  - Buffer states    │                    │         │
│           │                     │                    │         │
└───────────┼─────────────────────┼────────────────────┼─────────┘
            │                     │                    │
            ▼                     ▼                    ▼
    ┌───────────────┐     ┌───────────────┐   ┌───────────────┐
    │   Robot 1     │     │   Robot 2     │   │   Robot 3     │
    │ State Publisher│    │ State Publisher│   │ State Publisher│
    └───────────────┘     └───────────────┘   └───────────────┘

    ┌───────────────┐     ┌───────────────┐   ┌───────────────┐
    │   Carrier     │     │   Polisher    │   │   Cleaner     │
    │ State Publisher│    │ State Publisher│   │ State Publisher│
    └───────────────┘     └───────────────┘   └───────────────┘
```

## Components

### 1. StatePublisherActor
- **Purpose**: Manages subscriptions and broadcasts state changes
- **Pattern**: Pub/Sub
- **Location**: `StatePublication.cs:35-88`

**Key Features**:
- Maintains list of subscribers
- Sends current state to new subscribers immediately
- Broadcasts state changes to all subscribers

### 2. DedicatedRobotScheduler
- **Purpose**: Autonomous scheduler for a single robot
- **Location**: `DedicatedRobotScheduler.cs`

**Key Features**:
- Subscribes to robot state changes
- Subscribes to relevant station state changes
- Maintains local queue of transfer requests
- Reacts to state publications
- Makes autonomous decisions

**Subscription Strategy**:
```csharp
Robot 1 Scheduler subscribes to:
- Robot 1 state (idle ↔ busy ↔ carrying)
- Carrier state
- Polisher state
- Buffer state

Robot 2 Scheduler subscribes to:
- Robot 2 state
- Polisher state
- Cleaner state

Robot 3 Scheduler subscribes to:
- Robot 3 state
- Cleaner state
- Buffer state
```

### 3. PublicationBasedScheduler
- **Purpose**: Orchestrator that implements IRobotScheduler
- **Location**: `PublicationBasedScheduler.cs`

**Responsibilities**:
- Creates state publishers for robots and stations
- Creates dedicated scheduler for each robot
- Routes transfer requests to appropriate dedicated scheduler
- Coordinates publication infrastructure

## Event Flow

### 1. Station State Change
```
1. Station changes state (e.g., Polisher: processing → done)
2. Station.CurrentState setter invokes OnStateChanged callback
3. PublicationBasedScheduler publishes state change
4. StatePublisherActor broadcasts to subscribers
5. DedicatedRobotScheduler receives notification
6. Scheduler checks if it can execute pending requests
7. If conditions met, executes transfer
```

### 2. Robot State Change
```
1. Robot completes transfer (carrying → idle)
2. PublicationBasedScheduler.UpdateRobotState() called
3. StatePublisherActor broadcasts robot state change
4. DedicatedRobotScheduler receives its own robot's state
5. Scheduler checks pending queue
6. If work available and conditions met, starts next transfer
```

### 3. Transfer Request
```
1. Transfer request submitted
2. PublicationBasedScheduler routes to appropriate dedicated scheduler
3. DedicatedRobotScheduler validates route
4. Request queued
5. Scheduler checks if can execute immediately
6. If not, waits for state change notifications
```

## Benefits

### 1. **Decentralized Decision Making**
- Each robot's scheduler makes autonomous decisions
- No central bottleneck
- Scales better with more robots

### 2. **Reactive Architecture**
- Event-driven, not polling-based
- State changes trigger immediate reaction
- Lower latency for state-dependent operations

### 3. **Clear Separation of Concerns**
- Each scheduler focuses on one robot
- Station state management separated
- Easy to reason about individual robot behavior

### 4. **Flexibility**
- Easy to customize scheduler logic per robot
- Can have different strategies for different robots
- State publication infrastructure is reusable

### 5. **Debuggability**
- Clear event flow
- Each scheduler's decisions are isolated
- State changes are explicit events

## Usage

### Command Line
```bash
# Run with publication-based scheduler
dotnet run --robot-pubsub

# Combined with journey scheduler
dotnet run --robot-pubsub --journey-xstate
```

### Expected Behavior
```
📡 ROBOT SCHEDULER: Publication-Based (Dedicated per Robot)-based
🔒 JOURNEY SCHEDULER: Lock-based

🔧 Initializing Akka ActorSystem...
🤖 Initializing RobotScheduler (Publication-Based (Dedicated per Robot)-based)...
[PublicationBasedScheduler] 📡 Initialized publication-based scheduler (prefix: pubsched-...)

💿 Creating 10 wafers (2 carriers × 5 wafers each)...
🔄 Initializing WaferJourneyScheduler (Lock-based)...
⚙️  Creating station actors...
🤖 Creating robot actors...

[PublicationBasedScheduler] 📡 Created state publisher for Robot 1
[DedicatedScheduler:Robot 1] 📡 Subscribed to robot state publications
[DedicatedScheduler:Robot 1] 📡 Subscribed to Carrier state publications
[DedicatedScheduler:Robot 1] 📡 Subscribed to Polisher state publications
[DedicatedScheduler:Robot 1] 📡 Subscribed to Buffer state publications
[DedicatedScheduler:Robot 1] ✅ Dedicated scheduler started (publication-based, event-driven)

... (similar for Robot 2 and Robot 3)

📡 Registering stations with publication-based scheduler...
[PublicationBasedScheduler] 📡 Registered station publisher: Polisher
[PublicationBasedScheduler] 📡 Published station state: Polisher → idle (wafer: )

✅ All components initialized!
```

## Comparison with Other Schedulers

| Feature | Lock | Actor | XState | Ant Colony | **PubSub** |
|---------|------|-------|--------|------------|------------|
| Architecture | Central | Central | Central | Decentralized | **Dedicated per Robot** |
| Coordination | Locks | Messages | State Machine | Work Pool | **State Publications** |
| Decision Making | Central | Central | Central | Autonomous | **Autonomous per Robot** |
| Event-Driven | ❌ | ✅ | ✅ | ✅ | **✅** |
| Polling | ❌ | ❌ | ❌ | ❌ | **❌** |
| Scalability | Medium | High | High | Very High | **Very High** |
| Debuggability | Medium | Medium | High | Low | **Very High** |

## Implementation Details

### State Publication Flow
```csharp
// Station state change triggers publication
station.CurrentState = "done"; // Property setter
  ↓
OnStateChanged?.Invoke("done", waferId); // Callback
  ↓
pubSubScheduler.UpdateStationState(name, "done", waferId); // Publish
  ↓
StatePublisherActor receives PublishStateMessage
  ↓
Broadcasts StateChangeEvent to all subscribers
  ↓
DedicatedRobotScheduler.HandleStateChange(evt)
  ↓
Scheduler reacts and potentially executes transfer
```

### Route-Based Subscription
Each dedicated scheduler only subscribes to stations relevant to its robot's routes:
```csharp
private HashSet<string> GetRelevantStations(string robotId)
{
    return robotId switch
    {
        "Robot 1" => new HashSet<string> { "Carrier", "Polisher", "Buffer" },
        "Robot 2" => new HashSet<string> { "Polisher", "Cleaner" },
        "Robot 3" => new HashSet<string> { "Cleaner", "Buffer" },
        _ => new HashSet<string>()
    };
}
```

## Design Principles

1. **Single Responsibility**: Each scheduler manages one robot
2. **Event-Driven**: State changes drive coordination
3. **Loose Coupling**: Publishers don't know subscribers
4. **Reactive**: Schedulers react to state publications
5. **Autonomous**: Each scheduler makes independent decisions

## Future Enhancements

Potential improvements:
- **Priority-based subscription**: High-priority schedulers get notifications first
- **Filtered publications**: Only publish relevant state changes
- **State history**: Track state change history for debugging
- **Dynamic subscription**: Subscribe/unsubscribe based on current needs
- **Metrics**: Track publication latency and scheduler reaction times

## Testing

The publication-based scheduler:
- ✅ Implements IRobotScheduler interface
- ✅ Works with existing journey schedulers
- ✅ Respects single-wafer rules
- ✅ Handles all 8-step wafer journey
- ✅ Processes two carriers successively

## Conclusion

The **Publication-Based Scheduler** represents a paradigm shift:
- **From**: Central coordination
- **To**: Distributed, reactive coordination

This architecture is particularly well-suited for:
- ✅ Complex multi-robot systems
- ✅ Systems requiring low-latency reactions to state changes
- ✅ Scenarios where robot behavior needs customization
- ✅ Debugging and monitoring individual robot behavior

---

**Icon**: 📡
**Command**: `--robot-pubsub`
**Status**: ✅ Implemented and tested
**Date**: 2025-11-02
