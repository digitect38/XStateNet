# XStateNet Deadlock Analysis: Lock Acquisition Sequence

## The Deadlock Scenario

When a state machine action tries to send an event to itself, a deadlock occurs due to the lock acquisition sequence.

## Lock Acquisition Sequence Diagram

```
Thread 1 (Initial Event Processing)
====================================

1. External Call: machine.SendAsync("START")
   │
   ├─► ACQUIRE: _stateLock.WriteLock ✓
   │   (StateMachine.cs line ~490)
   │
   ├─► Process Event "START"
   │   │
   │   ├─► Find Transition: idle → sending
   │   │
   │   ├─► Execute Transition
   │   │   │
   │   │   └─► Execute Entry Actions for "sending" state
   │   │       │
   │   │       └─► Execute action: "sendPing"
   │   │           │
   │   │           ├─► Action code runs...
   │   │           │   - Sends message to other machine ✓
   │   │           │   - Logs messages ✓
   │   │           │
   │   │           └─► Calls: sm.SendAsync("SENT") ← PROBLEM!
   │   │               │
   │   │               └─► Try to ACQUIRE: _stateLock.WriteLock ✗
   │   │                   │
   │   │                   └─► DEADLOCK!
   │   │                       (Same thread already holds the lock)
   │   │
   │   └─► [Never reaches here - stuck in action]
   │
   └─► [Never reaches here - can't release lock]


Legend:
  ✓ = Success
  ✗ = Blocked/Failed
  ← = Problem point
```

## Detailed Lock Flow

### Step-by-Step Breakdown:

1. **Initial Event Reception**
   ```
   User Code: await machine.SendAsync("START")
   ```

2. **Lock Acquisition (First Lock)**
   ```csharp
   // StateMachine.cs SendInternal() method
   _stateLock.EnterWriteLock();  // Thread 1 acquires lock
   try {
       // All transition logic happens here...
   ```

3. **State Transition Processing**
   ```
   State: idle → sending
   Lock Status: HELD by Thread 1
   ```

4. **Entry Action Execution**
   ```csharp
   // Still inside the lock!
   // Executing entry action for "sending" state
   ExecuteActions(entryActions);  // Calls "sendPing" action
   ```

5. **Inside the Action (Still Holding Lock)**
   ```csharp
   // In the sendPing action:
   async (sm) => {
       // Do some work...
       await cm1.SendToAsync("machine2", "PING");  // ✓ Works (different machine)

       // Try to send event to self:
       await sm.SendAsync("SENT");  // ✗ DEADLOCK HERE!
   }
   ```

6. **Deadlock Point**
   ```csharp
   // sm.SendAsync("SENT") internally calls:
   _stateLock.EnterWriteLock();  // ✗ Can't acquire - same thread already holds it!
   ```

## Why Fire-and-Forget Doesn't Solve It

Even with fire-and-forget pattern:

```
Thread 1 (Holds Lock)          Thread Pool Thread
===================           ==================

Holds _stateLock.WriteLock
│
├─► Execute Action
│   │
│   └─► Task.Run(() => {
│       │                     Starts execution
│       │                     │
│       │                     ├─► await sm.SendAsync("SENT")
│       │                     │   │
│       │                     │   └─► Try ACQUIRE _stateLock ✗
│       │                     │       │
│       │                     │       └─► BLOCKED!
│       │                     │           (Waiting for Thread 1)
│       │                     │
│       └─────────────────────┘
│
├─► Action completes
│
├─► [More transition logic...]
│
└─► RELEASE _stateLock
    │
    └─────────────────────────────► Thread Pool Thread can now acquire lock
                                     │
                                     └─► Process "SENT" event (too late!)
```

## The Lock Hierarchy

```
┌─────────────────────────────────────┐
│         _stateLock (WriteLock)       │
│  ┌─────────────────────────────────┐ │
│  │   State Transition Logic        │ │
│  │  ┌─────────────────────────┐   │ │
│  │  │   Execute Actions       │   │ │
│  │  │  ┌──────────────────┐  │   │ │
│  │  │  │ Action Code      │  │   │ │
│  │  │  │ ├─ External IO ✓ │  │   │ │
│  │  │  │ └─ SendAsync ✗   │  │   │ │  ← Can't re-enter lock!
│  │  │  └──────────────────┘  │   │ │
│  │  └─────────────────────────┘   │ │
│  └─────────────────────────────────┘ │
└─────────────────────────────────────┘
```

## Lock Acquisition Timeline

```
Time →
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

T0: External event "START" arrives
    │
T1: ├─► Thread-1: ACQUIRE _stateLock ✓
    │
T2: ├─► Thread-1: Begin transition (idle → sending)
    │
T3: ├─► Thread-1: Start executing entry actions
    │
T4: ├─► Thread-1: Inside "sendPing" action
    │   │
T5: │   ├─► Thread-1: Calls sm.SendAsync("SENT")
    │   │
T6: │   ├─► Thread-1: Tries to ACQUIRE _stateLock ✗ [BLOCKED]
    │   │
    │   │   ⚠️ DEADLOCK: Thread-1 waiting for Thread-1!
    │   │
    │   └─► [Infinite wait...]
    │
    └─► [Never releases lock]

Alternative with Task.Run:
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

T0: External event "START" arrives
    │
T1: ├─► Thread-1: ACQUIRE _stateLock ✓
    │
T2: ├─► Thread-1: Begin transition
    │
T3: ├─► Thread-1: Execute action
    │   │
T4: │   ├─► Thread-1: Task.Run(() => sm.SendAsync("SENT"))
    │   │   │
T5: │   │   └─► Thread-2: Starts execution
    │   │       │
T6: │   │       └─► Thread-2: Tries ACQUIRE _stateLock ✗ [BLOCKED]
    │   │
T7: ├─► Thread-1: Action completes
    │
T8: ├─► Thread-1: Finish transition
    │
T9: └─► Thread-1: RELEASE _stateLock
        │
T10:    └─► Thread-2: ACQUIRE _stateLock ✓ (Finally!)
            │
T11:        └─► Thread-2: Process "SENT" event
```

## The Core Problem

The state machine uses a **ReaderWriterLockSlim** with write lock for all transitions to ensure atomicity. This means:

1. **One transition at a time**: Only one thread can be transitioning the state machine
2. **Actions run inside the lock**: Entry/exit actions execute while the lock is held
3. **No re-entrancy**: The same thread cannot acquire the write lock twice
4. **Recursive events impossible**: You cannot send events to self from within actions

## Solutions

### ❌ What Doesn't Work:
- `await sm.SendAsync()` - Direct deadlock
- `Task.Run(() => sm.SendAsync())` - Delayed deadlock (waits for lock release)
- `sm.Send()` (synchronous) - Same deadlock
- `ThreadPool.QueueUserWorkItem()` - Same as Task.Run

### ✅ What Works:
1. **Don't send events to self from actions**
   - Use state machine configuration for transitions
   - Let actions only perform side effects

2. **Use external orchestration**
   - Have the test/caller manage the state flow
   - Send events from outside the state machine

3. **Queue events for later processing**
   - Store events to be sent after action completes
   - Process them after lock is released (requires framework changes)

4. **Redesign state machine**
   - Use guards instead of actions for decisions
   - Use automatic transitions in state configuration
   - Separate "doing work" from "state management"

## Conclusion

The deadlock is a fundamental consequence of XStateNet's locking strategy. The framework prioritizes:
- **Thread safety**: Prevents race conditions
- **Atomicity**: Ensures transitions complete fully
- **Consistency**: Maintains state integrity

This comes at the cost of preventing recursive event sending from within actions. This is a design trade-off, not a bug.