# InterMachine Circular Wait Deadlock

## The Problem

When two state machines simultaneously send messages to each other while in their action handlers, a circular wait deadlock occurs.

## The Deadlock Sequence

```
Time    Machine1                          Machine2
====    ========                          ========
T0      Receives "START" event           Receives "START" event
        |                                 |
T1      ACQUIRE Lock1 ✓                  ACQUIRE Lock2 ✓
        |                                 |
T2      Enter "sendPing" action          Enter "sendPing" action
        |                                 |
T3      Call SendToAsync("machine2",     Call SendToAsync("machine1",
        "PING")                           "PING")
        |                                 |
T4      InterMachineConnector tries      InterMachineConnector tries
        to call:                          to call:
        machine2.SendAsync("PING")        machine1.SendAsync("PING")
        |                                 |
T5      Try ACQUIRE Lock2 ✗              Try ACQUIRE Lock1 ✗
        (Blocked - M2 holds it)           (Blocked - M1 holds it)
        |                                 |
T6      DEADLOCK!                        DEADLOCK!
        Waiting for Lock2                 Waiting for Lock1
```

## Visual Representation

```
   Machine1                    Machine2
   ┌─────────────┐            ┌─────────────┐
   │ Holds Lock1 │            │ Holds Lock2 │
   └─────┬───────┘            └─────┬───────┘
         │                           │
         │  Wants Lock2              │  Wants Lock1
         │      ↓                    │      ↓
         └──────────────┬────────────┘
                        │
                   CIRCULAR WAIT
                     DEADLOCK!
```

## Code Flow Analysis

### 1. Initial State - Both Machines Start
```csharp
// Test code starts both machines simultaneously
await Task.WhenAll(
    machine1.SendAsync("START"),
    machine2.SendAsync("START")
);
```

### 2. Both Enter Actions While Holding Locks
```csharp
// Machine1's sendPing action (holding Lock1)
new NamedAction("sendPing", async (sm) => {
    var cm1 = session.GetMachine("machine1");
    await cm1.SendToAsync("machine2", "PING");  // <- Needs Lock2
    sm.SendAndForget("SENT");
})

// Machine2's sendPing action (holding Lock2)
new NamedAction("sendPing", async (sm) => {
    var cm2 = session.GetMachine("machine2");
    await cm2.SendToAsync("machine1", "PING");  // <- Needs Lock1
    sm.SendAndForget("SENT");
})
```

### 3. InterMachineConnector Delivers Messages
```csharp
// In InterMachineConnector.cs
public async Task SendAsync(string from, string to, string eventName, object data)
{
    // ...
    var targetMachine = _machines[toMachineId];
    await targetMachine.SendAsync(eventName, data);  // <- Tries to acquire target's lock!
}
```

## Why Thread-Safe Mode Fixes It

When `threadSafe: true` is enabled:

1. **EventQueue Creation**: Each machine gets an EventQueue backed by .NET Channels
2. **Asynchronous Processing**: Events are queued and processed asynchronously
3. **Lock Release Before Inter-Machine Call**: The EventQueue mechanism allows the lock to be released before attempting to send to another machine
4. **Breaking the Circular Wait**: Since events are queued, there's no immediate lock acquisition attempt

## Alternative Solutions

### 1. Asynchronous Inter-Machine Delivery (Recommended)
Modify `InterMachineConnector` to queue events instead of direct delivery:
```csharp
// Instead of direct send:
await targetMachine.SendAsync(eventName, data);

// Use fire-and-forget:
targetMachine.SendAndForget(eventName, data);
// Or use Task.Run:
_ = Task.Run(() => targetMachine.SendAsync(eventName, data));
```

### 2. Design Pattern Change
Avoid simultaneous bidirectional communication:
- Use request-response patterns
- Implement message ordering/sequencing
- Add delays or randomization to prevent simultaneous sends

### 3. Lock-Free Message Passing
Implement a true actor model with lock-free message queues for inter-machine communication.

## Conclusion

The deadlock is not about machines sending events to themselves, but about **circular dependency** when two machines try to send messages to each other simultaneously while holding their respective locks. The thread-safe EventQueue mode breaks this circular wait by queueing events instead of processing them immediately.