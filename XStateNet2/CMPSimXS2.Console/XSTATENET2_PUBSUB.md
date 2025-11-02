# XStateNet2 Native Pub/Sub Features

## Overview

**YES, XStateNet2 has native pub/sub features!**

XStateNet2's `StateMachineActor` includes built-in support for state change notifications using the **Subscribe/StateChanged pattern**.

## Native Pub/Sub Messages

### 1. Subscribe Message
```csharp
// From: XStateNet2.Core.Messages.Messages.cs:93
public record Subscribe : IStateMachineMessage;
```

**Usage:**
```csharp
// Subscribe to state machine's state changes
stateMachineActor.Tell(new Subscribe());
```

### 2. Unsubscribe Message
```csharp
// From: XStateNet2.Core.Messages.Messages.cs:98
public record Unsubscribe : IStateMachineMessage;
```

**Usage:**
```csharp
// Unsubscribe from state changes
stateMachineActor.Tell(new Unsubscribe());
```

### 3. StateChanged Notification
```csharp
// From: XStateNet2.Core.Messages.Messages.cs:74-78
public record StateChanged(
    string PreviousState,
    string CurrentState,
    SendEvent? TriggeringEvent
) : IStateMachineMessage;
```

**Automatically sent to subscribers when state transitions occur.**

## How It Works

### Implementation in StateMachineActor

```csharp
// From StateMachineActor.cs:18
private readonly HashSet<IActorRef> _subscribers = new();

// Subscribe handler (line 98-102)
Receive<Subscribe>(_ =>
{
    _subscribers.Add(Sender);
    _log.Debug($"[{_script.Id}] Subscriber added: {Sender.Path}");
});

// Unsubscribe handler (line 103-107)
Receive<Unsubscribe>(_ =>
{
    _subscribers.Remove(Sender);
    _log.Debug($"[{_script.Id}] Subscriber removed: {Sender.Path}");
});

// Notification broadcast (line 1298-1304)
private void NotifyStateChanged(string previousState, string currentState, SendEvent? evt)
{
    var notification = new StateChanged(previousState, currentState, evt);

    // Direct notification to subscribers
    foreach (var subscriber in _subscribers)
    {
        subscriber.Tell(notification);
    }

    // Also publish to event stream
    Context.System.EventStream.Publish(notification);
}
```

## Two Publication Mechanisms

### 1. Direct Subscription (Targeted)
- **Subscribe** to specific state machine
- Receive **StateChanged** notifications only from that machine
- Direct actor-to-actor communication
- **Use case**: When you want notifications from specific state machines

```csharp
// Subscriber actor
Receive<StateChanged>(msg =>
{
    Console.WriteLine($"State changed: {msg.PreviousState} → {msg.CurrentState}");
});

// Subscribe to a specific state machine
stateMachineActor.Tell(new Subscribe());
```

### 2. EventStream (Broadcast)
- **All** state changes published to `ActorSystem.EventStream`
- **System-wide** listening
- Any actor can subscribe to EventStream
- **Use case**: Monitoring, logging, debugging

```csharp
// Subscribe to all StateChanged events in the system
Context.System.EventStream.Subscribe(Self, typeof(StateChanged));

Receive<StateChanged>(msg =>
{
    Console.WriteLine($"System-wide: {msg.PreviousState} → {msg.CurrentState}");
});
```

## Usage Examples

### Example 1: Robot Scheduler Subscribing to Robot State

```csharp
public class RobotSchedulerActor : ReceiveActor
{
    public RobotSchedulerActor(IActorRef robotStateMachine)
    {
        // Subscribe using XStateNet2's native Subscribe
        robotStateMachine.Tell(new Subscribe());

        // Handle state change notifications
        Receive<StateChanged>(msg =>
        {
            if (msg.CurrentState == "idle")
            {
                // Robot is idle, assign work!
                AssignWork();
            }
        });
    }

    private void AssignWork()
    {
        // Assign transfer request to robot
    }
}
```

### Example 2: Monitoring All State Machines

```csharp
public class StateMonitorActor : ReceiveActor
{
    public StateMonitorActor()
    {
        // Subscribe to ALL state changes in the system
        Context.System.EventStream.Subscribe(Self, typeof(StateChanged));

        Receive<StateChanged>(msg =>
        {
            // Log all state transitions
            Console.WriteLine($"[Monitor] {msg.PreviousState} → {msg.CurrentState}");
        });
    }
}
```

### Example 3: Multi-Robot Coordination

```csharp
public class CoordinatorActor : ReceiveActor
{
    private readonly Dictionary<IActorRef, string> _robotStates = new();

    public CoordinatorActor(List<IActorRef> robots)
    {
        // Subscribe to all robots
        foreach (var robot in robots)
        {
            robot.Tell(new Subscribe());
            _robotStates[robot] = "unknown";
        }

        Receive<StateChanged>(msg =>
        {
            // Track which robot sent this
            _robotStates[Sender] = msg.CurrentState;

            // Coordinate based on all robot states
            CoordinateRobots();
        });
    }
}
```

## Comparison: Custom vs Native Pub/Sub

### Custom StatePublisherActor (Current CMPSimXS2 Implementation)
```csharp
// Pros:
✅ Works for non-state-machine entities (simple models)
✅ Consistent interface for robots and stations
✅ Doesn't require everything to be a state machine
✅ Can include custom metadata

// Cons:
❌ Duplicate implementation (XStateNet2 already has this!)
❌ Extra actors to manage
❌ More code to maintain
```

### XStateNet2 Native Pub/Sub
```csharp
// Pros:
✅ Built into XStateNet2 - no extra code!
✅ Automatic EventStream publishing
✅ Well-tested and maintained
✅ Includes triggering event in notification
✅ Integrated with state machine lifecycle

// Cons:
❌ Only works for StateMachineActor instances
❌ Stations need to be wrapped in state machines
```

## Recommended Approach

### For Robot FSMs
**Use XStateNet2's native pub/sub**:
```csharp
// Robots are already StateMachineActors
robotStateMachine.Tell(new Subscribe());

Receive<StateChanged>(msg =>
{
    // React to robot state changes
});
```

### For Stations (if they're StateMachineActors)
**Use XStateNet2's native pub/sub**:
```csharp
// If stations are state machines
stationStateMachine.Tell(new Subscribe());
```

### For Simple Models (non-state-machines)
**Use custom StatePublisherActor**:
```csharp
// For simple models without state machines
// Custom wrapper is fine
```

## Hybrid Approach (Best of Both Worlds)

```csharp
// Use XStateNet2 native for state machines
robotStateMachine.Tell(new Subscribe());

// Use custom publisher for simple models
var stationPublisher = system.ActorOf<StatePublisherActor>(stationName);
stationPublisher.Tell(new StatePublisherActor.SubscribeMessage(Self));

// Handle both!
Receive<StateChanged>(HandleRobotStateChange);
Receive<StateChangeEvent>(HandleStationStateChange);
```

## Performance Considerations

### Direct Subscription
- **Low overhead**: Direct `Tell` to subscribers
- **Fast**: No serialization if same machine
- **Scales**: O(n) where n = number of subscribers

### EventStream
- **Higher overhead**: Akka EventStream dispatch
- **Flexible**: System-wide listening
- **Filter by type**: Only receive relevant events

## Debugging Tips

### Enable Logging
```csharp
// StateMachineActor logs subscriptions at Debug level
_log.Debug($"[{_script.Id}] Subscriber added: {Sender.Path}");
```

### Monitor All State Changes
```csharp
// Create monitor actor
var monitor = system.ActorOf<StateMonitorActor>("monitor");

// All StateChanged events will be logged
```

## Conclusion

**XStateNet2 has excellent native pub/sub features!**

Key points:
1. ✅ **Subscribe/Unsubscribe** messages for targeted subscriptions
2. ✅ **StateChanged** notifications with full state info
3. ✅ **EventStream** publishing for system-wide monitoring
4. ✅ Built-in, tested, and maintained
5. ✅ Integrated with state machine lifecycle

**Use native pub/sub when:**
- Working with StateMachineActors
- Want automatic EventStream publishing
- Need integrated state transition notifications

**Use custom pub/sub when:**
- Working with simple models (non-state-machines)
- Need custom metadata
- Want different notification format

---

**Related Files:**
- `XStateNet2.Core/Messages/Messages.cs:93-98` - Subscribe/Unsubscribe
- `XStateNet2.Core/Messages/Messages.cs:74-78` - StateChanged
- `XStateNet2.Core/Actors/StateMachineActor.cs:18` - Subscribers
- `XStateNet2.Core/Actors/StateMachineActor.cs:98-107` - Subscribe handling
- `XStateNet2.Core/Actors/StateMachineActor.cs:1295-1305` - Notification

**Date:** 2025-11-02
